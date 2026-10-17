using System;

namespace TCGCardTracker.Core
{
    /// <summary>
    /// A Point And Slots reward rarity with a card version on the end, such as "Common - Silver Foil" or
    /// "Legendary - Full Art": the rarity Point And Slots looks for, and the exact version (border 0 Base .. 5 Full Art,
    /// foil or not) the tracker gives instead of rolling one. "Common - Foil" is a Base foil.
    /// </summary>
    public static class SlotVersion
    {
        public static readonly string[] BorderNames = { "Base", "First Edition", "Silver", "Gold", "EX", "Full Art" };
        private const string Separator = " - ";

        public static bool TryParse(string? name, out string rarity, out int border, out bool foil)
        {
            rarity = name ?? "";
            border = 0;
            foil = false;
            int at = rarity.LastIndexOf(Separator, StringComparison.Ordinal);
            if (at <= 0) return false;
            string version = rarity.Substring(at + Separator.Length).Trim();
            bool isFoil = version.Equals("Foil", StringComparison.OrdinalIgnoreCase)
                || version.EndsWith(" Foil", StringComparison.OrdinalIgnoreCase);
            if (isFoil) version = version.Substring(0, version.Length - 4).Trim();
            int found = version.Length == 0 && isFoil ? 0 : Array.FindIndex(BorderNames, b => Squash(b) == Squash(version));
            if (found < 0) return false;
            rarity = rarity.Substring(0, at).Trim();
            border = found;
            foil = isFoil;
            return true;
        }

        /// <summary>The rarity alone: "Common - Silver" gives "Common"; names without a version come back unchanged.</summary>
        public static string RarityOf(string? name) => TryParse(name, out var rarity, out _, out _) ? rarity : name ?? "";

        /// <summary>"COMMON SILVER FOIL", for Point And Slots' pixel font, which has letters and digits only.</summary>
        public static string WinLabel(string rarity, int border, bool foil) =>
            (rarity + (border > 0 ? " " + BorderNames[border] : "") + (foil ? " Foil" : "")).ToUpperInvariant();

        private static string Squash(string s) => s.Replace(" ", "").Replace("1st", "first").ToLowerInvariant();
    }
}
