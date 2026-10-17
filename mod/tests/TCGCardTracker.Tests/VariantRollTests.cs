using System;
using System.Collections.Generic;
using System.Linq;
using TCGCardTracker.Core;
using Xunit;

namespace TCGCardTracker.Tests
{
    public class VariantRollTests
    {
        private static Func<float> Rolls(params float[] values)
        {
            var queue = new Queue<float>(values);
            return () => queue.Count > 0 ? queue.Dequeue() : 99f;
        }

        [Fact]
        public void HighRollsGiveTheBaseCard() =>
            Assert.Equal((0, false), VariantRoll.Roll(Rolls(99, 99, 99, 99, 99, 99)));

        [Fact]
        public void ChecksTheRarestBorderFirst() =>
            Assert.Equal((5, false), VariantRoll.Roll(Rolls(0.1f, 99)));

        [Fact]
        public void FirstEditionAndFoil() =>
            Assert.Equal((1, true), VariantRoll.Roll(Rolls(99, 99, 99, 99, 10, 2)));

        [Fact]
        public void SkipsBordersTheSetLacks()
        {
            var mask = new bool[12];
            mask[0] = mask[3] = true; // Base and Gold, non-foil only
            // Full Art would hit on 0.1, but the set has none; Gold hits next.
            Assert.Equal((3, false), VariantRoll.Roll(Rolls(0.1f, 0.1f, 2), mask));
        }

        [Fact]
        public void FallsBackToAVersionTheSetHas()
        {
            var mask = new bool[12];
            mask[6 + 2] = true; // Silver foil only
            Assert.Equal((2, true), VariantRoll.Roll(Rolls(99, 99, 99, 99, 99, 99), mask));
        }

        [Fact]
        public void OddsRoughlyMatchPacks()
        {
            var random = new Random(7);
            var results = Enumerable.Range(0, 100000).Select(_ => VariantRoll.Roll(() => (float)random.NextDouble() * 100f)).ToList();
            double baseShare = results.Count(r => r.border == 0) / (double)results.Count;
            double foilShare = results.Count(r => r.foil) / (double)results.Count;
            Assert.InRange(baseShare, 0.68, 0.715); // 0.9975 * 0.99 * 0.96 * 0.92 * 0.8 ≈ 0.698
            Assert.InRange(foilShare, 0.045, 0.055);
        }
    }
}
