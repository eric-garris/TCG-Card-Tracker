using TCGCardTracker.Core;
using Xunit;

namespace TCGCardTracker.Tests
{
    public class RewardLinesTests
    {
        private static Holding H(int ungraded, params (int col, int n)[] graded)
        {
            var h = new Holding { Ungraded = ungraded, ByCol = new int[Collection.Cols] };
            foreach (var (col, n) in graded) { h.ByCol[col] = n; h.Graded += n; }
            return h;
        }

        [Fact]
        public void NewCardShowsNewAndTheCopyJustReceived()
        {
            var lines = Holding.RewardLines("Roxy · Gold Edition · Foil", "PStars", H(1), 1, isNew: true);
            Assert.Equal("Roxy · Gold Edition · Foil  (PStars)  NEW", lines[0]);
            Assert.Equal("    You now own 1 copy  ·  Ungraded: 1", lines[1]);
        }

        [Fact]
        public void SeveralCopiesAndGradedOnes()
        {
            var lines = Holding.RewardLines("Pigni · EX", "Tetramon", H(3, (10, 2)), 2, isNew: false);
            Assert.Equal("Pigni · EX  (Tetramon)  x2", lines[0]);
            Assert.StartsWith("    You now own 5 copies  ·  Ungraded: 3  ·  Graded: 2", lines[1]);
        }
    }
}
