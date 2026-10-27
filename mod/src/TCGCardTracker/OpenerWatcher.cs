using System;
using HarmonyLib;
using TCGCardTracker.Core;
using UnityEngine;

namespace TCGCardTracker
{
    /// <summary>
    /// "New for your collection" when you collect cards from an auto pack opener. The machine rolls its packs in
    /// the background with no opening screen, so PackWatcher leaves those rolls alone; instead a prefix on the
    /// opener's click snapshots what you own (leaving out what is still inside openers), and once the cards have
    /// come out the usual toast lists what is new. Cards counted by CPlayerData.AddCard after the click keep the
    /// window open until they stop. The hooks only read, and any error is logged while the game carries on.
    /// </summary>
    internal static class OpenerWatcher
    {
        private const float Window = 3f;
        private const float SettleAfterAdd = 1f;

        private static int[]? _before;
        private static float _clickedAt;
        private static float _lastAddAt = -1f;

        public static void Install(Harmony harmony)
        {
            foreach (var method in new[] { "OnMouseButtonUp", "OnRightMouseButtonUp" })
                PackWatcher.Patch(harmony, typeof(InteractableAutoPackOpener), method, typeof(OpenerWatcher), nameof(BeforeClick), prefix: true,
                    "the new-card popup", "collecting from auto pack openers");
            PackWatcher.Patch(harmony, typeof(CPlayerData), nameof(CPlayerData.AddCard), typeof(OpenerWatcher), nameof(AfterAddCard), prefix: false,
                "the new-card popup", "collecting from auto pack openers");
        }

        public static void Reset() => _before = null;

        /// <summary>Harmony prefix. Never skips the game's code, and never lets an error reach it.</summary>
        private static void BeforeClick()
        {
            try
            {
                if (!Plugin.NewCardPopup.Value || !GameReader.IsReady()) return;
                // A second click while the first is still settling keeps the first snapshot.
                if (_before == null) _before = PackWatcher.Snapshot(out _, Loc.Openers);
                _clickedAt = Time.realtimeSinceStartup;
            }
            catch (Exception e)
            {
                _before = null;
                Plugin.LogOnce("opener-click", "New-card popup could not read the collection before collecting from an opener: " + e.Message);
            }
        }

        private static void AfterAddCard(int addAmount)
        {
            if (_before != null && addAmount > 0) _lastAddAt = Time.realtimeSinceStartup;
        }

        /// <summary>Called every frame: once the collected cards have settled, compares and shows the toast.</summary>
        public static void Tick()
        {
            if (_before == null) return;
            float now = Time.realtimeSinceStartup;
            bool added = _lastAddAt >= _clickedAt;
            if (added ? now - _lastAddAt < SettleAfterAdd : now - _clickedAt < Window) return;
            var before = _before;
            _before = null;
            _lastAddAt = -1f;
            PackWatcher.ShowGainsSince(before, "Collected from pack opener", Loc.Openers);
        }
    }
}
