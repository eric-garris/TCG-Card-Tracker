using System.Collections.Generic;
using System.Linq;
using TCGCardTracker.Core;
using Xunit;

namespace TCGCardTracker.Tests
{
    public class CollectionTests
    {
        private static readonly string[] Editions = { "Basic", "First Edition", "Silver", "Gold", "EX", "Full Art" };

        /// <summary>A catalog shaped like the game: 121 main monsters, Ghost's 20 legendaries.</summary>
        private static Catalog MakeCatalog()
        {
            var main = Enumerable.Range(1, 121).ToArray();
            var ghost = new[] { 4, 8, 12, 16, 20, 24, 28, 32, 42, 50, 51, 52, 53, 54, 59, 63, 67, 71, 75, 102 };
            var sets = new List<SetDef>
            {
                new SetDef { Key = "tetramon", Name = "Tetramon", ExpansionType = 0, Lists = new[] { new ListDef { Suffix = "" } }, Shown = main, Editions = Editions },
                new SetDef { Key = "destiny", Name = "Destiny", ExpansionType = 1, Lists = new[] { new ListDef { Suffix = "Destiny", IsDestiny = true } }, Shown = main, Editions = Editions },
                new SetDef { Key = "ascension", Name = "Ascension", ExpansionType = 7, Lists = new[] { new ListDef { Suffix = "Ascension" } }, Shown = main, Editions = Editions },
                new SetDef
                {
                    Key = "ghost", Name = "Ghost", ExpansionType = 2, PerNoFoil = 1, Shown = ghost, Editions = new[] { "Full Art" },
                    Lists = new[] { new ListDef { Suffix = "Ghost", Label = "White" }, new ListDef { Suffix = "GhostBlack", IsDestiny = true, Label = "Black" } },
                },
            };
            return Catalog.Build(sets, id => id == 1 ? "Pigni" : id == 2 ? "Burpig" : id == 102 ? "Katengu" : "#" + id, id => id % 4 == 0 ? Tier.Legendary : Tier.Common);
        }

        private static readonly Catalog Cat = MakeCatalog();
        private static readonly bool[] All = Enumerable.Repeat(true, Collection.LocCount).ToArray();

        [Fact]
        public void CatalogMatchesGameCardCounts()
        {
            Assert.Equal(1452, Cat.CardsOf(Cat.SetForExpansion(0)!).Count);
            Assert.Equal(1452, Cat.CardsOf(Cat.SetForExpansion(7)!).Count);
            Assert.Equal(80, Cat.CardsOf(Cat.SetForExpansion(2)!).Count);
            Assert.Equal(new[] { "White", "Black" }, Cat.GroupsOf(Cat.SetForExpansion(2)!));
        }

        [Fact]
        public void SaveIndexLayoutMatchesGetCardSaveIndex()
        {
            Assert.Equal("Pigni · Basic", Cat.FromCompact(0, 0, false)!.Label);
            Assert.Equal("Pigni · Basic · Foil", Cat.FromCompact(0, 6, false)!.Label);
            Assert.Equal(21, Cat.FromPhysical(0, 2, 3, true, false)!.SaveIndex); // Burpig Gold foil
            var k = Cat.FromPhysical(2, 102, 0, true, true)!;
            Assert.Equal((1, 39, "Katengu"), (k.List, k.SaveIndex, k.Name));
            Assert.Null(Cat.FromPhysical(2, 1, 0, false, false)); // not a Ghost monster
            Assert.Same(Cat.FromCompact(1, 30, true), Cat.FromCompact(1, 30, false)); // isDestiny only matters for Ghost
        }

        [Fact]
        public void CountsEveryLocationWithTheRightMeaningOfAmount()
        {
            var c = new Collection(Cat);
            var tetramon = Cat.SetForExpansion(0)!;
            var album = new int[2100];
            album[21] = 2;
            album[2000] = 99; // padding past the real cards is ignored
            c.AddAlbum(tetramon, 0, album);
            c.AddGradedAlbumRow(new CompactRec { Expansion = 0, SaveIndex = 21, Amount = 10, GradedIndex = 1 });
            c.AddGradedAlbumRow(new CompactRec { Expansion = 0, SaveIndex = 21, Amount = 10, GradedIndex = 2 });
            c.AddPhysical(Loc.Displays, new PhysicalRec { Expansion = 0, Monster = 2, Border = 3, Foil = true, Grade = 8 });
            c.AddPhysical(Loc.Displays, new PhysicalRec { Monster = 0 }); // empty slot
            c.AddCompact(Loc.Storage, new CompactRec { Expansion = 1, SaveIndex = 0, Amount = 5 });
            c.AddCompact(Loc.Storage, new CompactRec { Expansion = 1, SaveIndex = 0, Amount = 6, GradedIndex = 9 });
            c.AddPhysical(Loc.Hand, new PhysicalRec { Expansion = 0, Monster = 9999 });
            c.AddGradedAlbumRow(new CompactRec { Expansion = 0, SaveIndex = 0, Amount = 380009117, GradedIndex = 3 });

            var burpig = Cat.FromCompact(0, 21, false)!;
            var destinyPigni = Cat.FromCompact(1, 0, false)!;
            int At(Loc l, Card card, int col) => c.ByLoc[(int)l][card.Index * Collection.Cols + col];

            Assert.Equal(2, At(Loc.Binder, burpig, 0));
            Assert.Equal(2, At(Loc.Binder, burpig, 10));
            Assert.Equal(1, At(Loc.Displays, burpig, 8));
            Assert.Equal(5, At(Loc.Storage, destinyPigni, 0));
            Assert.Equal(1, At(Loc.Storage, destinyPigni, 6));
            Assert.Equal(1, At(Loc.Binder, Cat.FromCompact(0, 0, false)!, Collection.ColOther));
            Assert.Equal(1, c.ModdedGrades);
            Assert.Equal(1, c.Unknown);
        }

        [Fact]
        public void VanillaOverTenGradeCountsAsTen()
        {
            var c = new Collection(Cat);
            c.AddCompact(Loc.Storage, new CompactRec { Expansion = 0, SaveIndex = 0, Amount = 14, GradedIndex = 5 });
            var pigni = Cat.FromCompact(0, 0, false)!;
            Assert.Equal(1, c.ByLoc[(int)Loc.Storage][pigni.Index * Collection.Cols + 10]);
            Assert.Equal(0, c.ModdedGrades);
        }

        [Fact]
        public void AggregatesUniqueAndCopiesPerColumn()
        {
            var c = new Collection(Cat);
            var tetramon = Cat.SetForExpansion(0)!;
            var album = new int[1452];
            album[0] = 3;
            album[1] = 1;
            c.AddAlbum(tetramon, 0, album);
            c.AddGradedAlbumRow(new CompactRec { Expansion = 0, SaveIndex = 0, Amount = 10, GradedIndex = 1 });
            c.AddGradedAlbumRow(new CompactRec { Expansion = 0, SaveIndex = 2, Amount = 10, GradedIndex = 2 });
            c.AddGradedAlbumRow(new CompactRec { Expansion = 0, SaveIndex = 2, Amount = 9, GradedIndex = 3 });
            c.AddCompact(Loc.Decks, new CompactRec { Expansion = 0, SaveIndex = 5, Amount = 4 });

            var cards = Cat.CardsOf(tetramon);
            var totals = c.Combine(All);
            Assert.Equal((3, 1452, 8), Tuple(Aggregate.Of(cards, totals, Column.Ungraded)));
            Assert.Equal((2, 1452, 2), Tuple(Aggregate.Of(cards, totals, new Column(10))));
            Assert.Equal((2, 1452, 3), Tuple(Aggregate.Of(cards, totals, Column.Graded)));
            Assert.Equal((4, 1452, 11), Tuple(Aggregate.Of(cards, totals, Column.Any)));

            var binderOnly = c.Combine(new[] { true, false, false, false, false, false, false, false, false });
            Assert.Equal((2, 1452, 4), Tuple(Aggregate.Of(cards, binderOnly, Column.Ungraded)));
        }

        private static (int, int, int) Tuple(Agg a) => (a.Owned, a.Total, a.Copies);
    }
}
