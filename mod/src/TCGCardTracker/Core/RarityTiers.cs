using System;
using System.Collections.Generic;
using System.Linq;

namespace TCGCardTracker.Core
{
    /// <summary>
    /// Vanilla tier (Common…Legendary) of a custom set's cards. TCG Studio 2.x gives every rarity of every set a value of its own
    /// (1000 and up, numbered in the set's order, most common first), so the value alone doesn't say Common/Rare/Epic/Legendary.
    /// Values 0..3 are the vanilla tiers already; the others are ranked within the set and spread over the tiers the way
    /// TCG Studio does (<see cref="PromoRarityPlan.Tier"/>).
    /// </summary>
    public static class RarityTiers
    {
        /// <summary>Tier of each raw rarity value found in one set.</summary>
        public static Dictionary<int, Tier> ForSet(IEnumerable<int> rawRarities)
        {
            var result = new Dictionary<int, Tier>();
            var custom = new List<int>();
            foreach (int r in rawRarities.Distinct())
            {
                if (r >= 0 && r <= 3) result[r] = (Tier)r;
                else if (r > 3) custom.Add(r);
            }
            custom.Sort();
            for (int i = 0; i < custom.Count; i++)
                result[custom[i]] = (Tier)Math.Max(0, Math.Min(3, PromoRarityPlan.Tier(i, custom.Count)));
            return result;
        }
    }
}
