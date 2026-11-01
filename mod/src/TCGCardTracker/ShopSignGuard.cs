using System;
using HarmonyLib;

namespace TCGCardTracker
{
    /// <summary>
    /// Keeps TCG Studio's shop sign code from stopping the day clock on game 1.03+.
    ///
    /// TCG Studio (1.14.0) applies its shop sign settings from a Harmony postfix on LightManager.Awake. On the game's
    /// Unity 6 build its sign-text code refers to a TextMeshPro field that no longer exists (TMP_Asset.material), so
    /// ShopSign.Apply throws MissingFieldException. That exception leaves LightManager.Awake, and Unity disables a
    /// component whose Awake throws: LightManager then never subscribes to the "game data loaded" event, never runs
    /// its Init or Update, and the clock stays blank with time frozen (the day never ends).
    ///
    /// A Harmony finalizer on ShopSign.Apply swallows that error (logged once), so LightManager.Awake finishes and the
    /// clock runs. TCG Studio's custom shop sign picture and text still don't apply on 1.03 (they never could); the
    /// vanilla sign shows.
    ///
    /// WORKAROUND (added 2026-11-01, tracker 0.15.1, TCG Studio 1.14.0, game 1.03): delete this file and its Install()
    /// call in Plugin.Awake once a TCG Studio release no longer logs that MissingFieldException in Player.log.
    /// </summary>
    internal static class ShopSignGuard
    {
        private const string ShopSign = "TCGCustomCards.Runtime.ShopSign";
        private static bool _logged;

        public static void Install()
        {
            try
            {
                var type = AccessTools.TypeByName(ShopSign);
                if (type == null) return; // TCG Studio isn't installed (or renamed it): nothing to guard.
                var apply = AccessTools.Method(type, "Apply");
                if (apply == null) return;
                new Harmony(Plugin.Guid).Patch(apply, finalizer: new HarmonyMethod(typeof(ShopSignGuard), nameof(Finalizer)));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not guard TCG Studio's shop sign: " + e.Message);
            }
        }

        /// <summary>Harmony finalizer on TCG Studio's ShopSign.Apply: any error stays inside it.</summary>
        private static Exception? Finalizer(Exception? __exception)
        {
            if (__exception != null && !_logged)
            {
                _logged = true;
                Plugin.Log.LogWarning("TCG Studio's shop sign failed on this game version (" + __exception.GetType().Name + ": " +
                    __exception.Message + "). Kept it from stopping the shop clock; the sign stays vanilla.");
            }
            return null;
        }
    }
}
