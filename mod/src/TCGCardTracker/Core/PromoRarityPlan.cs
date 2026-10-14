using System;
using System.Collections.Generic;

namespace TCGCardTracker.Core
{
    /// <summary>
    /// Adds custom-set rarity names to Point And Slots' rarity settings, which are comma-separated names it matches
    /// exactly (ordinal). A name it already lists as a promo rarity is left alone, as is any name added before
    /// (so one the player deletes stays deleted). A new name joins the promo list (slot rewards), the shop list,
    /// and gets a shop price by tier.
    /// </summary>
    public static class PromoRarityPlan
    {
        /// <summary>Shop price per tier (0 common .. 3 legendary), matching Point And Slots' own Rare/Epic/Legendary prices.</summary>
        public static readonly int[] TierCost = { 1000, 2000, 6000, 35000 };

        public sealed class Result
        {
            public string Promo = "", Shop = "", Costs = "";
            public readonly List<string> Added = new List<string>();
            public bool Changed => Added.Count > 0;
        }

        public static Result Plan(string promo, string shop, string costs, ICollection<string> alreadyAdded,
            IEnumerable<KeyValuePair<string, int>> custom)
        {
            var promoList = Split(promo);
            var shopList = Split(shop);
            var costList = Split(costs);
            var costKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in costList)
            {
                int colon = c.IndexOf(':');
                if (colon > 0) costKeys.Add(c.Substring(0, colon).Trim());
            }

            var result = new Result();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in custom)
            {
                string name = (kv.Key ?? "").Trim();
                // Commas and colons would split the setting into other names.
                if (name.Length == 0 || name.IndexOf(',') >= 0 || name.IndexOf(':') >= 0) continue;
                if (!seen.Add(name) || alreadyAdded.Contains(name) || promoList.Contains(name)) continue;

                promoList.Add(name);
                if (!shopList.Contains(name)) shopList.Add(name);
                if (costKeys.Add(name)) costList.Add(name + ":" + TierCost[Math.Max(0, Math.Min(TierCost.Length - 1, kv.Value))]);
                result.Added.Add(name);
            }

            result.Promo = result.Changed ? string.Join(", ", promoList) : promo;
            result.Shop = result.Changed ? string.Join(", ", shopList) : shop;
            result.Costs = result.Changed ? string.Join(", ", costList) : costs;
            return result;
        }

        /// <summary>
        /// Tier (0..3) of each of a set's rarities, given in the set's order from most common. Same spread TCG Studio uses:
        /// up to four rarities map straight onto the tiers, more are spread evenly across them.
        /// </summary>
        public static int Tier(int index, int count)
        {
            if (count <= 1) return 0;
            if (count <= 4) return Math.Min(index, 3);
            return (int)Math.Round(index * 3.0 / (count - 1));
        }

        private static List<string> Split(string value)
        {
            var list = new List<string>();
            foreach (var part in (value ?? "").Split(','))
            {
                string s = part.Trim();
                if (s.Length > 0) list.Add(s);
            }
            return list;
        }
    }
}
