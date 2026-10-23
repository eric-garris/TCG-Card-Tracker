using TCGCardTracker.Core;
using Xunit;

namespace TCGCardTracker.Tests
{
    public class SaleSpeedTests
    {
        [Theory]
        [InlineData(1f, 0.0, 0.75f)]
        [InlineData(1f, 0.5, 1f)]
        [InlineData(1f, 0.9999, 1.25f)]
        [InlineData(0.01f, 0.5, 0.1f)]
        [InlineData(float.NaN, 0.5, 1f)]
        public void CustomerIntervalVariesByAQuarter(float seconds, double roll, float expected) =>
            Assert.Equal(expected, SaleSpeed.CustomerInterval(seconds, roll), 3);

        [Theory]
        [InlineData(10f, 0.0, 5)]
        [InlineData(10f, 0.9999, 10)]
        [InlineData(9f, 0.0, 5)]
        [InlineData(1f, 0.9999, 1)]
        [InlineData(0f, 0.5, 1)]
        [InlineData(2f, 0.0, 1)]
        [InlineData(2f, 0.9999, 2)]
        public void PurchaseCountIsHalfToAllOfTheMax(float max, double roll, int expected) =>
            Assert.Equal(expected, SaleSpeed.PurchaseCount(max, roll));

        [Fact]
        public void RecheckScalesTheirDelay()
        {
            Assert.Equal(3.75, SaleSpeed.Recheck(15, 5f), 3);
            Assert.Equal(6.25, SaleSpeed.Recheck(25, 5f), 3);
            Assert.Equal(15, SaleSpeed.Recheck(15, 20f), 3);
            Assert.Equal(0.5, SaleSpeed.Recheck(1, 1f), 3);
            Assert.Equal(15, SaleSpeed.Recheck(15, 0f), 3);
        }

        [Fact]
        public void FirstCheckScalesAndStaysWhole()
        {
            Assert.Equal(75, SaleSpeed.FirstCheck(300, 5f));
            Assert.Equal(4, SaleSpeed.FirstCheck(15, 5f));
            Assert.Equal(1, SaleSpeed.FirstCheck(2, 1f));
        }
    }
}
