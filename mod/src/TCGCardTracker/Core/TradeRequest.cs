using System;
using System.Collections.Generic;

namespace TCGCardTracker.Core
{
    /// <summary>Which of your custom-set cards a trading customer asks for, once custom sets join what they want.</summary>
    public static class TradeRequest
    {
        /// <summary>The game's (and Enhanced Prefab Loader's) fair-trade window: 75% up to 150% of the offered card's price.</summary>
        public const float MinRatio = 0.75f, MaxRatio = 1.5f;

        public static bool FairPrice(float price, float offered) =>
            price > 0f && price >= offered * MinRatio && price < offered * MaxRatio;

        /// <summary>
        /// The index into <paramref name="prices"/> of the card to ask for instead, or -1 to keep the game's pick.
        /// Only cards at a fair price for <paramref name="offered"/> count. <paramref name="roll"/> and
        /// <paramref name="pick"/> are uniform random numbers in [0, 1).
        /// </summary>
        public static int Choose(IReadOnlyList<float> prices, float offered, float chance, double roll, double pick)
        {
            if (chance <= 0f || offered <= 0f || roll >= chance) return -1;
            var fair = new List<int>();
            for (int i = 0; i < prices.Count; i++)
                if (FairPrice(prices[i], offered)) fair.Add(i);
            if (fair.Count == 0) return -1;
            int k = Math.Min((int)(pick * fair.Count), fair.Count - 1);
            return fair[Math.Max(k, 0)];
        }
    }
}
