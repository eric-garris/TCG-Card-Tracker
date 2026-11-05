using TCGCardTracker.Core;
using Xunit;

namespace TCGCardTracker.Tests
{
    public class RestockPickTests
    {
        [Fact]
        public void EmptyListPicksNothing() => Assert.Equal(-1, RestockPick.MostValuable(new float[0]));

        [Fact]
        public void PicksHighestPrice() => Assert.Equal(2, RestockPick.MostValuable(new[] { 1.5f, 0.2f, 40f, 12f }));

        [Fact]
        public void TiesGoToFirst() => Assert.Equal(1, RestockPick.MostValuable(new[] { 3f, 9f, 9f }));

        [Fact]
        public void UnknownPriceCountsAsZero() => Assert.Equal(1, RestockPick.MostValuable(new[] { float.NaN, 0.5f }));

        [Fact]
        public void AllZeroPicksFirst() => Assert.Equal(0, RestockPick.MostValuable(new[] { 0f, 0f }));
    }
}
