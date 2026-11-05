using System.Collections.Generic;

namespace TCGCardTracker.Core
{
    /// <summary>Which card storage shelf entry an employee takes next when restocking card displays.</summary>
    public static class RestockPick
    {
        /// <summary>
        /// Index of the most valuable entry (the first one when several tie), or -1 for an empty list.
        /// A price that isn't a number (unknown card) counts as 0.
        /// </summary>
        public static int MostValuable(IReadOnlyList<float> prices)
        {
            int best = -1;
            float bestPrice = 0f;
            for (int i = 0; i < prices.Count; i++)
            {
                float p = float.IsNaN(prices[i]) ? 0f : prices[i];
                if (best < 0 || p > bestPrice)
                {
                    best = i;
                    bestPrice = p;
                }
            }
            return best;
        }
    }
}
