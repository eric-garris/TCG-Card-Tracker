using System;
using System.Collections.Generic;

namespace TCGCardTracker.Core
{
    /// <summary>Which card set a trading or selling customer brings, once custom sets join the draw.</summary>
    public static class TradePick
    {
        /// <summary>
        /// The game's roll, or one of the custom sets with probability <paramref name="chance"/>. A roll that is
        /// already a custom set (the set mod picked it) is kept. <paramref name="roll"/> and <paramref name="pick"/>
        /// are uniform random numbers in [0, 1).
        /// </summary>
        public static int Choose(int rolled, IReadOnlyList<int> custom, float chance, double roll, double pick)
        {
            if (custom.Count == 0 || chance <= 0f || Contains(custom, rolled)) return rolled;
            if (roll >= chance) return rolled;
            int i = Math.Min((int)(pick * custom.Count), custom.Count - 1);
            return custom[Math.Max(i, 0)];
        }

        private static bool Contains(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == value) return true;
            return false;
        }
    }
}
