// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Collections;

namespace TemporalCollections.Tests.Collections
{
    public class TemporalQueueTests
    {
        [Fact]
        public void Enqueue_IncreasesCount()
        {
            var queue = new TemporalQueue<int>();
            Assert.Equal(0, queue.Count);

            queue.Enqueue(42);
            Assert.Equal(1, queue.Count);
        }

        [Fact]
        public void Dequeue_ReturnsItemsInOrder()
        {
            var queue = new TemporalQueue<string>();
            queue.Enqueue("first");
            queue.Enqueue("second");

            var firstItem = queue.Dequeue();
            var secondItem = queue.Dequeue();

            Assert.Equal("first", firstItem.Value);
            Assert.Equal("second", secondItem.Value);
            Assert.True(firstItem.Timestamp <= secondItem.Timestamp);
            Assert.Equal(0, queue.Count);
        }

        [Fact]
        public void Peek_ReturnsOldestItemWithoutRemoving()
        {
            var queue = new TemporalQueue<int>();
            queue.Enqueue(1);
            queue.Enqueue(2);

            var peeked = queue.Peek();
            Assert.Equal(1, peeked.Value);
            Assert.Equal(2, queue.Count);
        }

        [Fact]
        public void Dequeue_Throws_WhenEmpty()
        {
            var queue = new TemporalQueue<int>();
            Assert.Throws<InvalidOperationException>(() => queue.Dequeue());
        }

        [Fact]
        public void Peek_Throws_WhenEmpty()
        {
            var queue = new TemporalQueue<int>();
            Assert.Throws<InvalidOperationException>(() => queue.Peek());
        }

        [Fact]
        public void GetInRange_ReturnsCorrectItems()
        {
            var queue = new TemporalQueue<int>();
            var now = DateTime.UtcNow;

            queue.Enqueue(1);
            Thread.Sleep(10);
            queue.Enqueue(2);
            Thread.Sleep(10);
            queue.Enqueue(3);

            var from = now.AddMilliseconds(5);
            var to = DateTime.UtcNow;

            var itemsInRange = queue.GetInRange(from, to).ToList();

            Assert.All(itemsInRange, item => Assert.InRange(item.Timestamp, from, to));
            Assert.Contains(itemsInRange, i => i.Value == 2);
            Assert.Contains(itemsInRange, i => i.Value == 3);
            Assert.DoesNotContain(itemsInRange, i => i.Value == 1);
        }

        [Fact]
        public void RemoveOlderThan_RemovesCorrectItems()
        {
            var queue = new TemporalQueue<int>();
            queue.Enqueue(1);
            Thread.Sleep(10);
            var cutoff = DateTime.UtcNow;
            Thread.Sleep(10);
            queue.Enqueue(2);
            queue.Enqueue(3);

            queue.RemoveOlderThan(cutoff);

            Assert.DoesNotContain(queue.GetInRange(DateTime.MinValue, DateTime.MaxValue), i => i.Value == 1);
            Assert.Contains(queue.GetInRange(DateTime.MinValue, DateTime.MaxValue), i => i.Value == 2);
            Assert.Contains(queue.GetInRange(DateTime.MinValue, DateTime.MaxValue), i => i.Value == 3);
        }

        [Fact]
        public void GetTimeSpan_ShouldBeDifferenceBetweenEarliestAndLatest()
        {
            var queue = new TemporalQueue<int>();

            queue.Enqueue(1);
            Thread.Sleep(5);
            queue.Enqueue(2);

            var all = queue.GetInRange(DateTime.MinValue, DateTime.MaxValue).OrderBy(i => i.Timestamp).ToList();
            Assert.True(all.Count >= 2, "Need at least two items for a non-zero span.");

            var expected = all[^1].Timestamp - all[0].Timestamp;
            Assert.Equal(expected, queue.GetTimeSpan());
        }

        [Fact]
        public void CountInRange_ShouldReturnCorrectCount()
        {
            var queue = new TemporalQueue<int>();

            queue.Enqueue(1);
            Thread.Sleep(5);
            var split = DateTime.UtcNow;
            Thread.Sleep(5);
            queue.Enqueue(2);
            Thread.Sleep(5);
            queue.Enqueue(3);

            var from = split;
            var to = DateTime.UtcNow.AddMinutes(1);

            var expected = queue.GetInRange(from, to).Count();
            var counted = queue.CountInRange(from, to);

            Assert.Equal(expected, counted);
        }

        [Fact]
        public void GetBefore_ShouldReturnItemsStrictlyBeforeTime()
        {
            var queue = new TemporalQueue<string>();

            queue.Enqueue("A");
            Thread.Sleep(5);
            var split = DateTime.UtcNow;
            Thread.Sleep(5);
            queue.Enqueue("B");

            var before = queue.GetBefore(split).Select(x => x.Value).ToList();

            Assert.Contains("A", before);
            Assert.DoesNotContain("B", before);
        }

        [Fact]
        public void GetAfter_ShouldReturnItemsStrictlyAfterTime()
        {
            var queue = new TemporalQueue<string>();

            queue.Enqueue("A");
            Thread.Sleep(5);
            var split = DateTime.UtcNow;
            Thread.Sleep(5);
            queue.Enqueue("B");

            var after = queue.GetAfter(split).Select(x => x.Value).ToList();

            Assert.Contains("B", after);
            Assert.DoesNotContain("A", after);
        }

        [Fact]
        public void GetEarliest_And_GetLatest_ShouldReturnFirstAndLast()
        {
            var queue = new TemporalQueue<string>();

            queue.Enqueue("first");
            Thread.Sleep(5);
            queue.Enqueue("middle");
            Thread.Sleep(5);
            queue.Enqueue("last");

            var earliest = queue.GetEarliest();
            var latest = queue.GetLatest();

            Assert.NotNull(earliest);
            Assert.NotNull(latest);

            Assert.Equal("first", earliest!.Value);
            Assert.Equal("last", latest!.Value);

            var all = queue.GetInRange(DateTime.MinValue, DateTime.MaxValue).OrderBy(i => i.Timestamp).ToList();
            Assert.Equal(all.First().Timestamp, earliest.Timestamp);
            Assert.Equal(all.Last().Timestamp, latest.Timestamp);
        }

        [Fact]
        public void RemoveRange_ShouldDeleteItemsWithinInclusiveBounds()
        {
            var queue = new TemporalQueue<int>();

            queue.Enqueue(1);                // A
            Thread.Sleep(5);
            var tRangeStart = DateTime.UtcNow;
            Thread.Sleep(5);
            queue.Enqueue(2);                // B
            Thread.Sleep(5);
            queue.Enqueue(3);                // C
            Thread.Sleep(5);
            var tRangeEnd = DateTime.UtcNow;
            Thread.Sleep(5);
            queue.Enqueue(4);                // D

            queue.RemoveRange(tRangeStart, tRangeEnd);

            // Sanity check with full ordered list
            var remaining = queue.GetInRange(DateTime.MinValue, DateTime.MaxValue).Select(i => i.Value).ToList();

            Assert.Contains(1, remaining);
            Assert.Contains(4, remaining);
            Assert.DoesNotContain(2, remaining);
            Assert.DoesNotContain(3, remaining);
        }

        [Fact]
        public void Clear_ShouldEmptyQueueAndResetQueryableState()
        {
            var queue = new TemporalQueue<int>();

            queue.Enqueue(10);
            queue.Enqueue(20);

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
            var queue = new TemporalQueue<int>();

            Parallel.For(0, 1000, i => queue.Enqueue(i));

            Assert.Equal(1000, queue.Count);

            // Dequeue all to ensure queue integrity
            var seen = new HashSet<int>();
            while (queue.Count > 0)
            {
                var item = queue.Dequeue();
                seen.Add(item.Value);
            }

            Assert.Equal(1000, seen.Count);
            Assert.Contains(0, seen);
            Assert.Contains(999, seen);
        }

        [Fact]
        public void GetInRange_ShouldBeInclusive_OnBothBounds()
        {
            var q = new TemporalQueue<int>();

            q.Enqueue(1);
            Thread.Sleep(2);
            var t1 = DateTime.UtcNow;
            Thread.Sleep(2);
            q.Enqueue(2);
            Thread.Sleep(2);
            var t2 = DateTime.UtcNow;
            Thread.Sleep(2);
            q.Enqueue(3);

            // Inclusive: items exactly at t1 or t2 must be included
            var results = q.GetInRange(t1, t2).Select(x => x.Value).ToList();

            Assert.Contains(2, results); // inside the window
            Assert.DoesNotContain(1, results); // before window
            Assert.DoesNotContain(3, results); // after window
        }

        [Fact]
        public void GetBefore_And_GetAfter_ShouldBeStrict()
        {
            var q = new TemporalQueue<string>();

            q.Enqueue("A");
            Thread.Sleep(2);
            var split = DateTime.UtcNow;
            Thread.Sleep(2);
            q.Enqueue("B");

            var before = q.GetBefore(split).Select(x => x.Value).ToList();
            var after = q.GetAfter(split).Select(x => x.Value).ToList();

            // Strict semantics: elements exactly at 'split' must be excluded
            Assert.Contains("A", before);
            Assert.DoesNotContain("B", before);

            Assert.Contains("B", after);
            Assert.DoesNotContain("A", after);
        }

        [Fact]
        public void RemoveOlderThan_ShouldNotRemove_ItemsEqualToCutoff()
        {
            var q = new TemporalQueue<int>();

            q.Enqueue(1);
            Thread.Sleep(2);
            var cutoff = DateTime.UtcNow;
            Thread.Sleep(2);
            q.Enqueue(2);

            // Remove strictly older than cutoff
            q.RemoveOlderThan(cutoff);

            var vals = q.GetInRange(DateTime.MinValue, DateTime.MaxValue).Select(i => i.Value).ToList();
            Assert.DoesNotContain(1, vals); // likely older
            Assert.Contains(2, vals);       // >= cutoff should remain
        }

        [Fact]
        public void RemoveRange_ShouldBeInclusive_OnBothBounds()
        {
            var q = new TemporalQueue<int>();

            q.Enqueue(1);
            Thread.Sleep(2);
            var start = DateTime.UtcNow;
            Thread.Sleep(2);
            q.Enqueue(2);
            Thread.Sleep(2);
            var end = DateTime.UtcNow;
            Thread.Sleep(2);
            q.Enqueue(3);

            // Remove [start, end] inclusive -> should remove only "2"
            q.RemoveRange(start, end);

            var remaining = q.GetInRange(DateTime.MinValue, DateTime.MaxValue).Select(x => x.Value).ToList();
            Assert.Contains(1, remaining);
            Assert.Contains(3, remaining);
            Assert.DoesNotContain(2, remaining);
        }

        [Fact]
        public void Timestamps_ShouldBeMonotonic_EvenOnBurst()
        {
            var q = new TemporalQueue<int>();

            for (int i = 0; i < 200; i++) q.Enqueue(i);

            var items = q.GetInRange(DateTime.MinValue, DateTime.MaxValue)
                         .OrderBy(i => i.Timestamp)
                         .ToList();

            for (int i = 1; i < items.Count; i++)
            {
                Assert.True(items[i - 1].Timestamp < items[i].Timestamp,
                    $"Timestamps not strictly increasing at index {i}");
            }
        }

        [Fact]
        public void Count_ShouldMatch_GetInRange_AllTimeSnapshot()
        {
            var q = new TemporalQueue<int>();
            for (int i = 0; i < 25; i++) q.Enqueue(i);

            var all = q.GetInRange(DateTime.MinValue, DateTime.MaxValue).Count();
            Assert.Equal(q.Count, all);
        }

        [Fact]
        public void Range_With_Unspecified_ShouldBehaveLikeUtc_WhenAssumeUtc()
        {
            var q = new TemporalQueue<int>();

            q.Enqueue(10);
            Thread.Sleep(2);
            var midUtc = DateTime.UtcNow;
            Thread.Sleep(2);
            q.Enqueue(20);

            // Same instant but Unspecified kind
            var midUnspec = DateTime.SpecifyKind(midUtc, DateTimeKind.Unspecified);

            var withUtc = q.GetInRange(midUtc, DateTime.UtcNow).Select(x => x.Value).ToList();
            var withUnspec = q.GetInRange(midUnspec, DateTime.UtcNow).Select(x => x.Value).ToList();

            // With internal AssumeUtc policy, results should match
            Assert.Equal(withUtc, withUnspec);
        }

        [Fact]
        public void Clear_AfterMutations_ShouldLeaveQueueEmpty()
        {
            var q = new TemporalQueue<int>();
            for (int i = 0; i < 5; i++) q.Enqueue(i);

            q.RemoveOlderThan(DateTime.UtcNow.AddMinutes(1)); // probably removes all
            q.Clear(); // idempotent

            Assert.Equal(0, q.Count);
            Assert.Empty(q.GetInRange(DateTime.MinValue, DateTime.MaxValue));
            Assert.Empty(q.GetBefore(DateTime.UtcNow));
            Assert.Empty(q.GetAfter(DateTime.UtcNow));
            Assert.Null(q.GetEarliest());
            Assert.Null(q.GetLatest());
        }

        [Fact]
        public void Dequeue_After_Clear_ShouldThrow()
        {
            var q = new TemporalQueue<string>();
            q.Enqueue("x");
            q.Clear();
            Assert.Throws<InvalidOperationException>(() => q.Dequeue());
        }

        [Fact]
        public void RemoveRange_OnEmptyOrNoOverlap_ShouldBeNoOp()
        {
            var q = new TemporalQueue<int>();

            // No items → no-op
            q.RemoveRange(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
            Assert.Equal(0, q.Count);

            // Add items then remove a non-overlapping future range → no-op
            q.Enqueue(1);
            q.Enqueue(2);
            var futureFrom = DateTime.UtcNow.AddHours(1);
            var futureTo = futureFrom.AddMinutes(1);
            q.RemoveRange(futureFrom, futureTo);

            var vals = q.GetInRange(DateTime.MinValue, DateTime.MaxValue).Select(i => i.Value).ToList();
            Assert.Equal(new[] { 1, 2 }, vals.OrderBy(v => v));
        }

        [Fact]
        public void Peek_DoesNotChange_OrderingOrCount()
        {
            var q = new TemporalQueue<int>();
            q.Enqueue(1);
            q.Enqueue(2);

            var beforeCount = q.Count;
            var head = q.Peek();

            Assert.Equal(1, head.Value);
            Assert.Equal(beforeCount, q.Count);

            var first = q.Dequeue();
            Assert.Equal(1, first.Value);
        }

        [Fact]
        public void GetInRange_ShouldReturn_ChronologicalOrder()
        {
            var q = new TemporalQueue<int>();
            q.Enqueue(1);
            Thread.Sleep(2);
            q.Enqueue(2);
            Thread.Sleep(2);
            q.Enqueue(3);

            var all = q.GetInRange(DateTime.MinValue, DateTime.MaxValue).ToList();

            // Ensure timestamps are non-decreasing across the returned snapshot
            for (int i = 1; i < all.Count; i++)
            {
                Assert.True(all[i - 1].Timestamp <= all[i].Timestamp,
                    $"Out-of-order items at index {i}");
            }
        }

        [Fact]
        public void CountSince_ShouldBeInclusive_AndConsistentWithGetInRange()
        {
            var q = new TemporalQueue<int>();

            q.Enqueue(1);
            Thread.Sleep(5);
            q.Enqueue(2);
            Thread.Sleep(5);
            q.Enqueue(3);

            var all = q.GetInRange(DateTime.MinValue, DateTime.MaxValue)
                       .OrderBy(i => i.Timestamp)
                       .ToList();
            Assert.True(all.Count >= 2, "Need at least two items for a valid cutoff.");

            // Use the timestamp of the 2nd item as inclusive cutoff
            var cutoff = all[1].Timestamp.UtcDateTime;

            var countSince = q.CountSince(cutoff);
            Assert.Equal(all.Count - 1, countSince); // items at index 1 and onward

            // Cross-check with GetInRange(cutoff, now)
            var cross = q.GetInRange(cutoff, DateTime.UtcNow).Count();
            Assert.Equal(cross, countSince);
        }

        [Fact]
        public void TemporalQueue_GetNearest_WorksAndTiesPreferLater()
        {
            var q = new TemporalQueue<string>();
            q.Enqueue("A");
            q.Enqueue("B");
            q.Enqueue("C");

            var all = q.GetInRange(
                new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            ).ToList(); // oldest -> newest

            Assert.True(all.Count >= 3);

            var b = all[1]; // "B"
            var c = all[2]; // "C"

            // Exact sul timestamp di B
            var exact = q.GetNearest(b.Timestamp.UtcDateTime);
            Assert.NotNull(exact);
            Assert.Equal("B", exact!.Value);

            long dt = c.Timestamp.UtcTicks - b.Timestamp.UtcTicks;
            Assert.True(dt > 0, "Timestamps should be strictly increasing");

            if ((dt & 1L) == 0L)
            {
                long midTicks = b.Timestamp.UtcTicks + (dt / 2);
                var mid = new DateTimeOffset(midTicks, TimeSpan.Zero).UtcDateTime;

                var tie = q.GetNearest(mid);
                Assert.NotNull(tie);
                Assert.Equal("C", tie!.Value);
            }
            else
            {
                long midFloorTicks = b.Timestamp.UtcTicks + (dt / 2);
                long midCeilTicks = midFloorTicks + 1;

                var midFloor = new DateTimeOffset(midFloorTicks, TimeSpan.Zero).UtcDateTime;
                var midCeil = new DateTimeOffset(midCeilTicks, TimeSpan.Zero).UtcDateTime;

                var nearFloor = q.GetNearest(midFloor);
                var nearCeil = q.GetNearest(midCeil);

                Assert.NotNull(nearFloor);
                Assert.NotNull(nearCeil);

                Assert.Equal("B", nearFloor!.Value);
                Assert.Equal("C", nearCeil!.Value);
            }
        }
    }
}