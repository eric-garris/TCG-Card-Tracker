using TCGCardTracker.Core;
using Xunit;

namespace TCGCardTracker.Tests
{
    public class LadderTests
    {
        [Theory]
        [InlineData(1000, 1)]
        [InlineData(1049, 1)]
        [InlineData(1050, 2)]
        [InlineData(3450, 50)]
        [InlineData(9000, 50)]
        [InlineData(500, 1)]
        public void RankIsEvery50Rating(int rating, int rank) => Assert.Equal(rank, Ladder.Rank(rating));

        [Fact]
        public void EvenGamesMoveRating16()
        {
            Assert.Equal(1216, Ladder.Rate(1200, 0.5f, DuelResult.Win));
            Assert.Equal(1184, Ladder.Rate(1200, 0.5f, DuelResult.Loss));
            Assert.Equal(1200, Ladder.Rate(1200, 0.5f, DuelResult.Draw));
        }

        [Fact]
        public void BeatingAnEasyDeckGainsLittleAndLosingToItCostsALot()
        {
            Assert.Equal(1206, Ladder.Rate(1200, 0.8f, DuelResult.Win));
            Assert.Equal(1174, Ladder.Rate(1200, 0.8f, DuelResult.Loss));
        }

        [Fact]
        public void RatingNeverDropsBelowTheStart() => Assert.Equal(Ladder.StartRating, Ladder.Rate(1005, 0.5f, DuelResult.Loss));

        [Theory]
        [InlineData(1, 1)]
        [InlineData(20, 20)]
        [InlineData(50, 2193)]
        public void PrizeGrowsPerRank(int rank, int packs) => Assert.Equal(packs, Ladder.PrizePacks(rank, 1.17f, 1f));

        [Fact]
        public void ResultPacks()
        {
            Assert.Equal(20, Ladder.ResultPacks(DuelResult.Win, 20, 1.17f, 1f));
            Assert.Equal(10, Ladder.ResultPacks(DuelResult.Draw, 20, 1.17f, 1f));
            Assert.Equal(0, Ladder.ResultPacks(DuelResult.Loss, 20, 1.17f, 1f));
            Assert.Equal(0, Ladder.ResultPacks(DuelResult.Win, 20, 1.17f, 0f));
        }

        [Theory]
        [InlineData(0, 5)]
        [InlineData(1, 3)]
        [InlineData(2, 2)]
        [InlineData(7, 1)]
        public void Placement(int index, int multiplier) => Assert.Equal(multiplier, Ladder.PlacementMultiplier(index));

        [Fact]
        public void MagicPowerSpansWeakToStrong()
        {
            Assert.Equal(-1.0, Ladder.MagicPower(1), 6);
            Assert.Equal(1.0, Ladder.MagicPower(50), 6);
            Assert.Equal(-1.0 / 49, Ladder.MagicPower(25), 6);
        }

        [Fact]
        public void DecksAreDealtByHowOftenYouBeatThem()
        {
            var s = new LadderState();
            // Deck 2 beaten every time, deck 0 never, deck 1 in between, deck 3 unplayed (50%).
            for (int i = 0; i < 5; i++)
            {
                Ladder.RecordDeck(s, 2, DuelResult.Win);
                Ladder.RecordDeck(s, 0, DuelResult.Loss);
            }
            Ladder.RecordDeck(s, 1, DuelResult.Win);
            Ladder.RecordDeck(s, 1, DuelResult.Win);
            Ladder.RecordDeck(s, 1, DuelResult.Loss);
            // Easiest to hardest: 2, 1, 3, 0.
            Assert.Equal(2, Ladder.PickDeck(s, 4, 1, 0.0));
            Assert.Equal(0, Ladder.PickDeck(s, 4, 50, 0.99));
            Assert.Equal(3, Ladder.PickDeck(s, 4, 50, 0.0));
        }

        [Fact]
        public void VaultAddsAndTakes()
        {
            var save = new LadderSave();
            save.AddPacks(5, 10);
            save.AddPacks(5, 2);
            save.AddPacks(7, 3);
            Assert.Equal(15, save.VaultPacks());
            save.TakePacks(5, 12);
            Assert.Single(save.vault);
            Assert.Equal(3, save.VaultPacks());
        }
    }
}
