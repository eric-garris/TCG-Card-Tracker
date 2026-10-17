using TCGCardTracker.Core;
using Xunit;

namespace TCGCardTracker.Tests
{
    public class SlotPayoutsTests
    {
        [Fact]
        public void SharesIncludeLossesAndSkipFallbacks()
        {
            var rows = SlotPayouts.Rows("777|100|Legendary;777|0|Epic;R|150|Epic;che1|1000|Common", 8750);
            Assert.Equal("Legendary  1%", rows[1].Label);
            Assert.Equal("Epic  1.5%", rows[2].Label);
            Assert.Equal("Common  10%", rows[12].Label);
            Assert.Equal("No reward", rows[0].Label);
        }

        [Fact]
        public void SeveralRaritiesOnOneRow()
        {
            var rows = SlotPayouts.Rows("pika|30|Ultra Rare;PIKA|30|Rare PRIME", 940);
            Assert.Equal("Ultra Rare / Rare PRIME  6%", rows[3].Label);
        }
    }
}
