using System;
using System.Collections.Generic;
using HarmonyLib;
using TCGCardTracker.Core;

namespace TCGCardTracker
{
    /// <summary>
    /// Makes employees restock card displays with the most valuable card on a card storage shelf first.
    ///
    /// The game's InteractableCardStorageShelf.GetRandomCard picks a uniformly random entry of the shelf's card list
    /// (one entry per distinct card, each graded card its own entry) and takes one copy of it. Only employees
    /// restocking card displays call it. With [Employees] RestockHighestValue on, a prefix finds the entry with the
    /// highest market price (as F7 shows it: ungraded price, or the graded price for its grade) and hands the game a
    /// one-entry list holding just that entry, so its random pick can only land there. The game then takes the card,
    /// spawns it and updates the copy count on the shared entry object as usual; the finalizer puts the full list
    /// back and drops the entry from it if the game used it up.
    /// </summary>
    internal static class RestockOrder
    {
        public static void Install()
        {
            try
            {
                var target = AccessTools.Method(typeof(InteractableCardStorageShelf), "GetRandomCard");
                if (target == null)
                {
                    Plugin.Log.LogWarning("Card storage shelf has no GetRandomCard on this game version; employees restock as usual.");
                    return;
                }
                new Harmony(Plugin.Guid).Patch(target,
                    prefix: new HarmonyMethod(typeof(RestockOrder), nameof(Prefix)),
                    finalizer: new HarmonyMethod(typeof(RestockOrder), nameof(Finalizer)));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not hook employee card restocking: " + e.Message);
            }
        }

        /// <summary>Harmony prefix. Never skips the game's code; swaps in a one-entry list holding the best card.</summary>
        private static void Prefix(InteractableCardStorageShelf __instance, out State? __state)
        {
            __state = null;
            try
            {
                if (!Plugin.RestockHighestValue.Value) return;
                var list = __instance.GetCompactCardDataAmountList();
                if (list == null || list.Count < 2) return;
                var prices = new float[list.Count];
                for (int i = 0; i < list.Count; i++) prices[i] = Price(list[i]);
                int best = RestockPick.MostValuable(prices);
                if (best < 0) return;
                var only = new List<CompactCardDataAmount> { list[best] };
                __state = new State(list, list[best], only);
                __instance.SetCompactCardDataAmountList(only);
            }
            catch (Exception e)
            {
                __state = null;
                Plugin.LogOnce("restock-pick", "Could not pick the most valuable card to restock (" + e.Message + "); this card is random.");
            }
        }

        /// <summary>Harmony finalizer: always puts the shelf's full list back, even if the game's code failed.</summary>
        private static Exception? Finalizer(InteractableCardStorageShelf __instance, State? __state, Exception? __exception)
        {
            if (__state == null) return __exception;
            try
            {
                // The game removes the entry from the list it was given when the last copy (or the graded card) is taken.
                if (!__state.Only.Contains(__state.Entry)) __state.Full.Remove(__state.Entry);
                __instance.SetCompactCardDataAmountList(__state.Full);
            }
            catch (Exception e)
            {
                Plugin.LogOnce("restock-restore", "Could not restore a card storage shelf after restocking: " + e.Message);
            }
            return __exception;
        }

        private static float Price(CompactCardDataAmount c)
        {
            try
            {
                // A graded entry stores its grade in amount (CPlayerData.GetGradedCardData); an ungraded one its copy count.
                int grade = c.gradedCardIndex > 0 ? c.amount : 0;
                return CPlayerData.GetCardMarketPrice(c.cardSaveIndex, c.expansionType, c.isDestiny, grade);
            }
            catch
            {
                return 0f;
            }
        }

        private sealed class State
        {
            public readonly List<CompactCardDataAmount> Full;
            public readonly CompactCardDataAmount Entry;
            public readonly List<CompactCardDataAmount> Only;

            public State(List<CompactCardDataAmount> full, CompactCardDataAmount entry, List<CompactCardDataAmount> only)
            {
                Full = full;
                Entry = entry;
                Only = only;
            }
        }
    }
}
