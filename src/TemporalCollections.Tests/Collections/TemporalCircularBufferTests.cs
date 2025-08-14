// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Collections;

namespace TemporalCollections.Tests.Collections
{
    public class TemporalCircularBufferTests
    {
        [Fact]
        public void Constructor_Throws_OnNonPositiveCapacity()
        {
            Assert.Throws<ArgumentException>(() => new TemporalCircularBuffer<int>(0));
            Assert.Throws<ArgumentException>(() => new TemporalCircularBuffer<int>(-1));
        }

        [Fact]
        public void Add_IncreasesCount_UntilCapacity()
        {
            var buffer = new TemporalCircularBuffer<string>(3);

            Assert.Equal(0, buffer.Count);

            buffer.Add("a");
            buffer.Add("b");

            Assert.Equal(2, buffer.Count);

            buffer.Add("c");
            Assert.Equal(3, buffer.Count);

            // Adding beyond capacity does not increase count past Capacity
            buffer.Add("d");
            Assert.Equal(3, buffer.Count);
        }

        [Fact]
        public void GetSnapshot_ReturnsItemsInOrder_OldestToNewest()
        {
            var buffer = new TemporalCircularBuffer<string>(3);

            buffer.Add("a");
            Thread.Sleep(1);
            buffer.Add("b");
            Thread.Sleep(1);
            buffer.Add("c");

            var snapshot = buffer.GetSnapshot().Select(i => i.Value).ToList();

            Assert.Equal(new[] { "a", "b", "c" }, snapshot);

            // Overwrite oldest item ("a") with "d"
            buffer.Add("d");
            var snapshot2 = buffer.GetSnapshot().Select(i => i.Value).ToList();

            Assert.Equal(new[] { "b", "c", "d" }, snapshot2);
        }

        [Fact]
        public void GetInRange_FiltersCorrectly()
        {
            var buffer = new TemporalCircularBuffer<string>(5);

            buffer.Add("old");
            Thread.Sleep(20);
            var from = DateTime.UtcNow;
            Thread.Sleep(10);
            buffer.Add("inrange");
            Thread.Sleep(10);
            var to = DateTime.UtcNow;
            Thread.Sleep(10);
            buffer.Add("new");

            var inRange = buffer.GetInRange(from, to).Select(i => i.Value).ToList();

            Assert.Contains("inrange", inRange);
            Assert.DoesNotContain("old", inRange);
            Assert.DoesNotContain("new", inRange);
        }

        [Fact]
        public void RemoveOlderThan_RemovesOldItems()
        {
            var buffer = new TemporalCircularBuffer<string>(4);

            buffer.Add("first");
            Thread.Sleep(10);
            var cutoff = DateTime.UtcNow;
            Thread.Sleep(10);
            buffer.Add("second");
            buffer.Add("third");

            buffer.RemoveOlderThan(cutoff);

            var snapshot = buffer.GetSnapshot().Select(i => i.Value).ToList();

            Assert.DoesNotContain("first", snapshot);
            Assert.Contains("second", snapshot);
            Assert.Contains("third", snapshot);
            Assert.Equal(2, buffer.Count);
        }

        [Fact]
        public void GetTimeSpan_ShouldBeDifferenceBetweenEarliestAndLatest()
        {
            var buffer = new TemporalCircularBuffer<int>(10);

            buffer.Add(1);
            Thread.Sleep(5);
            buffer.Add(2);

            var all = buffer.GetSnapshot().OrderBy(i => i.Timestamp).ToList();
            Assert.True(all.Count >= 2, "Need at least two items for a non-zero span.");

            var expected = all[^1].Timestamp - all[0].Timestamp;
            Assert.Equal(expected, buffer.GetTimeSpan());
        }

        [Fact]
        public void CountInRange_ShouldReturnCorrectCount()
        {
            var buffer = new TemporalCircularBuffer<int>(10);

            buffer.Add(1);
            Thread.Sleep(5);
            var split = DateTime.UtcNow;
            Thread.Sleep(5);
            buffer.Add(2);
            Thread.Sleep(5);
            buffer.Add(3);

            var from = split;
            var to = DateTime.UtcNow.AddMinutes(1);

            var expected = buffer.GetInRange(from, to).Count();
            var counted = buffer.CountInRange(from, to);

            Assert.Equal(expected, counted);
        }

        [Fact]
        public void GetBefore_ShouldReturnItemsStrictlyBeforeTime()
        {
            var buffer = new TemporalCircularBuffer<string>(10);

            buffer.Add("A");
            Thread.Sleep(5);
            var split = DateTime.UtcNow;
            Thread.Sleep(5);
            buffer.Add("B");

            var before = buffer.GetBefore(split).Select(x => x.Value).ToList();

            Assert.Contains("A", before);
            Assert.DoesNotContain("B", before);
        }

        [Fact]
        public void GetAfter_ShouldReturnItemsStrictlyAfterTime()
        {
            var buffer = new TemporalCircularBuffer<string>(10);

            buffer.Add("A");
            Thread.Sleep(5);
            var split = DateTime.UtcNow;
            Thread.Sleep(5);
            buffer.Add("B");

            var after = buffer.GetAfter(split).Select(x => x.Value).ToList();

            Assert.Contains("B", after);
            Assert.DoesNotContain("A", after);
        }

        [Fact]
        public void GetEarliest_And_GetLatest_ShouldReturnOldestAndNewest()
        {
            var buffer = new TemporalCircularBuffer<string>(10);

            buffer.Add("first");
            Thread.Sleep(5);
            buffer.Add("middle");
            Thread.Sleep(5);
            buffer.Add("last");

            var earliest = buffer.GetEarliest();
            var latest = buffer.GetLatest();

            Assert.NotNull(earliest);
            Assert.NotNull(latest);

            Assert.Equal("first", earliest!.Value);
            Assert.Equal("last", latest!.Value);

            // Sanity check against ordered snapshot
            var ordered = buffer.GetSnapshot().OrderBy(i => i.Timestamp).ToList();
            Assert.Equal(ordered.First().Timestamp, earliest.Timestamp);
            Assert.Equal(ordered.Last().Timestamp, latest.Timestamp);
        }

        [Fact]
        public void RemoveRange_ShouldDeleteItemsWithinInclusiveBounds()
        {
            // Capacity >= 4 so no overwrites during the test.
            var buffer = new TemporalCircularBuffer<int>(10);

            buffer.Add(1);                // A
            Thread.Sleep(5);
            var tStart = DateTime.UtcNow;
            Thread.Sleep(5);
            buffer.Add(2);                // B (in range)
            Thread.Sleep(5);
            buffer.Add(3);                // C (in range)
            Thread.Sleep(5);
            var tEnd = DateTime.UtcNow;
            Thread.Sleep(5);
            buffer.Add(4);                // D

            // Remove B and C (timestamps between tStart and tEnd inclusive)
            buffer.RemoveRange(tStart, tEnd);

            var remaining = buffer.GetSnapshot().Select(i => i.Value).ToList();

            Assert.Contains(1, remaining);   // A remains
            Assert.Contains(4, remaining);   // D remains
            Assert.DoesNotContain(2, remaining); // B removed
            Assert.DoesNotContain(3, remaining); // C removed
        }

        [Fact]
        public void Clear_ShouldEmptyBufferAndResetQueryableState()
        {
            var buffer = new TemporalCircularBuffer<int>(5);

            buffer.Add(10);
            buffer.Add(20);

            buffer.Clear();

            Assert.Equal(0, buffer.Count);
            Assert.Empty(buffer.GetSnapshot());
            Assert.Empty(buffer.GetInRange(DateTime.MinValue, DateTime.MaxValue));
            Assert.Equal(TimeSpan.Zero, buffer.GetTimeSpan());
            Assert.Null(buffer.GetEarliest());
            Assert.Null(buffer.GetLatest());
        }

        [Fact]
        public void Add_ShouldBeThreadSafe_WhenManyParallelAdds()
        {
            // Capacity guarantees no overwrite during the test
            var capacity = 2000;
            var buffer = new TemporalCircularBuffer<int>(capacity);

            Parallel.For(0, 1000, i => buffer.Add(i));

            Assert.Equal(1000, buffer.Count);

            // Check earliest/latest consistency
            var earliest = buffer.GetEarliest();
            var latest = buffer.GetLatest();
            Assert.NotNull(earliest);
            Assert.NotNull(latest);
            Assert.True(earliest!.Timestamp <= latest!.Timestamp);
        }

        [Fact]
        public void Add_BeyondCapacity_ShouldOverwriteOldestButKeepChronology()
        {
            // Ensure overwrite path is exercised and ordering in GetSnapshot remains oldest->newest
            var buffer = new TemporalCircularBuffer<int>(3);

            buffer.Add(1);
            Thread.Sleep(2);
            buffer.Add(2);
            Thread.Sleep(2);
            buffer.Add(3);
            Thread.Sleep(2);
            buffer.Add(4); // overwrites "1"

            var snapshot = buffer.GetSnapshot().Select(i => i.Value).ToList();
            Assert.Equal(new[] { 2, 3, 4 }, snapshot);
        }
    }
}