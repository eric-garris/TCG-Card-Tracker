using System;
using System.Collections.Generic;
using System.Reflection;
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
    ///
    /// The card a customer asks for in return is picked by the game from your vanilla cards only. A postfix on
    /// CustomerTradeCardScreen.SetCustomer swaps it, at <see cref="Plugin.CustomRequestChance"/>, for one of your
    /// custom-set cards at a fair price (the game's own 75% - 150% of the offered card), and redraws that side.
    /// </summary>
    internal static class CustomTrades
    {
        internal const string StudioGuid = "tcgcustomcards";
        internal const string StudioHook = "TCGCustomCards.Runtime.VanillaFilter";

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
                var harmony = new Harmony(Plugin.Guid);
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(CustomTrades), nameof(AfterTradeExpansion)));
                Plugin.Log.LogInfo($"Customers can bring TCG Studio cards to trade (chance {Plugin.CustomTradeChance.Value:P0}).");

                // Before the trade banners' postfix, so the banner describes the card actually asked for.
                var setCustomer = AccessTools.Method(typeof(CustomerTradeCardScreen), "SetCustomer");
                if (setCustomer == null)
                {
                    Plugin.Log.LogWarning("Could not find the game's trade screen; customers keep asking for vanilla cards only.");
                    return;
                }
                harmony.Patch(setCustomer, postfix: new HarmonyMethod(typeof(CustomTrades), nameof(AfterSetCustomer)) { priority = Priority.High });
                Plugin.Log.LogInfo($"Trading customers can ask for your TCG Studio cards (chance {Plugin.CustomRequestChance.Value:P0}).");
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
    
        /// <summary>
        /// Harmony postfix on the trade screen opening for a customer. Only a new trade offer is changed: a customer
        /// coming back with an offer they already made (the trade data argument) keeps it. Any error keeps the game's pick.
        /// </summary>
        private static void AfterSetCustomer(CustomerTradeCardScreen __instance, object[] __args)
        {
            try
            {
                float chance = Math.Max(0f, Math.Min(1f, Plugin.CustomRequestChance.Value));
                if (chance <= 0f || !GameReader.IsReady()) return;
                foreach (var a in __args)
                    if (a is CustomerTradeData) return;
                var screen = Traverse.Create(__instance);
                if (!(screen.Field("m_IsTrading").GetValue() as bool? ?? false)) return;
                var offered = screen.Field("m_CardData_L").GetValue() as CardData;
                var wanted = screen.Field("m_CardData_R").GetValue() as CardData;
                if (offered == null || wanted == null) return;
                var custom = GameReader.CustomExpansions();
                if (custom.Count == 0 || custom.Contains((int)wanted.expansionType)) return;

                float offeredPrice = CPlayerData.GetCardMarketPrice(offered);
                var (cards, prices) = OwnedCustomCards(custom, offered);
                int i = TradeRequest.Choose(prices, offeredPrice, chance, Rng.NextDouble(), Rng.NextDouble());
                if (i < 0) return;

                // In place, so anything already holding the game's pick (such as the customer's saved offer) follows.
                Copy(cards[i], wanted);
                Redraw(screen, wanted, prices[i]);
                Plugin.Log.LogInfo($"Trading customer asks for a TCG Studio card instead: {GameReader.ExpansionName((int)wanted.expansionType)} #{(int)wanted.monsterType}" +
                    (wanted.cardGrade > 0 ? $" graded {wanted.cardGrade}" : "") + $" ({prices[i]:0.00} for an offer of {offeredPrice:0.00}).");
            }
            catch (Exception e)
            {
                Plugin.LogOnce("custom-request", "Could not pick a TCG Studio card for a customer to ask for: " + e.Message);
            }
        }

        /// <summary>Your custom-set cards a customer could ask for (ungraded copies in the binder, and graded cards), with prices.</summary>
        private static (List<CardData> cards, List<float> prices) OwnedCustomCards(List<int> custom, CardData offered)
        {
            var cards = new List<CardData>();
            var prices = new List<float>();
            foreach (int e in custom)
            {
                var exp = (ECardExpansionType)e;
                var album = CPlayerData.GetCardCollectedList(exp, false);
                if (album == null) continue;
                for (int i = 0; i < album.Count; i++)
                {
                    if (album[i] <= 0) continue;
                    var card = CPlayerData.GetCardData(i, exp, false);
                    if (card == null || (int)card.monsterType <= 0 || Same(card, offered)) continue;
                    cards.Add(card);
                    prices.Add(CPlayerData.GetCardMarketPrice(i, exp, false, 0));
                }
            }
            var graded = GetGraded.Value;
            if (graded != null && CPlayerData.m_GradedCardInventoryList != null)
            {
                foreach (var g in CPlayerData.m_GradedCardInventoryList)
                {
                    if (!custom.Contains((int)g.expansionType)) continue;
                    if (!(graded.Invoke(null, new object[] { g }) is CardData card) || (int)card.monsterType <= 0 || Same(card, offered)) continue;
                    cards.Add(card);
                    prices.Add(CPlayerData.GetCardMarketPrice(g.cardSaveIndex, g.expansionType, g.isDestiny, g.amount));
                }
            }
            return (cards, prices);
        }

        private static readonly Lazy<MethodInfo?> GetGraded = new Lazy<MethodInfo?>(() =>
            AccessTools.Method(typeof(CPlayerData), "GetGradedCardData", new[] { typeof(CompactCardDataAmount) }));

        private static readonly Lazy<MethodInfo?> HasGradedInAlbum = new Lazy<MethodInfo?>(() =>
            AccessTools.Method(typeof(CPlayerData), "HasGradedCardInAlbum", new[] { typeof(CardData) }));

        private static bool Same(CardData a, CardData b) =>
            a.expansionType == b.expansionType && a.monsterType == b.monsterType && a.borderType == b.borderType
            && a.isFoil == b.isFoil && a.isDestiny == b.isDestiny && a.cardGrade == b.cardGrade;

        private static void Copy(CardData from, CardData to)
        {
            foreach (var f in AccessTools.GetDeclaredFields(typeof(CardData)))
                if (!f.IsStatic && !f.IsInitOnly) f.SetValue(to, f.GetValue(from));
        }

        /// <summary>Redraws the right-hand card, its binder preview, binder count and market price, as the game sets them.</summary>
        private static void Redraw(Traverse screen, CardData card, float price)
        {
            foreach (var ui in new[] { "m_CardUI_R", "m_CardUI_Album" })
            {
                var target = screen.Field(ui).GetValue();
                if (target != null) Traverse.Create(target).Method("SetCardUI", new[] { typeof(CardData) }).GetValue(card);
            }
            string count;
            if (card.cardGrade > 0)
                count = HasGradedInAlbum.Value?.Invoke(null, new object[] { card }) as bool? ?? false ? "X1" : "X0";
            else
                count = "X" + CPlayerData.GetCardAmount(card);
            SetText(screen.Field("m_AlbumCardCount").GetValue(), _ => count);
            // "Market Price : $1.23" in the player's language: keep the label, swap the price.
            SetText(screen.Field("m_MarketPrice_R").GetValue(), old =>
            {
                int colon = old.LastIndexOf(':');
                return (colon >= 0 ? old.Substring(0, colon + 1) + " " : "") + GameReader.PriceString(price);
            });
        }

        private static void SetText(object? label, Func<string, string> text)
        {
            if (label == null) return;
            var t = Traverse.Create(label).Property("text");
            t.SetValue(text(t.GetValue() as string ?? ""));
        }
    }
}
