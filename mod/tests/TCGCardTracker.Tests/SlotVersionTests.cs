using TCGCardTracker.Core;
using Xunit;

namespace TCGCardTracker.Tests
{
    public class SlotVersionTests
    {
        [Theory]
        [InlineData("Common - Silver Foil", "Common", 2, true)]
        [InlineData("Legendary - Full Art", "Legendary", 5, false)]
        [InlineData("Super Legend - first edition", "Super Legend", 1, false)]
        [InlineData("Rare - Foil", "Rare", 0, true)]
        [InlineData("Epic - Base", "Epic", 0, false)]
        public void ReadsTheVersion(string name, string rarity, int border, bool foil)
        {
            Assert.True(SlotVersion.TryParse(name, out var r, out var b, out var f));
            Assert.Equal((rarity, border, foil), (r, b, f));
        }

        [Theory]
        [InlineData("Common")]
        [InlineData("Rare Holofoil")]
        [InlineData("Holo Rare - Shiny")]
        public void LeavesOtherNamesAlone(string name)
        {
            Assert.False(SlotVersion.TryParse(name, out _, out _, out _));
            Assert.Equal(name, SlotVersion.RarityOf(name));
        }

        [Fact]
        public void WinLabel() => Assert.Equal("COMMON SILVER FOIL", SlotVersion.WinLabel("Common", 2, true));

        [Fact]
        public void FitKeepsAVersionTheSetHas()
        {
            var mask = new bool[12];
            mask[2] = true; // Silver, not foil
            Assert.Equal((2, false), VariantRoll.Fit(2, true, () => 0f, mask));
            Assert.Equal((3, true), VariantRoll.Fit(3, true, () => 0f));
        }
    }
}
