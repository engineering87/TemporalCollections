// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Collections;

namespace TemporalCollections.Tests.Collections
{
    public class TemporalPriorityQueueTests
    {
        [Fact]
        public void Enqueue_IncreasesCount()
        {
            var queue = new TemporalPriorityQueue<int, string>();
            Assert.Equal(0, queue.Count);

            queue.Enqueue("a", 5);
            queue.Enqueue("b", 3);

            Assert.Equal(2, queue.Count);
        }

        [Fact]
        public void TryPeek_ReturnsHighestPriorityWithoutRemoving()
        {
            var queue = new TemporalPriorityQueue<int, string>();
            queue.Enqueue("low", 10);
            queue.Enqueue("high", 1);

            bool result = queue.TryPeek(out var val);
            Assert.True(result);
            Assert.Equal("high", val);
            Assert.Equal(2, queue.Count); // still 2, not removed
        }

        [Fact]
        public void TryDequeue_ReturnsItemsInPriorityOrder()
        {
            var queue = new TemporalPriorityQueue<int, string>();
            queue.Enqueue("item1", 5);
            Thread.Sleep(1);
            queue.Enqueue("item2", 3);
            Thread.Sleep(1);
            queue.Enqueue("item3", 3);

            bool result1 = queue.TryDequeue(out var val1);
            bool result2 = queue.TryDequeue(out var val2);
            bool result3 = queue.TryDequeue(out var val3);
            bool result4 = queue.TryDequeue(out var val4);

            Assert.True(result1);
            Assert.True(result2);
            Assert.True(result3);
            Assert.False(result4);

            Assert.Equal("item2", val1); // priority 3, earlier timestamp
            Assert.Equal("item3", val2); // priority 3, later timestamp
            Assert.Equal("item1", val3); // priority 5
            Assert.Null(val4);
            Assert.Equal(0, queue.Count);
        }

        [Fact]
        public void GetInRange_ReturnsOnlyItemsInRange()
        {
            var queue = new TemporalPriorityQueue<int, string>();

            queue.Enqueue("old", 1);
            Thread.Sleep(20);
            var from = DateTime.UtcNow;
            queue.Enqueue("inrange", 2);
            Thread.Sleep(20);
            var to = DateTime.UtcNow;
            queue.Enqueue("new", 3);

            var items = queue.GetInRange(from, to).Select(i => i.Value).ToList();

            Assert.Contains("inrange", items);
            Assert.DoesNotContain("old", items);
            Assert.DoesNotContain("new", items);
        }

        [Fact]
        public void RemoveOlderThan_RemovesCorrectItems()
        {
            var queue = new TemporalPriorityQueue<int, string>();

            queue.Enqueue("first", 1);
            Thread.Sleep(10);
            var cutoff = DateTime.UtcNow;
            Thread.Sleep(10);
            queue.Enqueue("second", 2);
            queue.Enqueue("third", 3);

            queue.RemoveOlderThan(cutoff);

            bool peekResult = queue.TryPeek(out var val);

            Assert.True(peekResult);
            Assert.NotEqual("first", val);
            Assert.Equal(2, queue.Count);
        }

        [Fact]
        public void GetTimeSpan_ShouldBeDifferenceBetweenEarliestAndLatest()
        {
            var queue = new TemporalPriorityQueue<int, string>();

            queue.Enqueue("first", 5);
            Thread.Sleep(5);
            queue.Enqueue("second", 1);

            // Order by timestamp to compute expected span deterministically
            var all = queue.GetInRange(DateTime.MinValue, DateTime.MaxValue)
                           .OrderBy(i => i.Timestamp)
                           .ToList();

            Assert.True(all.Count >= 2, "Need at least two items for a non-zero span.");
            var expected = all[^1].Timestamp - all[0].Timestamp;

            Assert.Equal(expected, queue.GetTimeSpan());
        }

        [Fact]
        public void CountInRange_ShouldMatchGetInRangeCount()
        {
            var queue = new TemporalPriorityQueue<int, string>();

            queue.Enqueue("A", 3);
            Thread.Sleep(5);
            var split = DateTime.UtcNow;
            Thread.Sleep(5);
            queue.Enqueue("B", 2);
            Thread.Sleep(5);
            queue.Enqueue("C", 1);

            var from = split;
            var to = DateTime.UtcNow.AddMinutes(1);

            var expected = queue.GetInRange(from, to).Count();
            var counted = queue.CountInRange(from, to);

            Assert.Equal(expected, counted);
        }

        [Fact]
        public void GetBefore_ShouldReturnItemsStrictlyBeforeTime()
        {
            var queue = new TemporalPriorityQueue<int, string>();

            queue.Enqueue("old", 10);
            Thread.Sleep(5);
            var split = DateTime.UtcNow;
            Thread.Sleep(5);
            queue.Enqueue("new", 1);

            var before = queue.GetBefore(split).Select(x => x.Value).ToList();

            Assert.Contains("old", before);
            Assert.DoesNotContain("new", before);
        }

        [Fact]
        public void GetAfter_ShouldReturnItemsStrictlyAfterTime()
        {
            var queue = new TemporalPriorityQueue<int, string>();

            queue.Enqueue("old", 10);
            Thread.Sleep(5);
            var split = DateTime.UtcNow;
            Thread.Sleep(5);
            queue.Enqueue("new", 1);

            var after = queue.GetAfter(split).Select(x => x.Value).ToList();

            Assert.Contains("new", after);
            Assert.DoesNotContain("old", after);
        }

        [Fact]
        public void GetEarliest_And_GetLatest_ShouldReflectChronology()
        {
            var queue = new TemporalPriorityQueue<int, string>();

            queue.Enqueue("first", 5);
            Thread.Sleep(5);
            queue.Enqueue("middle", 4);
            Thread.Sleep(5);
            queue.Enqueue("last", 3);

            var earliest = queue.GetEarliest();
            var latest = queue.GetLatest();

            Assert.NotNull(earliest);
            Assert.NotNull(latest);

            // Earliest/latest are based on timestamp, not priority
            Assert.Equal("first", earliest!.Value);
            Assert.Equal("last", latest!.Value);

            // Sanity check with full ordered list
            var all = queue.GetInRange(DateTime.MinValue, DateTime.MaxValue)
                           .OrderBy(i => i.Timestamp)
                           .ToList();
            Assert.Equal(all.First().Timestamp, earliest.Timestamp);
            Assert.Equal(all.Last().Timestamp, latest.Timestamp);
        }

        [Fact]
        public void RemoveRange_ShouldDeleteItemsWithinInclusiveBounds()
        {
            var queue = new TemporalPriorityQueue<int, string>();

            queue.Enqueue("A", 5);                // before range
            Thread.Sleep(5);
            var tStart = DateTime.UtcNow;
            Thread.Sleep(5);
            queue.Enqueue("B", 3);                // in range
            Thread.Sleep(5);
            queue.Enqueue("C", 2);                // in range
            Thread.Sleep(5);
            var tEnd = DateTime.UtcNow;
            Thread.Sleep(5);
            queue.Enqueue("D", 1);                // after range

            // Remove B and C (timestamps between tStart and tEnd inclusive)
            queue.RemoveRange(tStart, tEnd);

            var remaining = queue.GetInRange(DateTime.MinValue, DateTime.MaxValue)
                                 .Select(i => i.Value)
                                 .ToList();

            Assert.Contains("A", remaining);
            Assert.Contains("D", remaining);
            Assert.DoesNotContain("B", remaining);
            Assert.DoesNotContain("C", remaining);
        }

        [Fact]
        public void Clear_ShouldEmptyQueueAndResetQueryableState()
        {
            var queue = new TemporalPriorityQueue<int, string>();

            queue.Enqueue("x", 2);
            queue.Enqueue("y", 1);

            queue.Clear();

            Assert.Equal(0, queue.Count);
            Assert.Empty(queue.GetInRange(DateTime.MinValue, DateTime.MaxValue));
            Assert.Equal(TimeSpan.Zero, queue.GetTimeSpan());
            Assert.Null(queue.GetEarliest());
            Assert.Null(queue.GetLatest());
        }

        [Fact]
        public void Enqueue_ShouldBeThreadSafe_WhenManyParallelEnqueues()
        {
            var queue = new TemporalPriorityQueue<int, int>();

            // Enqueue concurrently with randomish priorities
            Parallel.For(0, 1000, i =>
            {
                int priority = i % 10; // smaller is higher priority
                queue.Enqueue(i, priority);
            });

            Assert.Equal(1000, queue.Count);

            // Dequeue everything to ensure consistency and no item loss
            var seen = new HashSet<int>();
            while (queue.TryDequeue(out var val))
                seen.Add(val);

            Assert.Equal(1000, seen.Count);
            Assert.Contains(0, seen);
            Assert.Contains(999, seen);
        }

        [Fact]
        public void CountSince_ShouldBeInclusive_AndConsistentWithGetInRange()
        {
            var queue = new TemporalPriorityQueue<int, string>();

            queue.Enqueue("A", 5);
            Thread.Sleep(5);
            queue.Enqueue("B", 3);
            Thread.Sleep(5);
            queue.Enqueue("C", 1);

            var all = queue.GetInRange(DateTime.MinValue, DateTime.MaxValue)
                           .OrderBy(i => i.Timestamp)
                           .ToList();
            Assert.True(all.Count >= 2, "Need at least two items for a valid cutoff.");

            // Inclusive cutoff at the 2nd item's timestamp → expect items at index 1 and onward
            var cutoff = all[1].Timestamp.UtcDateTime;

            var countSince = queue.CountSince(cutoff);
            Assert.Equal(all.Count - 1, countSince);

            // Cross-check with GetInRange(cutoff, now)
            var cross = queue.GetInRange(cutoff, DateTime.UtcNow).Count();
            Assert.Equal(cross, countSince);
        }

        [Fact]
        public void TemporalPriorityQueue_GetNearest_WorksAndTiesPreferLater()
        {
            DateTime WideFrom = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime WideTo = new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            var pq = new TemporalPriorityQueue<int, string>();
            pq.Enqueue("A", 2);
            pq.Enqueue("B", 1);
            pq.Enqueue("C", 3);

            var all = pq.GetInRange(WideFrom, WideTo).ToList();
            Assert.True(all.Count >= 3);

            var b = all[1];
            var c = all[2];

            // Exact lookup on B: should always return B
            var exact = pq.GetNearest(b.Timestamp.UtcDateTime);
            Assert.NotNull(exact);
            Assert.Equal("B", exact!.Value);

            // Compute the delta in ticks between B and C
            long dt = c.Timestamp.UtcTicks - b.Timestamp.UtcTicks;
            Assert.True(dt > 0, "Timestamps should be strictly increasing");

            if ((dt & 1L) == 0L)
            {
                // Even delta -> there is a true midpoint between B and C
                // In this tie case, GetNearest should prefer the later item (C).
                long midTicks = b.Timestamp.UtcTicks + (dt / 2);
                var mid = new DateTimeOffset(midTicks, TimeSpan.Zero).UtcDateTime;

                var tie = pq.GetNearest(mid);
                Assert.NotNull(tie);
                Assert.Equal("C", tie!.Value);
            }
            else
            {
                // Odd delta -> no exact midpoint (one tick closer to B, the next tick closer to C).
                // Verify that GetNearest resolves correctly on both sides:
                long midFloorTicks = b.Timestamp.UtcTicks + (dt / 2); // closer to B
                long midCeilTicks = midFloorTicks + 1;               // closer to C

                var midFloor = new DateTimeOffset(midFloorTicks, TimeSpan.Zero).UtcDateTime;
                var midCeil = new DateTimeOffset(midCeilTicks, TimeSpan.Zero).UtcDateTime;

                var nearFloor = pq.GetNearest(midFloor);
                var nearCeil = pq.GetNearest(midCeil);

                Assert.NotNull(nearFloor);
                Assert.NotNull(nearCeil);

                Assert.Equal("B", nearFloor!.Value);
                Assert.Equal("C", nearCeil!.Value);
            }
        }
    }
}