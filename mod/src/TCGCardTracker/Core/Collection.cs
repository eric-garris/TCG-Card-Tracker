using System;
using System.Collections.Generic;

namespace TCGCardTracker.Core
{
    /// <summary>Where a copy sits. Mirrors the web app's locations.</summary>
    public enum Loc
    {
        Binder,
        Displays,
        Storage,
        Decks,
        Grading,
        Boxes,
        Hand,
        Donation,
        Openers,
    }

    /// <summary>A physical card (CardData): displays, hand, boxes, grading.</summary>
    public struct PhysicalRec
    {
        public int Expansion;
        public int Monster;
        public int Border;
        public bool Foil;
        public bool Destiny;
        public int Grade;
    }

    /// <summary>A compact row (CompactCardDataAmount): graded album, storage, decks, donation, openers.</summary>
    public struct CompactRec
    {
        public int Expansion;
        public int SaveIndex;
        public int Amount;
        public int GradedIndex;
        public bool Destiny;
    }

    /// <summary>
    /// Copy counts per card, per location, per column.
    /// Column 0 = ungraded, 1..10 = grade, 11 = a grade outside 1-10 (written by grading mods).
    /// </summary>
    public sealed class Collection
    {
        public const int Cols = 12;
        public const int ColOther = 11;
        public static readonly int LocCount = Enum.GetValues(typeof(Loc)).Length;

        public readonly Catalog Catalog;
        public readonly int[][] ByLoc;
        public int Unknown;
        public int ModdedGrades;

        public Collection(Catalog catalog)
        {
            Catalog = catalog;
            ByLoc = new int[LocCount][];
            for (int i = 0; i < LocCount; i++) ByLoc[i] = new int[catalog.Cards.Count * Cols];
        }

        private void Add(Loc loc, Card card, int grade, int copies)
        {
            if (copies <= 0) return;
            int col;
            if (grade <= 0) col = 0;
            else if (grade <= 10) col = grade;
            else
            {
                col = ColOther;
                ModdedGrades += copies;
            }
            ByLoc[(int)loc][card.Index * Cols + col] += copies;
        }

        /// <summary>Ungraded album counts (m_CardCollectedList*). Entries past the set's real cards are padding.</summary>
        public void AddAlbum(SetDef set, int list, IList<int>? counts)
        {
            if (counts == null) return;
            var cards = Catalog.CardsOf(set);
            int per = set.CardsPerList;
            int n = Math.Min(counts.Count, per);
            for (int i = 0; i < n; i++)
            {
                int c = counts[i];
                if (c > 0) Add(Loc.Binder, cards[list * per + i], 0, c);
            }
        }

        /// <summary>Graded album row: one copy, and `amount` holds the grade.</summary>
        public void AddGradedAlbumRow(CompactRec r)
        {
            var card = Catalog.FromCompact(r.Expansion, r.SaveIndex, r.Destiny);
            if (card == null || r.Amount <= 0)
            {
                Unknown++;
                return;
            }
            Add(Loc.Binder, card, r.Amount, 1);
        }

        public void AddPhysical(Loc loc, PhysicalRec r)
        {
            if (r.Monster <= 0) return; // empty display slot
            var card = Catalog.FromPhysical(r.Expansion, r.Monster, r.Border, r.Foil, r.Destiny);
            if (card == null)
            {
                Unknown++;
                return;
            }
            Add(loc, card, r.Grade, 1);
        }

        /// <summary>Container rows hold a copy count, except graded rows (gradedCardIndex &gt; 0) whose amount is the grade.</summary>
        public void AddCompact(Loc loc, CompactRec r)
        {
            var card = Catalog.FromCompact(r.Expansion, r.SaveIndex, r.Destiny);
            if (card == null)
            {
                Unknown++;
                return;
            }
            if (r.GradedIndex > 0) Add(loc, card, r.Amount, 1);
            else Add(loc, card, 0, r.Amount);
        }

        public int[] Combine(bool[] included)
        {
            var total = new int[Catalog.Cards.Count * Cols];
            for (int l = 0; l < LocCount; l++)
            {
                if (l >= included.Length || !included[l]) continue;
                var src = ByLoc[l];
                for (int i = 0; i < total.Length; i++) total[i] += src[i];
            }
            return total;
        }
    }

    /// <summary>A grid column: a single grade column, or a roll-up.</summary>
    public readonly struct Column : IEquatable<Column>
    {
        public const int AnyGraded = -1;
        public const int AnyForm = -2;

        public readonly int Value;

        public Column(int value) => Value = value;

        public static readonly Column Ungraded = new Column(0);
        public static readonly Column Graded = new Column(AnyGraded);
        public static readonly Column Any = new Column(AnyForm);

        public string Label =>
            Value == AnyForm ? "Any" :
            Value == AnyGraded ? "Graded" :
            Value == 0 ? "Ungraded" :
            Value == Collection.ColOther ? "Other" :
            Value.ToString();

        public string LongLabel =>
            Value == AnyForm ? "Any form" :
            Value == AnyGraded ? "Any grade" :
            Value == 0 ? "Ungraded" :
            Value == Collection.ColOther ? "Other grade" :
            "Grade " + Value;

        public bool Equals(Column other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is Column c && Equals(c);
        public override int GetHashCode() => Value;
    }

    public struct Agg
    {
        public int Owned;
        public int Total;
        public int Copies;
    }

    public static class Aggregate
    {
        public static int Copies(int[] totals, Card card, Column col)
        {
            int b = card.Index * Collection.Cols;
            if (col.Value >= 0) return totals[b + col.Value];
            int sum = 0;
            for (int c = col.Value == Column.AnyForm ? 0 : 1; c < Collection.Cols; c++) sum += totals[b + c];
            return sum;
        }

        public static Agg Of(IEnumerable<Card> cards, int[] totals, Column col)
        {
            var a = new Agg();
            foreach (var card in cards)
            {
                int n = Copies(totals, card, col);
                a.Total++;
                a.Copies += n;
                if (n > 0) a.Owned++;
            }
            return a;
        }
    }
}
