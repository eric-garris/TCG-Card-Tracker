using System.Collections.Generic;
using TCGCardTracker.Core;
using Xunit;

namespace TCGCardTracker.Tests
{
    public class PromoRarityPlanTests
    {
        private const string Promo = "Common, Uncommon, Rare, Epic";
        private const string Shop = "Rare, Epic";
        private const string Costs = "Rare:2000, Epic:6000";

        private static KeyValuePair<string, int> R(string name, int tier) => new KeyValuePair<string, int>(name, tier);

        [Fact]
        public void AddsANewNameToAllThreeSettings()
        {
            var plan = PromoRarityPlan.Plan(Promo, Shop, Costs, new HashSet<string>(), new[] { R("Mythic Rare", 3) });
            Assert.Equal(new[] { "Mythic Rare" }, plan.Added);
            Assert.Equal("Common, Uncommon, Rare, Epic, Mythic Rare", plan.Promo);
            Assert.Equal("Rare, Epic, Mythic Rare", plan.Shop);
            Assert.Equal("Rare:2000, Epic:6000, Mythic Rare:35000", plan.Costs);
        }

        [Fact]
        public void LeavesNamesPointAndSlotsAlreadyKnows()
        {
            // "Uncommon" is a promo rarity but deliberately not in the shop: keep it that way.
            var plan = PromoRarityPlan.Plan(Promo, Shop, Costs, new HashSet<string>(), new[] { R("Uncommon", 1), R("Rare", 2) });
            Assert.False(plan.Changed);
            Assert.Equal(Shop, plan.Shop);
        }

        [Fact]
        public void NeverReaddsANameAddedBefore()
        {
            var plan = PromoRarityPlan.Plan("Common", "", "", new HashSet<string> { "Holo" }, new[] { R("Holo", 2) });
            Assert.False(plan.Changed);
        }

        [Fact]
        public void KeepsAnExistingPriceAndSkipsDuplicatesAndBadNames()
        {
            var plan = PromoRarityPlan.Plan("Common", "", "Holo:999", new HashSet<string>(),
                new[] { R("Holo", 2), R("Holo", 0), R("A, B", 1), R(" ", 0) });
            Assert.Equal(new[] { "Holo" }, plan.Added);
            Assert.Equal("Holo:999", plan.Costs);
            Assert.Equal("Holo", plan.Shop);
        }

        [Theory]
        [InlineData(0, 1, 0)]
        [InlineData(1, 3, 1)]
        [InlineData(3, 4, 3)]
        [InlineData(0, 6, 0)]
        [InlineData(2, 6, 1)]
        [InlineData(3, 6, 2)]
        [InlineData(5, 6, 3)]
        public void SpreadsRaritiesOverTiersLikeTcgStudio(int index, int count, int tier) =>
            Assert.Equal(tier, PromoRarityPlan.Tier(index, count));
    }
}
