using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TCGCardTracker.Core;

namespace TCGCardTracker
{
    /// <summary>
    /// Speeds up selling in the TCG Marketplace mod (packxis). Its timings are fixed in code, with no settings:
    ///
    /// - MarketplaceService.EvaluateActiveListings checks each listing for a buyer every 15 - 25 real seconds (a new
    ///   listing waits MarketplaceDemand.GetInitialBuyerCheckDelaySeconds first), and a hit queues one copy.
    /// - MarketplaceCustomerScheduler then sends one customer from that queue every 2 - 4 real seconds
    ///   (MarketplaceService.NextCustomerIntervalSeconds, clamped by NormalizeInterval) who buys 1 - 2 cards
    ///   (NextCustomerPurchaseCount, clamped to 2 in TryBeginCustomer).
    ///
    /// Patches here swap in <see cref="Plugin.MarketCustomerSeconds"/>, <see cref="Plugin.MarketCardsPerCustomer"/>
    /// and <see cref="Plugin.MarketRecheckSeconds"/>. Its payment and saving steps are left alone. Everything is found
    /// by name, so the mod isn't needed at build time.
    /// </summary>
    internal static class Marketplace
    {
        internal const string Guid = "com.packxis.tcgmarketplace";
        private const string Service = "TCGMarketplace.MarketplaceService";
        private const string Scheduler = "TCGMarketplace.MarketplaceCustomerScheduler";
        private const string Demand = "TCGMarketplace.MarketplaceDemand";

        private static readonly Random Rng = new Random();
        private static AccessTools.FieldRef<object, int>? _requested;

        public static void Install()
        {
            try
            {
                var service = AccessTools.TypeByName(Service);
                if (service == null) return; // TCG Marketplace isn't installed: nothing to do.
                if (!Plugin.MarketFasterSales.Value) return;
                var scheduler = AccessTools.TypeByName(Scheduler);
                var harmony = new Harmony(Plugin.Guid);

                Prefix(harmony, service, "NextCustomerIntervalSeconds", nameof(BeforeNextInterval));
                Prefix(harmony, scheduler, "NormalizeInterval", nameof(BeforeNormalizeInterval));
                _requested = AccessTools.FieldRefAccess<int>(scheduler, "_requestedPurchaseCount");
                var begin = Method(scheduler, "TryBeginCustomer");
                harmony.Patch(begin, postfix: new HarmonyMethod(typeof(Marketplace), nameof(AfterTryBeginCustomer)));
                Plugin.Log.LogInfo($"TCG Marketplace: a customer about every {Plugin.MarketCustomerSeconds.Value:0.##} s, " +
                                   $"buying up to {SaleSpeed.PurchaseCount(Plugin.MarketCardsPerCustomer.Value, 0.9999)} cards.");

                harmony.Patch(Method(service, "EvaluateActiveListings"),
                    transpiler: new HarmonyMethod(typeof(Marketplace), nameof(RecheckTranspiler)));
                harmony.Patch(Method(AccessTools.TypeByName(Demand), "GetInitialBuyerCheckDelaySeconds"),
                    postfix: new HarmonyMethod(typeof(Marketplace), nameof(AfterFirstCheck)));
                Plugin.Log.LogInfo($"TCG Marketplace: listings checked for buyers about every {Plugin.MarketRecheckSeconds.Value:0.##} s.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not speed up TCG Marketplace (a newer version?); it sells at its own pace: " + e.Message);
            }
        }

        private static MethodInfo Method(Type? type, string name) =>
            (type == null ? null : AccessTools.Method(type, name)) ?? throw new MissingMethodException(type?.FullName ?? "?", name);

        private static void Prefix(Harmony harmony, Type? type, string name, string patch) =>
            harmony.Patch(Method(type, name), prefix: new HarmonyMethod(typeof(Marketplace), patch));

        /// <summary>Prefix on MarketplaceService.NextCustomerIntervalSeconds: our gap instead of 2 - 4 seconds.</summary>
        private static bool BeforeNextInterval(ref float __result)
        {
            __result = SaleSpeed.CustomerInterval(Plugin.MarketCustomerSeconds.Value, Rng.NextDouble());
            return false;
        }

        /// <summary>Prefix on MarketplaceCustomerScheduler.NormalizeInterval, which would clamp any gap to 2 - 4 seconds.</summary>
        private static bool BeforeNormalizeInterval(float value, ref float __result)
        {
            __result = float.IsNaN(value) || float.IsInfinity(value)
                ? SaleSpeed.CustomerInterval(Plugin.MarketCustomerSeconds.Value, 0.5)
                : Math.Max(0.1f, value);
            return false;
        }

        /// <summary>
        /// Postfix on MarketplaceCustomerScheduler.TryBeginCustomer, which caps a customer at 2 cards: when a customer
        /// starts, set how many cards they buy. The visit takes cards from the queue until it has that many or it runs out.
        /// </summary>
        private static void AfterTryBeginCustomer(object __instance, bool __result)
        {
            if (!__result || _requested == null) return;
            try
            {
                _requested(__instance) = SaleSpeed.PurchaseCount(Plugin.MarketCardsPerCustomer.Value, Rng.NextDouble());
            }
            catch (Exception e)
            {
                Plugin.LogOnce("market:count", "Could not set how many cards a TCG Marketplace customer buys: " + e.Message);
            }
        }

        /// <summary>
        /// EvaluateActiveListings sets a listing's next check with DateTime.UtcNow.AddSeconds(15 + random 0 - 10). Its
        /// only AddSeconds call becomes <see cref="Recheck"/>, which scales that delay.
        /// </summary>
        private static IEnumerable<CodeInstruction> RecheckTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var addSeconds = AccessTools.Method(typeof(DateTime), nameof(DateTime.AddSeconds));
            var recheck = AccessTools.Method(typeof(Marketplace), nameof(Recheck));
            int swapped = 0;
            foreach (var code in instructions)
            {
                if (code.Calls(addSeconds))
                {
                    code.opcode = System.Reflection.Emit.OpCodes.Call;
                    code.operand = recheck;
                    swapped++;
                }
                yield return code;
            }
            if (swapped != 1)
                Plugin.LogOnce("market:recheck", $"TCG Marketplace's listing check looks different than expected ({swapped} timers found).");
        }

        /// <summary>Stands in for <c>time.AddSeconds(seconds)</c> (same stack: the DateTime's address, then the seconds).</summary>
        private static DateTime Recheck(ref DateTime time, double seconds) =>
            time.AddSeconds(SaleSpeed.Recheck(seconds, Plugin.MarketRecheckSeconds.Value));

        /// <summary>Postfix on MarketplaceDemand.GetInitialBuyerCheckDelaySeconds: a new listing's first check, scaled the same.</summary>
        private static void AfterFirstCheck(ref int __result) =>
            __result = SaleSpeed.FirstCheck(__result, Plugin.MarketRecheckSeconds.Value);
    }
}
