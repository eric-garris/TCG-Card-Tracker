using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;

namespace TCGCardTracker
{
    /// <summary>
    /// Keeps TCG Studio's load-time "orphan cleanup" from deleting base-game items that game 1.03 added.
    ///
    /// TCG Studio (1.14.0) removes save references to custom items that are no longer installed. It decides an item is
    /// "custom" when its number is at or past EItemType.Max, and that Max is the value of the game it was built against
    /// (before 1.03), baked into its DLL. Game 1.03 added new base-game items past that old Max, among them the workbench's
    /// bulk boxes (BulkBoxNG/HG/MG/PG/UG/OG). On every load TCG Studio took those
    /// for leftovers of a removed set and cleared them out of shelves and other save data, so bundled bulk boxes vanished
    /// together with their shelf's price tag.
    ///
    /// The tracker wraps TCG Studio's SaveStripper.CleanOrphans: just while it runs, the base-game item numbers of the
    /// running game (read from the game's own enum, so this follows future updates) are listed in TCG Studio's item table,
    /// so its check sees them as installed and leaves them alone. They are taken out again right after (also on error).
    /// Saving, restoring and real custom items are not affected.
    ///
    /// WORKAROUND (added 2026-11-06, tracker 0.19.1, TCG Studio 1.14.0, game 1.03): delete this file and its Install()
    /// call in Plugin.Awake once a TCG Studio release built for game 1.03 keeps bulk boxes on reload without it.
    /// </summary>
    internal static class StudioItemGuard
    {
        private const string Stripper = "TCGCustomCards.Save.SaveStripper";
        private const string Registry = "TCGCustomCards.Runtime.Registry";

        private static IDictionary? _table;
        private static readonly List<int> Added = new List<int>();
        private static bool _logged;

        public static void Install()
        {
            try
            {
                var stripper = AccessTools.TypeByName(Stripper);
                var registry = AccessTools.TypeByName(Registry);
                if (stripper == null || registry == null) return; // TCG Studio isn't installed (or renamed it): nothing to guard.
                var clean = AccessTools.Method(stripper, "CleanOrphans");
                _table = AccessTools.Field(registry, "ByAccessoryItem")?.GetValue(null) as IDictionary;
                if (clean == null || _table == null)
                {
                    Plugin.Log.LogWarning("Could not guard TCG Studio's save cleanup: this TCG Studio version looks different.");
                    return;
                }
                new Harmony(Plugin.Guid + ".studioitems").Patch(clean,
                    prefix: new HarmonyMethod(typeof(StudioItemGuard), nameof(Prefix)),
                    finalizer: new HarmonyMethod(typeof(StudioItemGuard), nameof(Finalizer)));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not guard TCG Studio's save cleanup: " + e.Message);
            }
        }

        /// <summary>Lists the running game's own item numbers in TCG Studio's item table while its cleanup runs.</summary>
        private static void Prefix()
        {
            Added.Clear();
            if (_table == null) return;
            try
            {
                foreach (EItemType item in Enum.GetValues(typeof(EItemType)))
                {
                    int v = (int)item;
                    if (v < 0 || item.ToString() == "Max" || _table.Contains(v)) continue;
                    _table.Add(v, null);
                    Added.Add(v);
                }
                if (!_logged)
                {
                    _logged = true;
                    Plugin.Log.LogInfo("TCG Studio save cleanup: base-game items (bulk boxes and others new in game 1.03) are kept.");
                }
            }
            catch (Exception e)
            {
                Plugin.LogOnce("studio-items", "Could not protect base-game items from TCG Studio's save cleanup: " + e.Message);
            }
        }

        /// <summary>Takes them out again, whether the cleanup finished or threw (its error is passed on unchanged).</summary>
        private static Exception? Finalizer(Exception? __exception)
        {
            try
            {
                if (_table != null)
                    foreach (int v in Added) _table.Remove(v);
            }
            catch (Exception e)
            {
                Plugin.LogOnce("studio-items-undo", "Could not tidy TCG Studio's item table after its save cleanup: " + e.Message);
            }
            Added.Clear();
            return __exception;
        }
    }
}
