using TCGCardTracker.Core;
using Xunit;

namespace TCGCardTracker.Tests
{
    public class TradeRequestTests
    {
        private static readonly float[] Prices = { 1f, 8f, 10f, 14.99f, 15f, 40f };

        [Fact]
        public void KeepsTheGamePickWhenOff() =>
            Assert.Equal(-1, TradeRequest.Choose(Prices, 10f, 0f, 0.0, 0.5));

        [Fact]
        public void KeepsTheGamePickAboveTheChance() =>
            Assert.Equal(-1, TradeRequest.Choose(Prices, 10f, 0.25f, 0.25, 0.5));

        [Fact]
        public void KeepsTheGamePickWithoutAFairCard() =>
            Assert.Equal(-1, TradeRequest.Choose(new[] { 1f, 40f, 0f }, 10f, 1f, 0.0, 0.5));

        [Fact]
        public void KeepsTheGamePickWithoutCards() =>
            Assert.Equal(-1, TradeRequest.Choose(new float[0], 10f, 1f, 0.0, 0.5));

        [Theory]
        [InlineData(0.0, 1)]
        [InlineData(0.5, 2)]
        [InlineData(0.9999, 3)]
        public void PicksOnlyAmongFairlyPricedCards(double pick, int expected) =>
            Assert.Equal(expected, TradeRequest.Choose(Prices, 10f, 0.25f, 0.1, pick));

        [Theory]
        [InlineData(7.5f, true)]
        [InlineData(7.49f, false)]
        [InlineData(14.99f, true)]
        [InlineData(15f, false)]
        [InlineData(0f, false)]
        public void FairPriceWindow(float price, bool fair) =>
            Assert.Equal(fair, TradeRequest.FairPrice(price, 10f));
    }
}
