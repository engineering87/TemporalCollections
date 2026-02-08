// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Collections;
using TemporalCollections.Models;

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

        // --- Helpers (local to this test class) ---
        private static void AssertSortedByTimestamp<T>(IReadOnlyList<TemporalItem<T>> items)
        {
            for (int i = 1; i < items.Count; i++)
            {
                Assert.True(items[i - 1].Timestamp.UtcTicks <= items[i].Timestamp.UtcTicks,
                    $"Not sorted by timestamp at {i - 1}->{i}: {items[i - 1].Timestamp:o} > {items[i].Timestamp:o}");
            }
        }

        private static DateTimeOffset Mid(DateTimeOffset a, DateTimeOffset b)
        {
            long m = (a.UtcTicks + b.UtcTicks) / 2;
            return new DateTimeOffset(m, TimeSpan.Zero);
        }

        [Fact(DisplayName = "Empty queue: TryPeek/TryDequeue return false and default/null value")]
        public void EmptyQueue_TryPeek_TryDequeue_ReturnFalse()
        {
            var q = new TemporalPriorityQueue<int, string>();

            Assert.False(q.TryPeek(out var p));
            Assert.Null(p);

            Assert.False(q.TryDequeue(out var d));
            Assert.Null(d);

            Assert.Equal(0, q.Count);
            Assert.Empty(q.GetInRange(DateTime.MinValue, DateTime.MaxValue));
            Assert.Null(q.GetEarliest());
            Assert.Null(q.GetLatest());
            Assert.Equal(TimeSpan.Zero, q.GetTimeSpan());
        }

        [Fact(DisplayName = "Dequeue: stable ordering for same priority (FIFO by insertion timestamp)")]
        public void TryDequeue_SamePriority_IsStableByInsertionTime()
        {
            var q = new TemporalPriorityQueue<int, string>();

            // Same priority for all: should be dequeued by insertion timestamp (stable FIFO).
            q.Enqueue("A", 5);
            q.Enqueue("B", 5);
            q.Enqueue("C", 5);

            Assert.True(q.TryDequeue(out var v1));
            Assert.True(q.TryDequeue(out var v2));
            Assert.True(q.TryDequeue(out var v3));

            Assert.Equal("A", v1);
            Assert.Equal("B", v2);
            Assert.Equal("C", v3);
            Assert.Equal(0, q.Count);
        }

        [Fact(DisplayName = "Time queries are ordered by timestamp (not by priority)")]
        public void GetInRange_IsOrderedByTimestamp_NotByPriority()
        {
            var q = new TemporalPriorityQueue<int, string>();

            // Enqueue with priorities that would reorder if we were sorting by priority:
            // but GetInRange must return ordered by timestamp.
            q.Enqueue("first-lowprio", 100);  // inserted 1st
            q.Enqueue("second-highprio", 1);  // inserted 2nd
            q.Enqueue("third-midprio", 50);   // inserted 3rd

            var all = q.GetInRange(DateTime.MinValue, DateTime.MaxValue).ToList();
            Assert.Equal(3, all.Count);
            AssertSortedByTimestamp(all);

            Assert.Equal("first-lowprio", all[0].Value);
            Assert.Equal("second-highprio", all[1].Value);
            Assert.Equal("third-midprio", all[2].Value);
        }

        [Fact(DisplayName = "RemoveOlderThan is strictly older: cutoff item remains")]
        public void RemoveOlderThan_StrictlyOlder_CutoffRemains()
        {
            var q = new TemporalPriorityQueue<int, string>();

            q.Enqueue("A", 2);
            q.Enqueue("B", 1);
            q.Enqueue("C", 3);

            var all = q.GetInRange(DateTime.MinValue, DateTime.MaxValue).OrderBy(x => x.Timestamp.UtcTicks).ToList();
            Assert.True(all.Count >= 3);

            // cutoff = timestamp of the second item => must remove only the first (strictly older)
            var cutoff = all[1].Timestamp.UtcDateTime;
            q.RemoveOlderThan(cutoff);

            var left = q.GetInRange(DateTime.MinValue, DateTime.MaxValue).OrderBy(x => x.Timestamp.UtcTicks).ToList();
            Assert.Equal(2, left.Count);
            Assert.Equal(all[1].Value, left[0].Value); // cutoff item remains
            Assert.Equal(all[2].Value, left[1].Value);
        }

        [Fact(DisplayName = "RemoveRange is inclusive on boundaries [from,to]")]
        public void RemoveRange_InclusiveBoundaries_RemovesEndpoints()
        {
            var q = new TemporalPriorityQueue<int, string>();

            q.Enqueue("A", 3);
            q.Enqueue("B", 2);
            q.Enqueue("C", 1);

            var all = q.GetInRange(DateTime.MinValue, DateTime.MaxValue).OrderBy(x => x.Timestamp.UtcTicks).ToList();
            Assert.True(all.Count == 3);

            // Remove exactly item B by using same timestamp as start/end
            var tsB = all[1].Timestamp.UtcDateTime;
            q.RemoveRange(tsB, tsB);

            var left = q.GetInRange(DateTime.MinValue, DateTime.MaxValue).Select(x => x.Value).ToList();
            Assert.Equal(2, left.Count);
            Assert.DoesNotContain("B", left);
        }

        [Fact(DisplayName = "Range arguments can be swapped: GetInRange/CountInRange/RemoveRange behave same")]
        public void RangeArguments_Swapped_AreHandled()
        {
            var q1 = new TemporalPriorityQueue<int, string>();
            q1.Enqueue("A", 3);
            q1.Enqueue("B", 2);
            q1.Enqueue("C", 1);

            var all1 = q1.GetInRange(DateTime.MinValue, DateTime.MaxValue).OrderBy(x => x.Timestamp.UtcTicks).ToList();
            var from = all1[0].Timestamp.UtcDateTime;
            var to = all1[2].Timestamp.UtcDateTime;

            var r1 = q1.GetInRange(from, to).Select(x => x.Value).ToArray();
            var r2 = q1.GetInRange(to, from).Select(x => x.Value).ToArray();
            Assert.Equal(r1, r2);

            var c1 = q1.CountInRange(from, to);
            var c2 = q1.CountInRange(to, from);
            Assert.Equal(c1, c2);

            // RemoveRange swapped removes same items
            var q2 = new TemporalPriorityQueue<int, string>();
            q2.Enqueue("A", 3);
            q2.Enqueue("B", 2);
            q2.Enqueue("C", 1);

            var all2 = q2.GetInRange(DateTime.MinValue, DateTime.MaxValue).OrderBy(x => x.Timestamp.UtcTicks).ToList();
            var midTs = all2[1].Timestamp.UtcDateTime;

            // Remove only middle item but with swapped args around same point (still inclusive)
            q2.RemoveRange(midTs, midTs);
            var left = q2.GetInRange(DateTime.MinValue, DateTime.MaxValue).Select(x => x.Value).ToList();
            Assert.Equal(2, left.Count);
            Assert.DoesNotContain(all2[1].Value, left);
        }

        [Fact(DisplayName = "DateTime overloads: Unspecified is treated as UTC (AssumeUtc policy from base)")]
        public void DateTimeOverloads_Unspecified_AssumeUtc()
        {
            var q = new TemporalPriorityQueue<int, string>();
            q.Enqueue("A", 3);
            q.Enqueue("B", 2);
            q.Enqueue("C", 1);

            var all = q.GetInRange(DateTime.MinValue, DateTime.MaxValue)
                       .OrderBy(x => x.Timestamp.UtcTicks)
                       .ToList();

            // Build DateTime range with Kind=Unspecified but same clock values as UTC
            var fromUnspec = DateTime.SpecifyKind(all[0].Timestamp.UtcDateTime, DateTimeKind.Unspecified);
            var toUnspec = DateTime.SpecifyKind(all[2].Timestamp.UtcDateTime, DateTimeKind.Unspecified);

            var res = q.GetInRange(fromUnspec, toUnspec).ToList();
            Assert.Equal(3, res.Count);
        }

        [Fact(DisplayName = "GetNearest: tie prefers later item (>= time) as documented")]
        public void GetNearest_TiePrefersLater_WhenExactMidpointExists()
        {
            // Wide range for retrieving all items
            DateTime wideFrom = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime wideTo = new(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            var q = new TemporalPriorityQueue<int, string>();
            q.Enqueue("A", 10);
            q.Enqueue("B", 10);
            q.Enqueue("C", 10);

            var all = q.GetInRange(wideFrom, wideTo).OrderBy(x => x.Timestamp.UtcTicks).ToList();
            Assert.True(all.Count >= 3);

            var b = all[1];
            var c = all[2];

            long dt = c.Timestamp.UtcTicks - b.Timestamp.UtcTicks;
            Assert.True(dt > 0);

            if ((dt & 1L) == 0L)
            {
                // Exact midpoint exists -> tie case
                var mid = Mid(b.Timestamp, c.Timestamp).UtcDateTime;

                var nearest = q.GetNearest(mid);
                Assert.NotNull(nearest);
                Assert.Equal("C", nearest!.Value); // later preferred on tie
            }
            else
            {
                // No exact midpoint; test both sides: one tick closer to B, next tick closer to C
                long midFloorTicks = b.Timestamp.UtcTicks + (dt / 2);
                long midCeilTicks = midFloorTicks + 1;

                var nearB = q.GetNearest(new DateTimeOffset(midFloorTicks, TimeSpan.Zero).UtcDateTime);
                var nearC = q.GetNearest(new DateTimeOffset(midCeilTicks, TimeSpan.Zero).UtcDateTime);

                Assert.NotNull(nearB);
                Assert.NotNull(nearC);
                Assert.Equal("B", nearB!.Value);
                Assert.Equal("C", nearC!.Value);
            }
        }

        [Fact(DisplayName = "Concurrency: timestamps remain strictly increasing in time queries")]
        public void Concurrency_Enqueue_ProducesMonotonicTimestamps_InQueries()
        {
            var q = new TemporalPriorityQueue<int, int>();

            Parallel.For(0, 2000, i =>
            {
                q.Enqueue(i, i % 7);
            });

            Assert.Equal(2000, q.Count);

            var all = q.GetInRange(DateTime.MinValue, DateTime.MaxValue).ToList();
            Assert.Equal(2000, all.Count);
            AssertSortedByTimestamp(all);

            // Extra safety: no duplicates dropped (SortedSet strict ordering)
            var seen = new HashSet<int>(all.Select(x => x.Value));
            Assert.Equal(2000, seen.Count);
        }
    }
}