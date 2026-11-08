using System.Collections.Generic;
using TCGCardTracker.Core;
using Xunit;

namespace TCGCardTracker.Tests
{
    public class ScanScoresTests
    {
        private static List<CopyScore> Scores(params (double lo, double hi)[] s)
        {
            var list = new List<CopyScore>();
            foreach (var (lo, hi) in s) list.Add(new CopyScore(lo, hi));
            return list;
        }

        [Fact]
        public void RemainingSortsLowestFirst()
        {
            var r = ScanScores.Remaining(Scores((80, 80), (55, 60), (70, 70)), 0);
            Assert.Equal(new[] { 55.0, 70.0, 80.0 }, r.ConvertAll(s => s.Lo));
        }

        [Fact]
        public void ShelvedCopiesAreTheLowest()
        {
            var r = ScanScores.Remaining(Scores((80, 80), (55, 60), (70, 70)), 2);
            Assert.Single(r);
            Assert.Equal(80.0, r[0].Lo);
        }

        [Fact]
        public void MoreShelvedThanScannedLeavesNone() =>
            Assert.Empty(ScanScores.Remaining(Scores((40, 40)), 5));

        [Fact]
        public void OffShowsEverything() => Assert.True(ScanScores.Passes(Scores(), ScanFilter.Off, 70));

        [Fact]
        public void ScannedNeedsACopy()
        {
            Assert.False(ScanScores.Passes(Scores(), ScanFilter.Scanned, 70));
            Assert.True(ScanScores.Passes(Scores((99, 99)), ScanFilter.Scanned, 70));
        }

        [Fact]
        public void BelowNeedsTheWholeRangeUnder()
        {
            Assert.True(ScanScores.Passes(Scores((62, 62)), ScanFilter.Below, 70));
            Assert.False(ScanScores.Passes(Scores((65, 72)), ScanFilter.Below, 70)); // might be a good card
            Assert.False(ScanScores.Passes(Scores((70, 70)), ScanFilter.Below, 70)); // exactly at the line is not below
            Assert.True(ScanScores.Passes(Scores((90, 90), (60, 66)), ScanFilter.Below, 70));
        }

        [Fact]
        public void BadgeShowsLowestAndCount()
        {
            Assert.Null(ScanScores.Badge(Scores()));
            Assert.Equal("62%", ScanScores.Badge(ScanScores.Remaining(Scores((62, 62)), 0)));
            Assert.Equal("58-66% ·3", ScanScores.Badge(ScanScores.Remaining(Scores((90, 90), (58.4, 65.2), (75, 75)), 0)));
        }

        [Fact]
        public void PercentFormats()
        {
            Assert.Equal("69.5%", ScanScores.Percent(new CopyScore(69.5, 69.5)));
            Assert.Equal("99-100%", ScanScores.Percent(new CopyScore(99.2, 100.4)));
            Assert.Equal("70%", ScanScores.Percent(new CopyScore(70, 70.04)));
        }

        [Fact]
        public void BandsMatchCardGradeOverhaul()
        {
            Assert.Equal(0, ScanScores.Band(95));
            Assert.Equal(1, ScanScores.Band(94.9));
            Assert.Equal(2, ScanScores.Band(80));
            Assert.Equal(3, ScanScores.Band(70));
            Assert.Equal(4, ScanScores.Band(69.9));
        }

        [Fact]
        public void ThresholdIsClampedAndLabelled()
        {
            Assert.Equal(5f, ScanScores.Clamp(0f));
            Assert.Equal(100f, ScanScores.Clamp(150f));
            Assert.Equal(70f, ScanScores.Clamp(float.NaN));
            Assert.Equal("Below 65%", ScanScores.BelowLabel(65f));
        }
    

        [Fact]
        public void SortForMapsBinderSorts()
        {
            Assert.Equal(ScanSort.Price, ScanScores.SortFor("Price", false));
            Assert.Equal(ScanSort.TotalValue, ScanScores.SortFor("TotalValue", false));
            Assert.Equal(ScanSort.Amount, ScanScores.SortFor("Amount", false));
            Assert.Equal(ScanSort.Keep, ScanScores.SortFor("Name", false));
            Assert.Equal(ScanSort.Keep, ScanScores.SortFor(null, false));
            Assert.Equal(ScanSort.Score, ScanScores.SortFor("Price", true));
        }

        private static List<ScanSlot> Slots() => new List<ScanSlot>
        {
            new ScanSlot(0, 80, 1.5f, 1),  // card A, cheap version
            new ScanSlot(1, 60, 40f, 2),   // card B, expensive
            new ScanSlot(2, 85, 12f, 5),
            new ScanSlot(3, 60, 3f, 1),
        };

        [Fact]
        public void PriceSortIsPerVersion()
        {
            var s = Slots();
            ScanScores.Sort(s, ScanSort.Price);
            Assert.Equal(new[] { 1, 2, 3, 0 }, s.ConvertAll(x => x.Order));
        }

        [Fact]
        public void ScoreSortIsLowestFirstThenPrice()
        {
            var s = Slots();
            ScanScores.Sort(s, ScanSort.Score);
            Assert.Equal(new[] { 1, 3, 0, 2 }, s.ConvertAll(x => x.Order));
        }

        [Fact]
        public void TotalValueAndAmountSorts()
        {
            var s = Slots();
            ScanScores.Sort(s, ScanSort.TotalValue);
            Assert.Equal(new[] { 1, 2, 3, 0 }, s.ConvertAll(x => x.Order));
            s = Slots();
            ScanScores.Sort(s, ScanSort.Amount);
            Assert.Equal(new[] { 2, 1, 0, 3 }, s.ConvertAll(x => x.Order));
        }

        [Fact]
        public void KeepLeavesTheOrder()
        {
            var s = Slots();
            ScanScores.Sort(s, ScanSort.Keep);
            Assert.Equal(new[] { 0, 1, 2, 3 }, s.ConvertAll(x => x.Order));
        }
    }
}
