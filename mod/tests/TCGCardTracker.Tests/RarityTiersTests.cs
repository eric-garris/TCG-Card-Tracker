using TCGCardTracker.Core;
using Xunit;

namespace TCGCardTracker.Tests
{
    public class RarityTiersTests
    {
        [Fact]
        public void KeepsVanillaValues()
        {
            var t = RarityTiers.ForSet(new[] { 0, 3, 0 });
            Assert.Equal(Tier.Common, t[0]);
            Assert.Equal(Tier.Legendary, t[3]);
        }

        [Fact]
        public void RanksTcgStudioValuesWithinTheSet()
        {
            // TCG Studio 2.x: Common, Rare, Epic, Legendary of a later set are 1012..1015.
            var t = RarityTiers.ForSet(new[] { 1013, 1012, 1015, 1014, 1012 });
            Assert.Equal(Tier.Common, t[1012]);
            Assert.Equal(Tier.Rare, t[1013]);
            Assert.Equal(Tier.Epic, t[1014]);
            Assert.Equal(Tier.Legendary, t[1015]);
        }

        [Fact]
        public void SpreadsMoreThanFourRarities()
        {
            var t = RarityTiers.ForSet(new[] { 1000, 1001, 1002, 1003, 1004, 1005, 1006 });
            Assert.Equal(Tier.Common, t[1000]);
            Assert.Equal(Tier.Legendary, t[1006]);
            Assert.Equal(Tier.Rare, t[1002]);
        }
    }
}
