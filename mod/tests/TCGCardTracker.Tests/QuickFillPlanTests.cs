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
    }
}
