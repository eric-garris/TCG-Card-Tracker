using System;

namespace TCGCardTracker.Core
{
    /// <summary>
    /// The TCG Marketplace mod's sale timings, rescaled by the tracker's settings. Its own numbers: a customer every
    /// 2 - 4 seconds buying 1 - 2 cards, each listing re-checked for a buyer every 15 - 25 seconds (20 on average).
    /// </summary>
    public static class SaleSpeed
    {
        /// <summary>The marketplace's own average seconds between two checks of one listing.</summary>
        public const float TheirRecheckSeconds = 20f;

        /// <summary>
        /// Seconds until the next customer: <paramref name="seconds"/> give or take a quarter, never under 0.1.
        /// <paramref name="roll"/> is a uniform random number in [0, 1).
        /// </summary>
        public static float CustomerInterval(float seconds, double roll)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) seconds = 1f;
            return Math.Max(0.1f, seconds * (float)(0.75 + 0.5 * roll));
        }

        /// <summary>
        /// How many cards one customer buys: from half of <paramref name="max"/> (rounded up) to all of it, at least 1.
        /// </summary>
        public static int PurchaseCount(float max, double roll)
        {
            int n = float.IsNaN(max) ? 1 : (int)Math.Round(Math.Max(1f, Math.Min(500f, max)));
            int low = (n + 1) / 2;
            return Math.Min(n, low + (int)(roll * (n - low + 1)));
        }

        /// <summary>The marketplace's delay before a listing's next check, scaled so the average becomes <paramref name="recheck"/>.</summary>
        public static double Recheck(double theirSeconds, float recheck)
        {
            if (float.IsNaN(recheck) || recheck <= 0f) return theirSeconds;
            return Math.Max(0.5, theirSeconds * recheck / TheirRecheckSeconds);
        }

        /// <summary>The wait before a new listing's first check, scaled the same way (whole seconds, at least 1).</summary>
        public static int FirstCheck(int theirSeconds, float recheck) =>
            Math.Max(1, (int)Math.Round(Recheck(theirSeconds, recheck)));
    }
}
