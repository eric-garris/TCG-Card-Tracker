using TCGCardTracker.Core;
using Xunit;

namespace TCGCardTracker.Tests
{
    public class TradePickTests
    {
        private static readonly int[] Custom = { 100, 101, 102 };

        [Fact]
        public void KeepsTheGameRollWithoutCustomSets() =>
            Assert.Equal(2, TradePick.Choose(2, new int[0], 1f, 0.0, 0.5));

        [Fact]
        public void KeepsTheGameRollWhenOff() =>
            Assert.Equal(0, TradePick.Choose(0, Custom, 0f, 0.0, 0.5));

        [Fact]
        public void KeepsASetTheSetModAlreadyPicked() =>
            Assert.Equal(101, TradePick.Choose(101, Custom, 1f, 0.0, 0.0));

        [Theory]
        [InlineData(0.24, 0.0, 100)]
        [InlineData(0.0, 0.5, 101)]
        [InlineData(0.1, 0.9999, 102)]
        [InlineData(0.25, 0.0, 0)]
        [InlineData(0.9, 0.0, 0)]
        public void SwapsInACustomSetAtTheChance(double roll, double pick, int expected) =>
            Assert.Equal(expected, TradePick.Choose(0, Custom, 0.25f, roll, pick));

        [Fact]
        public void AlwaysCustomAtFullChance() =>
            Assert.Equal(100, TradePick.Choose(7, Custom, 1f, 0.9999, 0.0));
    }
}
