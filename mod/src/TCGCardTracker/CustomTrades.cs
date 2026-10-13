using System;
using HarmonyLib;
using TCGCardTracker.Core;

namespace TCGCardTracker
{
    /// <summary>
    /// Lets customers bring TCG Studio cards to trade or sell while the vanilla sets are still on.
    ///
    /// The game rolls a trading customer's set from its own sets only. TCG Studio rewrites that roll through
    /// its VanillaFilter.TradeExpansion, but only swaps in a custom set when vanilla cards are hidden
    /// (its [Content] ShowVanillaCards off). A postfix there swaps in one of its sets at
    /// <see cref="Plugin.CustomTradeChance"/>, so the rest of TCG Studio's custom-set trade path runs as usual.
    /// </summary>
    internal static class CustomTrades
    {
        internal const string StudioGuid = "tcgcustomcards";
        private const string StudioHook = "TCGCustomCards.Runtime.VanillaFilter";

        private static readonly Random Rng = new Random();

        public static void Install()
        {
            try
            {
                var type = AccessTools.TypeByName(StudioHook);
                if (type == null) return; // TCG Studio isn't installed: nothing to do.
                var target = AccessTools.Method(type, "TradeExpansion");
                if (target == null)
                {
                    Plugin.Log.LogWarning("Could not find TCG Studio's trade hook (a newer TCG Studio?); custom cards stay out of trades.");
                    return;
                }
                new Harmony(Plugin.Guid).Patch(target, postfix: new HarmonyMethod(typeof(CustomTrades), nameof(AfterTradeExpansion)));
                Plugin.Log.LogInfo($"Customers can bring TCG Studio cards to trade (chance {Plugin.CustomTradeChance.Value:P0}).");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not hook TCG Studio's trades; custom cards stay out of trades: " + e.Message);
            }
        }

        /// <summary>Harmony postfix on TCG Studio's VanillaFilter.TradeExpansion. Any error keeps its answer.</summary>
        private static void AfterTradeExpansion(ref ECardExpansionType __result)
        {
            try
            {
                float chance = Math.Max(0f, Math.Min(1f, Plugin.CustomTradeChance.Value));
                if (chance <= 0f) return;
                var custom = GameReader.CustomExpansions();
                __result = (ECardExpansionType)TradePick.Choose((int)__result, custom, chance, Rng.NextDouble(), Rng.NextDouble());
            }
            catch (Exception e)
            {
                Plugin.LogOnce("custom-trade", "Could not pick a custom set for a trade: " + e.Message);
            }
        }
    }
}
