using System;
using System.Collections.Generic;
using System.Globalization;

namespace TCGCardTracker.Core
{
    /// <summary>What the binder's scan-score filter shows.</summary>
    public enum ScanFilter
    {
        /// <summary>Every card, as without the filter.</summary>
        Off = 0,
        /// <summary>Cards with at least one scanned copy in the binder.</summary>
        Scanned = 1,
        /// <summary>Cards with a scanned copy whose whole score range is below the threshold.</summary>
        Below = 2,
    }

    /// <summary>
    /// A copy's condition score as CardGradeOverhaul knows it, in percent: exact after a card scanner (Lo = Hi),
    /// a range after looking at it by hand.
    /// </summary>
    public readonly struct CopyScore
    {
        public readonly double Lo, Hi;

        public CopyScore(double lo, double hi)
        {
            Lo = Math.Min(lo, hi);
            Hi = Math.Max(lo, hi);
        }

        public bool Exact => Hi - Lo < 0.05;
    }

    /// <summary>How the scan-score filter orders the card versions it keeps (one slot each).</summary>
    public enum ScanSort
    {
        /// <summary>TCG Studio's own order (set order, name, rarity, ...): a card's versions stay together.</summary>
        Keep = 0,
        /// <summary>Most valuable version first.</summary>
        Price = 1,
        /// <summary>Highest copies x price first.</summary>
        TotalValue = 2,
        /// <summary>Most copies first.</summary>
        Amount = 3,
        /// <summary>Lowest scan score first (then most valuable).</summary>
        Score = 4,
    }

    /// <summary>One kept card version: its place in TCG Studio's list, its lowest remaining score, price and copies.</summary>
    public readonly struct ScanSlot
    {
        public readonly int Order;
        public readonly double Low;
        public readonly float Price;
        public readonly int Count;

        public ScanSlot(int order, double low, float price, int count)
        {
            Order = order;
            Low = low;
            Price = float.IsNaN(price) ? 0f : price;
            Count = count;
        }
    }

    /// <summary>The binder's scan-score badge and filter, worked out from one card version's scanned copies.</summary>
    public static class ScanScores
    {
        public const float MinThreshold = 5f, MaxThreshold = 100f, ThresholdStep = 5f;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>
        /// The scanned copies still in the binder, lowest score first. The card version's copies on card storage shelves
        /// (<paramref name="shelved"/>) are taken to be its lowest-scoring ones: moving a card to a shelf doesn't tell
        /// CardGradeOverhaul which copy went, and when it sells, CardGradeOverhaul lets go of the lowest-scoring record.
        /// </summary>
        public static List<CopyScore> Remaining(IEnumerable<CopyScore> scores, int shelved)
        {
            var list = new List<CopyScore>(scores);
            list.Sort((a, b) => a.Lo != b.Lo ? a.Lo.CompareTo(b.Lo) : a.Hi.CompareTo(b.Hi));
            if (shelved > 0) list.RemoveRange(0, Math.Min(shelved, list.Count));
            return list;
        }

        /// <summary>Whether a card version with these remaining scanned copies shows under the filter.</summary>
        public static bool Passes(IReadOnlyList<CopyScore> remaining, ScanFilter mode, double threshold)
        {
            switch (mode)
            {
                case ScanFilter.Scanned: return remaining.Count > 0;
                case ScanFilter.Below:
                    foreach (var s in remaining)
                        if (s.Hi < threshold) return true; // the whole range is below: never a good card by mistake
                    return false;
                default: return true;
            }
        }

        /// <summary>Badge text: the lowest score ("62%", "69.5%" or a range "58-66%"), then "·3" when 3 copies are scanned. Null for none.</summary>
        public static string? Badge(IReadOnlyList<CopyScore> remaining)
        {
            if (remaining.Count == 0) return null;
            var low = remaining[0];
            string text = Percent(low);
            if (remaining.Count > 1) text += " ·" + remaining.Count.ToString(Inv);
            return text;
        }

        /// <summary>A score as CardGradeOverhaul writes it: exact with up to one decimal, a range in whole numbers.</summary>
        public static string Percent(CopyScore s)
        {
            if (s.Exact) return s.Lo.ToString("0.#", Inv) + "%";
            int lo = (int)Math.Floor(s.Lo);
            int hi = Math.Min(100, (int)Math.Ceiling(s.Hi));
            return lo >= hi ? lo.ToString(Inv) + "%" : lo.ToString(Inv) + "-" + hi.ToString(Inv) + "%";
        }

        /// <summary>CardGradeOverhaul's colour band for a score: 0 = 95+, 1 = 90+, 2 = 80+, 3 = 70+, 4 = lower.</summary>
        public static int Band(double percent) =>
            percent >= 95.0 ? 0 : percent >= 90.0 ? 1 : percent >= 80.0 ? 2 : percent >= 70.0 ? 3 : 4;

        /// <summary>
        /// The binder sort that applies to single versions. TCG Studio sorts whole cards by the version a slot shows, so
        /// once a card is split into its versions, sorts by price, total value or copies are redone per version.
        /// </summary>
        public static ScanSort SortFor(string? binderSort, bool byScore)
        {
            if (byScore) return ScanSort.Score;
            switch (binderSort)
            {
                case "Price": return ScanSort.Price;
                case "TotalValue": return ScanSort.TotalValue;
                case "Amount": return ScanSort.Amount;
                default: return ScanSort.Keep;
            }
        }

        /// <summary>Orders the kept versions (stable: ties keep TCG Studio's order).</summary>
        public static void Sort(List<ScanSlot> slots, ScanSort sort)
        {
            if (sort == ScanSort.Keep) return;
            slots.Sort((a, b) =>
            {
                int c = 0;
                switch (sort)
                {
                    case ScanSort.Price: c = b.Price.CompareTo(a.Price); break;
                    case ScanSort.TotalValue: c = (b.Price * b.Count).CompareTo(a.Price * a.Count); break;
                    case ScanSort.Amount: c = b.Count.CompareTo(a.Count); break;
                    case ScanSort.Score:
                        c = a.Low.CompareTo(b.Low);
                        if (c == 0) c = b.Price.CompareTo(a.Price);
                        break;
                }
                return c != 0 ? c : a.Order.CompareTo(b.Order);
            });
        }

        /// <summary>The threshold kept on the 5% steps the - / + buttons use, within 5 - 100.</summary>
        public static float Clamp(float threshold)
        {
            if (float.IsNaN(threshold)) return 70f;
            return Math.Max(MinThreshold, Math.Min(MaxThreshold, threshold));
        }

        /// <summary>Label of the filter's threshold chip, e.g. "Below 70%".</summary>
        public static string BelowLabel(float threshold) => "Below " + Clamp(threshold).ToString("0.#", Inv) + "%";
    }
}
