using System;

namespace TCGCardTracker.Core
{
    /// <summary>
    /// Grading companies of the CardGradeOverhaul mod (com.qolmods.cardgradeoverhaul). It writes its own cert number
    /// into a slab's gradedCardIndex: 1,000,000,000 + company x 100,000,000 + serial. Slabs graded without it (the
    /// game's own, or graded before it was installed) carry a small index and count as its LegacyGrader setting.
    /// </summary>
    public static class Graders
    {
        /// <summary>In the mod's order: index = company number in the cert.</summary>
        public static readonly string[] Names = { "TCG", "PSA", "BGS", "CGC", "TAG", "SGC" };
        public static int Count => Names.Length;

        private const int OwnBase = 1000000000;
        private const int PerCompany = 100000000;

        /// <summary>The company of a slab with this gradedCardIndex; `legacy` for slabs without the mod's cert.</summary>
        public static int CompanyOf(int gradedIndex, int legacy)
        {
            if (gradedIndex < OwnBase) return legacy;
            int c = (gradedIndex - OwnBase) / PerCompany;
            return c >= 0 && c < Count ? c : legacy;
        }

        /// <summary>A company name as the mod's settings write it (PSA, BGS, ...); unknown or empty is TCG (the game's own).</summary>
        public static int Parse(string? name)
        {
            if (!string.IsNullOrEmpty(name))
                for (int i = 0; i < Count; i++)
                    if (string.Equals(Names[i], name!.Trim(), StringComparison.OrdinalIgnoreCase)) return i;
            return 0;
        }
    }
}
