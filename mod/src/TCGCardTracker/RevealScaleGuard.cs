using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;

namespace TCGCardTracker
{
    /// <summary>
    /// Keeps TCG Studio packs with more than 7 cards (e.g. imported Yu-Gi-Oh and Pokemon sets) from freezing the
    /// pack opening screen on game 1.03+.
    ///
    /// Game 1.03 added CardOpeningSequence.m_CardBaseScaleList: one base scale per card slot, filled once in Start
    /// from vanilla's 7 slots, and read by SetCardRevealScale(index) for every slot in m_Card3dUIList. TCG Studio
    /// (1.14.0) gives bigger packs extra slots by cloning vanilla's last slot into m_Card3dUIList, but doesn't know
    /// about the new list, so slot 7 throws ArgumentOutOfRangeException in InitOpenSequence on every frame and the
    /// opening screen never starts or closes (the rest of the game keeps running).
    ///
    /// A Harmony prefix on SetCardRevealScale tops the base-scale list up to the slot count before it is read.
    /// Clones copy vanilla's last slot, so each extra slot gets that slot's base scale.
    ///
    /// WORKAROUND (added 2026-11-01, tracker 0.15.3, TCG Studio 1.14.0, game 1.03): delete this file and its Install()
    /// call in Plugin.Awake once a TCG Studio release opens 9-card packs on 1.03 by itself.
    /// </summary>
    internal static class RevealScaleGuard
    {
        private static FieldInfo? _baseScales;
        private static bool _logged;

        public static void Install()
        {
            try
            {
                var target = AccessTools.Method(typeof(CardOpeningSequence), "SetCardRevealScale");
                _baseScales = AccessTools.Field(typeof(CardOpeningSequence), "m_CardBaseScaleList");
                if (target == null || _baseScales == null) return; // older game without the reveal scale: nothing to fix.
                new Harmony(Plugin.Guid).Patch(target, prefix: new HarmonyMethod(typeof(RevealScaleGuard), nameof(Prefix)));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not guard the pack opening for packs with more than 7 cards: " + e.Message);
            }
        }

        /// <summary>Harmony prefix. Never skips the game's code, and never lets an error reach it.</summary>
        private static void Prefix(CardOpeningSequence __instance)
        {
            try
            {
                if (!(_baseScales!.GetValue(__instance) is IList scales) || scales.Count == 0) return;
                int slots = __instance.m_Card3dUIList?.Count ?? 0;
                if (scales.Count >= slots) return;
                var last = scales[scales.Count - 1];
                int had = scales.Count;
                while (scales.Count < slots) scales.Add(last);
                if (!_logged)
                {
                    _logged = true;
                    Plugin.Log.LogInfo($"Pack opening: added card scales for {slots - had} extra card slot(s) (TCG Studio pack with {slots} cards).");
                }
            }
            catch (Exception e)
            {
                Plugin.LogOnce("reveal-scale", "Could not fit the pack opening to a pack with more than 7 cards: " + e.Message);
            }
        }
    }
}
