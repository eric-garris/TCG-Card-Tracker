using System;
using System.Collections.Generic;

namespace TCGCardTracker.Core
{
    /// <summary>Copies you could part with: everything beyond the one copy that counts toward a column.</summary>
    public struct Spare
    {
        public int Copies;
        /// <summary>The column of the copy that is kept: the best grade, then a modded grade, then ungraded.</summary>
        public int KeptCol;
    }

    /// <summary>A card that became new to the collection, or reached a better grade than any copy owned before.</summary>
    public struct Gain
    {
        public Card Card;
        /// <summary>True when no copy was owned before; false when the best copy got better.</summary>
        public bool IsNew;
        /// <summary>The best column now owned (0 = ungraded, 1-10 = grade, Collection.ColOther = other grade).</summary>
        public int BestCol;
        /// <summary>Copies gained in BestCol.</summary>
        public int Copies;
    }

    public static class CardQueries
    {
        /// <summary>The grid columns a column covers.</summary>
        public static IEnumerable<int> ColumnsOf(Column col)
        {
            if (col.Value >= 0)
            {
                yield return col.Value;
                yield break;
            }
            for (int c = col.Value == Column.AnyForm ? 0 : 1; c < Collection.Cols; c++) yield return c;
        }

        /// <summary>How good a column's copy is: ungraded, then a modded grade, then grades 1-10.</summary>
        private static int Rank(int col) => col == 0 ? 0 : col == Collection.ColOther ? 1 : col + 1;

        /// <summary>
        /// Compares two snapshots of combined totals: cards with no copy before and some now, and cards whose
        /// best copy is now a better grade than before.
        /// </summary>
        public static List<Gain> Gains(Catalog catalog, int[] before, int[] after)
        {
            var gains = new List<Gain>();
            foreach (var card in catalog.Cards)
            {
                int was = SpareOf(before, card, Column.Any).KeptCol;
                int now = SpareOf(after, card, Column.Any).KeptCol;
                if (now < 0 || (was >= 0 && Rank(now) <= Rank(was))) continue;
                int b = card.Index * Collection.Cols + now;
                gains.Add(new Gain { Card = card, IsNew = was < 0, BestCol = now, Copies = Math.Max(1, after[b] - before[b]) });
            }
            return gains;
        }

        /// <summary>Spare copies of a card within a column, keeping its single best copy.</summary>
        public static Spare SpareOf(int[] totals, Card card, Column col)
        {
            int b = card.Index * Collection.Cols;
            int copies = 0, kept = -1, keptRank = -1;
            foreach (int c in ColumnsOf(col))
            {
                int n = totals[b + c];
                if (n == 0) continue;
                copies += n;
                int rank = Rank(c);
                if (rank > keptRank)
                {
                    keptRank = rank;
                    kept = c;
                }
            }
            return new Spare { Copies = Math.Max(0, copies - 1), KeptCol = kept };
        }

        /// <summary>Market value of a card's spare copies; price(col) gives one copy's price, null when unknown.</summary>
        public static float SpareValue(int[] totals, Card card, Column col, Func<int, float?> price)
        {
            var spare = SpareOf(totals, card, col);
            if (spare.Copies == 0) return 0f;
            int b = card.Index * Collection.Cols;
            float value = 0f;
            foreach (int c in ColumnsOf(col))
            {
                int n = totals[b + c] - (c == spare.KeptCol ? 1 : 0);
                if (n > 0) value += n * (price(c) ?? 0f);
            }
            return value;
        }

        /// <summary>Search: every word must match the card's name, edition or variant, or equal its number.</summary>
        public static bool Matches(Card card, string? query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            foreach (var word in query!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var w = word.TrimStart('#');
                if (w.Length > 0 && int.TryParse(w, out int num))
                {
                    if (num != card.Num) return false;
                    continue;
                }
                if (card.Label.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0) return false;
            }
            return true;
        }
    }
}
