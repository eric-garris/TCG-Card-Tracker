using System.Linq;
using TCGCardTracker.Core;
using Xunit;

namespace TCGCardTracker.Tests
{
    public class QuickFillPlanTests
    {
        private static QuickFillPlan.Candidate<string> C(string card, int spares) => new QuickFillPlan.Candidate<string>(card, spares);

        [Fact]
        public void MostSparesGoFirstAcrossSets()
        {
            var plan = QuickFillPlan.Plan(new[] { C("A1", 2), C("B1", 9), C("C1", 5), C("B2", 5) }, 100, 50, (a, b) => a);
            Assert.Equal(new[] { "B1", "C1", "B2", "A1" }, plan.Select(p => p.card));
            Assert.Equal(new[] { 9, 5, 5, 2 }, plan.Select(p => p.amount));
        }

        [Fact]
        public void StopsWhenTheBoxIsFull()
        {
            var plan = QuickFillPlan.Plan(new[] { C("A", 6), C("B", 6) }, 8, 50, (a, b) => a);
            Assert.Equal(new[] { ("A", 6), ("B", 2) }, plan);
        }

        [Fact]
        public void StopsAtTheEntryLimit()
        {
            var plan = QuickFillPlan.Plan(new[] { C("A", 1), C("B", 1), C("C", 1) }, 100, 2, (a, b) => a);
            Assert.Equal(2, plan.Count);
        }

        [Fact]
        public void SkipsCardsWithoutSparesAndAddsNoEmptyEntries()
        {
            var plan = QuickFillPlan.Plan(new[] { C("A", 0), C("B", 3) }, 100, 50, (a, b) => a);
            Assert.Equal(new[] { ("B", 3) }, plan);
        }

        [Fact]
        public void HugeStacksGiveAtMostAThousandPerTurn()
        {
            var plan = QuickFillPlan.Plan(new[] { C("A", 2500) }, 5000, 50, (a, b) => b - 1);
            Assert.All(plan, p => Assert.True(p.amount <= 1000));
            Assert.Equal(2500, plan.Sum(p => p.amount));
        }

        [Fact]
        public void BundleTakesExactlyTheBoxSizeAtMostTenPerTurn()
        {
            var cands = new[] { C("A", 50), C("B", 50), C("C", 50) };
            var plan = QuickFillPlan.Bundle(cands, 100, 10, (a, b) => b - 1);
            Assert.Equal(100, plan.Sum(p => p.amount));
            Assert.All(plan, p => Assert.InRange(p.amount, 1, 10));
            Assert.Equal(new[] { "A", "B", "C" }, plan.Take(3).Select(p => p.card));
        }

        [Fact]
        public void BundleNeedsEnoughSpares()
        {
            Assert.Empty(QuickFillPlan.Bundle(new[] { C("A", 60), C("B", 39) }, 100, 10, (a, b) => a));
            Assert.Equal(100, QuickFillPlan.Bundle(new[] { C("A", 60), C("B", 40) }, 100, 10, (a, b) => a).Sum(p => p.amount));
        }
    }
}
