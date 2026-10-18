using System;
using System.Collections.Generic;

namespace TCGCardTracker.Core
{
    /// <summary>
    /// Picks a card version (border 0 Base .. 5 Full Art, foil or not) with the game's own pack odds: each special border
    /// is checked rarest first (Full Art 0.25%, EX 1%, Gold 4%, Silver 8%, First Edition 20%, else Base), then foil 5%.
    /// A set that only has some versions (TCG Studio's "variants") passes a mask of 12, indexed border + 6 if foil;
    /// a version it lacks becomes the other foil state, or else any version it has.
    /// </summary>
    public static class VariantRoll
    {
        public const int Borders = 6;
        public const float FoilChance = 5f;

        /// <summary>(border, percent) in the order they are checked.</summary>
        public static readonly KeyValuePair<int, float>[] BorderOdds =
        {
            new KeyValuePair<int, float>(5, 0.25f), // Full Art
            new KeyValuePair<int, float>(4, 1f),    // EX
            new KeyValuePair<int, float>(3, 4f),    // Gold
            new KeyValuePair<int, float>(2, 8f),    // Silver
            new KeyValuePair<int, float>(1, 20f),   // First Edition
        };

        /// <param name="percent">Returns a number in [0, 100) each call.</param>
        /// <param name="mask">The versions the set has (12 entries), or null for all of them.</param>
        public static (int border, bool foil) Roll(Func<float> percent, bool[]? mask = null)
        {
            int border = 0;
            foreach (var odds in BorderOdds)
                if (Has(mask, odds.Key) && percent() < odds.Value) { border = odds.Key; break; }
            return Fit(border, percent() < FoilChance, percent, mask);
        }

        /// <summary>The version itself if the set has it, else the same border with the other foil state, else any version it has.</summary>
        public static (int border, bool foil) Fit(int border, bool foil, Func<float> percent, bool[]? mask = null)
        {
            if (Allows(mask, border, foil)) return (border, foil);
            if (Allows(mask, border, !foil)) return (border, !foil);
            var allowed = new List<int>();
            for (int i = 0; i < Borders * 2; i++)
                if (mask![i]) allowed.Add(i);
            if (allowed.Count == 0) return (0, false);
            int pick = allowed[Math.Min(allowed.Count - 1, (int)(percent() / 100f * allowed.Count))];
            return (pick % Borders, pick >= Borders);
        }

        private static bool Has(bool[]? mask, int border) => Allows(mask, border, false) || Allows(mask, border, true);

        private static bool Allows(bool[]? mask, int border, bool foil) =>
            mask == null || mask.Length < Borders * 2 || mask[border + (foil ? Borders : 0)];
    }
}
