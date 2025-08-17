// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Collections;

namespace TemporalCollections.Tests.Collections
{
    public class TemporalSetTests
    {
        private static (TemporalSet<string> set, DateTimeOffset tA, DateTimeOffset tB, DateTimeOffset tC, DateTimeOffset tD) CreateSetABCD()
        {
            var set = new TemporalSet<string>();
            set.Add("A");
            set.Add("B");
            set.Add("C");
            set.Add("D");

            var snapshot = set.GetItems().OrderBy(i => i.Timestamp).ToList();
            Assert.Equal(4, snapshot.Count);

            return (set, snapshot[0].Timestamp, snapshot[1].Timestamp, snapshot[2].Timestamp, snapshot[3].Timestamp);
        }

        [Fact]
        public void Add_ShouldAddNewItem_AndReturnTrue()
        {
            var set = new TemporalSet<string>();

            var result = set.Add("A");

            Assert.True(result);
            Assert.True(set.Contains("A"));
            Assert.Equal(1, set.Count);

            var snapshot = set.GetItems().ToList();
            Assert.Single(snapshot);
            Assert.Equal("A", snapshot[0].Value);

            var now = DateTimeOffset.UtcNow;
            Assert.InRange(snapshot[0].Timestamp, now.AddMinutes(-1), now.AddSeconds(1));
        }

        [Fact]
        public void Add_Duplicate_ShouldReturnFalse_AndNotIncreaseCount()
        {
            var set = new TemporalSet<string>();

            var firstAdd = set.Add("A");
            var secondAdd = set.Add("A");

            Assert.True(firstAdd);
            Assert.False(secondAdd);
            Assert.Equal(1, set.Count);
        }

        [Fact]
        public void Remove_ShouldRemoveItemIfExists()
        {
            var set = new TemporalSet<int>();
            set.Add(42);

            Assert.True(set.Contains(42));

            var removed = set.Remove(42);

            Assert.True(removed);
            Assert.False(set.Contains(42));
            Assert.Equal(0, set.Count);
        }

        [Fact]
        public void Remove_NonExisting_ShouldReturnFalse()
        {
            var set = new TemporalSet<int>();

            var removed = set.Remove(999);

            Assert.False(removed);
        }

        [Fact]
        public void Add_ShouldBeThreadSafe_WhenManyParallelAdds()
        {
            var set = new TemporalSet<int>();

            Parallel.For(0, 2000, i =>
            {
                set.Add(i);
            });

            Assert.Equal(2000, set.Count);
            Assert.True(set.Contains(0));
            Assert.True(set.Contains(1999));
        }

        [Fact]
        public void GetEarliest_ShouldReturnFirstItem()
        {
            var (set, tA, _, _, _) = CreateSetABCD();

            var earliest = set.GetEarliest();

            Assert.NotNull(earliest);
            Assert.Equal("A", earliest!.Value);
            Assert.Equal(tA, earliest.Timestamp);
        }

        [Fact]
        public void GetLatest_ShouldReturnLastItem()
        {
            var (set, _, _, _, tD) = CreateSetABCD();

            var latest = set.GetLatest();

            Assert.NotNull(latest);
            Assert.Equal("D", latest!.Value);
            Assert.Equal(tD, latest.Timestamp);
        }

        [Fact]
        public void GetTimeSpan_ShouldBeDifferenceBetweenEarliestAndLatest()
        {
            var (set, tA, _, _, tD) = CreateSetABCD();

            var span = set.GetTimeSpan();

            Assert.Equal(tD - tA, span);
        }

        [Fact]
        public void GetInRange_ShouldReturnItemsWithinInclusiveBounds()
        {
            var (set, _, tB, tC, _) = CreateSetABCD();

            var items = set.GetInRange(tB.UtcDateTime, tC.UtcDateTime).Select(x => x.Value).ToList();

            Assert.Equal(new[] { "B", "C" }, items);
        }

        [Fact]
        public void CountInRange_ShouldReturnCorrectCount()
        {
            var (set, _, tB, tC, _) = CreateSetABCD();

            var count = set.CountInRange(tB.UtcDateTime, tC.UtcDateTime);

            Assert.Equal(2, count);
        }

        [Fact]
        public void GetBefore_ShouldReturnItemsStrictlyBeforeTime()
        {
            var (set, tA, tB, _, _) = CreateSetABCD();

            var midABTicks = (tA.UtcTicks + tB.UtcTicks) / 2;
            var midAB = new DateTimeOffset(midABTicks, TimeSpan.Zero);

            var items = set.GetBefore(midAB.UtcDateTime).Select(x => x.Value).ToList();

            Assert.Equal(new[] { "A" }, items);
        }

        [Fact]
        public void GetAfter_ShouldReturnItemsStrictlyAfterTime()
        {
            var (set, _, _, tC, tD) = CreateSetABCD();

            var midCDTicks = (tC.UtcTicks + tD.UtcTicks) / 2;
            var midCD = new DateTimeOffset(midCDTicks, TimeSpan.Zero);

            var items = set.GetAfter(midCD.UtcDateTime).Select(x => x.Value).ToList();

            Assert.Equal(new[] { "D" }, items);
        }

        [Fact]
        public void RemoveRange_ShouldRemoveItemsWithinInclusiveBounds()
        {
            var (set, _, tB, tC, _) = CreateSetABCD();

            set.RemoveRange(tB.UtcDateTime, tC.UtcDateTime);

            Assert.Equal(2, set.Count);
            Assert.True(set.Contains("A"));
            Assert.True(set.Contains("D"));
            Assert.False(set.Contains("B"));
            Assert.False(set.Contains("C"));
        }

        [Fact]
        public void RemoveOlderThan_ShouldRemoveStrictlyOlderItems()
        {
            var (set, tA, _, _, tD) = CreateSetABCD();

            set.RemoveOlderThan(tD.UtcDateTime);

            Assert.Equal(1, set.Count);
            Assert.True(set.Contains("D"));
            Assert.False(set.Contains("A"));
        }

        [Fact]
        public void Clear_ShouldEmptyTheSet()
        {
            var (set, _, _, _, _) = CreateSetABCD();

            set.Clear();

            Assert.Equal(0, set.Count);
            Assert.Empty(set.GetItems());
            Assert.Null(set.GetEarliest());
            Assert.Null(set.GetLatest());
            Assert.Equal(TimeSpan.Zero, set.GetTimeSpan());
        }

        [Fact]
        public void CountSince_ShouldBeInclusive_AndConsistentWithGetInRange()
        {
            var (set, _, _, tC, _) = CreateSetABCD();

            // Cutoff at C's timestamp: expect C and D (inclusive)
            var cutoff = tC.UtcDateTime;

            var countSince = set.CountSince(cutoff);
            Assert.Equal(2, countSince);

            // Cross-check with GetInRange(cutoff, now)
            var cross = set.GetInRange(cutoff, DateTime.UtcNow).Count();
            Assert.Equal(cross, countSince);
        }
    }
}