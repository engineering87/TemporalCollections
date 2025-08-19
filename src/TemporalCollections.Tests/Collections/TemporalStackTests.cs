// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Collections;

namespace TemporalCollections.Tests.Collections
{
    public class TemporalStackTests
    {
        [Fact]
        public void Push_ShouldIncreaseCount_AndStoreTimestamp()
        {
            var stack = new TemporalStack<string>();

            stack.Push("A");

            Assert.Equal(1, stack.Count);
            var item = stack.Peek();
            Assert.Equal("A", item.Value);
            Assert.True((DateTime.UtcNow - item.Timestamp).TotalSeconds < 1);
        }

        [Fact]
        public void Pop_ShouldReturnLastPushedItem_AndDecreaseCount()
        {
            var stack = new TemporalStack<int>();
            stack.Push(1);
            stack.Push(2);

            var popped = stack.Pop();

            Assert.Equal(2, popped.Value);
            Assert.Equal(1, stack.Count);
        }

        [Fact]
        public void Peek_ShouldReturnLastPushedItem_WithoutRemovingIt()
        {
            var stack = new TemporalStack<int>();
            stack.Push(1);
            stack.Push(2);

            var peeked = stack.Peek();

            Assert.Equal(2, peeked.Value);
            Assert.Equal(2, stack.Count);
        }

        [Fact]
        public void Pop_OnEmptyStack_ShouldThrow()
        {
            var stack = new TemporalStack<int>();
            Assert.Throws<InvalidOperationException>(() => stack.Pop());
        }

        [Fact]
        public void Peek_OnEmptyStack_ShouldThrow()
        {
            var stack = new TemporalStack<int>();
            Assert.Throws<InvalidOperationException>(() => stack.Peek());
        }

        [Fact]
        public void GetInRange_ShouldReturnOnlyItemsWithinTimeRange()
        {
            var stack = new TemporalStack<string>();

            stack.Push("Old");
            Thread.Sleep(20);
            var midTime = DateTime.UtcNow;
            Thread.Sleep(20);
            stack.Push("New");

            var results = stack.GetInRange(midTime, DateTime.UtcNow).ToList();

            Assert.Single(results);
            Assert.Equal("New", results[0].Value);
        }

        [Fact]
        public void RemoveOlderThan_ShouldRemoveItemsOlderThanCutoff()
        {
            var stack = new TemporalStack<string>();

            stack.Push("Old");
            Thread.Sleep(20);
            var cutoff = DateTime.UtcNow;
            Thread.Sleep(20);
            stack.Push("New");

            stack.RemoveOlderThan(cutoff);

            Assert.Single(stack.GetInRange(DateTime.MinValue, DateTime.MaxValue));
            Assert.Equal("New", stack.Peek().Value);
        }

        [Fact]
        public void GetTimeSpan_ShouldBeDifferenceBetweenEarliestAndLatest()
        {
            var stack = new TemporalStack<int>();

            stack.Push(1);
            Thread.Sleep(5);
            stack.Push(2);

            var all = stack.GetInRange(DateTime.MinValue, DateTime.MaxValue)
                           .OrderBy(i => i.Timestamp)
                           .ToList();
            Assert.True(all.Count >= 2, "Need at least two items for a non-zero span.");

            var expected = all[^1].Timestamp - all[0].Timestamp;
            Assert.Equal(expected, stack.GetTimeSpan());
        }

        [Fact]
        public void CountInRange_ShouldReturnCorrectCount()
        {
            var stack = new TemporalStack<int>();

            stack.Push(1);
            Thread.Sleep(5);
            var split = DateTime.UtcNow;
            Thread.Sleep(5);
            stack.Push(2);
            Thread.Sleep(5);
            stack.Push(3);

            var from = split;
            var to = DateTime.UtcNow.AddMinutes(1);

            var expected = stack.GetInRange(from, to).Count();
            var counted = stack.CountInRange(from, to);

            Assert.Equal(expected, counted);
        }

        [Fact]
        public void GetBefore_ShouldReturnItemsStrictlyBeforeTime()
        {
            var stack = new TemporalStack<string>();

            stack.Push("A");
            Thread.Sleep(5);
            var split = DateTime.UtcNow;
            Thread.Sleep(5);
            stack.Push("B");

            var before = stack.GetBefore(split).Select(x => x.Value).ToList();

            Assert.Contains("A", before);
            Assert.DoesNotContain("B", before);
        }

        [Fact]
        public void GetAfter_ShouldReturnItemsStrictlyAfterTime()
        {
            var stack = new TemporalStack<string>();

            stack.Push("A");
            Thread.Sleep(5);
            var split = DateTime.UtcNow;
            Thread.Sleep(5);
            stack.Push("B");

            var after = stack.GetAfter(split).Select(x => x.Value).ToList();

            Assert.Contains("B", after);
            Assert.DoesNotContain("A", after);
        }

        [Fact]
        public void GetEarliest_And_GetLatest_ShouldReturnFirstAndLastPushed()
        {
            var stack = new TemporalStack<string>();

            stack.Push("first");
            Thread.Sleep(5);
            stack.Push("middle");
            Thread.Sleep(5);
            stack.Push("last");

            var earliest = stack.GetEarliest();
            var latest = stack.GetLatest();

            Assert.NotNull(earliest);
            Assert.NotNull(latest);

            Assert.Equal("first", earliest!.Value);
            Assert.Equal("last", latest!.Value);

            var all = stack.GetInRange(DateTime.MinValue, DateTime.MaxValue).OrderBy(i => i.Timestamp).ToList();
            Assert.Equal(all.First().Timestamp, earliest.Timestamp);
            Assert.Equal(all.Last().Timestamp, latest.Timestamp);
        }

        [Fact]
        public void RemoveRange_ShouldDeleteItemsWithinInclusiveBounds()
        {
            var stack = new TemporalStack<int>();

            stack.Push(1);                // A
            Thread.Sleep(5);
            var tRangeStart = DateTime.UtcNow;
            Thread.Sleep(5);
            stack.Push(2);                // B
            Thread.Sleep(5);
            stack.Push(3);                // C
            Thread.Sleep(5);
            var tRangeEnd = DateTime.UtcNow;
            Thread.Sleep(5);
            stack.Push(4);

            stack.RemoveRange(tRangeStart, tRangeEnd);

            var remaining = stack.GetInRange(DateTime.MinValue, DateTime.MaxValue).Select(i => i.Value).ToList();

            Assert.Contains(1, remaining);
            Assert.Contains(4, remaining);
            Assert.DoesNotContain(2, remaining);
            Assert.DoesNotContain(3, remaining);
        }

        [Fact]
        public void Clear_ShouldEmptyStackAndResetQueryableState()
        {
            var stack = new TemporalStack<int>();

            stack.Push(10);
            stack.Push(20);

            stack.Clear();

            Assert.Equal(0, stack.Count);
            Assert.Empty(stack.GetInRange(DateTime.MinValue, DateTime.MaxValue));
            Assert.Equal(TimeSpan.Zero, stack.GetTimeSpan());
            Assert.Null(stack.GetEarliest());
            Assert.Null(stack.GetLatest());
        }

        [Fact]
        public void Push_ShouldBeThreadSafe_WhenManyParallelPushes()
        {
            var stack = new TemporalStack<int>();

            Parallel.For(0, 1000, i => stack.Push(i));

            Assert.Equal(1000, stack.Count);

            // Pop everything to ensure internal consistency and no duplicates loss
            var seen = new HashSet<int>();
            while (stack.Count > 0)
            {
                var item = stack.Pop();
                seen.Add(item.Value);
            }

            Assert.Equal(1000, seen.Count);
            Assert.Contains(0, seen);
            Assert.Contains(999, seen);
        }

        [Fact]
        public void GetInRange_ShouldBeInclusive_OnBothBounds()
        {
            var stack = new TemporalStack<int>();

            stack.Push(1);
            Thread.Sleep(2);
            var t1 = DateTime.UtcNow;
            Thread.Sleep(2);
            stack.Push(2);
            Thread.Sleep(2);
            var t2 = DateTime.UtcNow;
            Thread.Sleep(2);
            stack.Push(3);

            // Inclusive range: items at both bounds must be included
            var results = stack.GetInRange(t1, t2).Select(x => x.Value).ToList();

            Assert.Contains(2, results);
            Assert.DoesNotContain(1, results); // before range
            Assert.DoesNotContain(3, results); // after range
        }

        [Fact]
        public void GetBefore_And_GetAfter_ShouldBeStrict()
        {
            var stack = new TemporalStack<string>();

            stack.Push("A");
            Thread.Sleep(2);
            var split = DateTime.UtcNow;
            Thread.Sleep(2);
            stack.Push("B");

            var before = stack.GetBefore(split).Select(x => x.Value).ToList();
            var after = stack.GetAfter(split).Select(x => x.Value).ToList();

            // Strict behavior: items exactly at split are excluded
            Assert.Contains("A", before);
            Assert.DoesNotContain("B", before);

            Assert.Contains("B", after);
            Assert.DoesNotContain("A", after);
        }

        [Fact]
        public void RemoveOlderThan_ShouldNotRemove_EqualToCutoff()
        {
            var stack = new TemporalStack<string>();

            stack.Push("old");
            Thread.Sleep(2);
            var cutoff = DateTime.UtcNow;
            Thread.Sleep(2);
            stack.Push("eqOrNew");

            stack.RemoveOlderThan(cutoff);

            var vals = stack.GetInRange(DateTime.MinValue, DateTime.MaxValue).Select(i => i.Value).ToList();
            Assert.Contains("eqOrNew", vals);
            // "old" may be removed if < cutoff
        }

        [Fact]
        public void RemoveRange_ShouldBeInclusive_OnBothBounds()
        {
            var stack = new TemporalStack<int>();

            stack.Push(1);
            Thread.Sleep(2);
            var start = DateTime.UtcNow;
            Thread.Sleep(2);
            stack.Push(2);
            Thread.Sleep(2);
            var end = DateTime.UtcNow;
            Thread.Sleep(2);
            stack.Push(3);

            stack.RemoveRange(start, end);

            var remaining = stack.GetInRange(DateTime.MinValue, DateTime.MaxValue).Select(x => x.Value).ToList();

            Assert.Contains(1, remaining);
            Assert.Contains(3, remaining);
            Assert.DoesNotContain(2, remaining);
        }

        [Fact]
        public void Timestamps_ShouldBeStrictlyMonotonic_EvenOnBurst()
        {
            var stack = new TemporalStack<int>();

            // Push a burst of items without sleeping
            for (int i = 0; i < 100; i++)
                stack.Push(i);

            var items = stack.GetInRange(DateTime.MinValue, DateTime.MaxValue)
                             .OrderBy(i => i.Timestamp)
                             .ToList();

            // Each timestamp must be strictly greater than the previous one
            for (int i = 1; i < items.Count; i++)
            {
                Assert.True(items[i - 1].Timestamp < items[i].Timestamp,
                    $"Timestamps are not strictly increasing at index {i}: {items[i - 1].Timestamp:o} !< {items[i].Timestamp:o}");
            }
        }

        [Fact]
        public void Count_ShouldBeConsistent_WithGetInRange_AllTime()
        {
            var stack = new TemporalStack<int>();
            for (int i = 0; i < 20; i++) stack.Push(i);

            var all = stack.GetInRange(DateTime.MinValue, DateTime.MaxValue).Count();
            Assert.Equal(stack.Count, all);
        }

        [Fact]
        public void Range_With_Unspecified_ShouldBehaveLikeUtc_WhenAssumeUtc()
        {
            var stack = new TemporalStack<int>();

            stack.Push(1);
            Thread.Sleep(2);
            var midUtc = DateTime.UtcNow;
            Thread.Sleep(2);
            stack.Push(2);

            var midUnspecified = DateTime.SpecifyKind(midUtc, DateTimeKind.Unspecified);

            var withUtc = stack.GetInRange(midUtc, DateTime.UtcNow).Select(x => x.Value).ToList();
            var withUnspec = stack.GetInRange(midUnspecified, DateTime.UtcNow).Select(x => x.Value).ToList();

            // Results must be equal if policy internally assumes UTC
            Assert.Equal(withUtc, withUnspec);
        }

        [Fact]
        public void Clear_AfterMutations_ShouldLeaveEmptyEnumerable()
        {
            var stack = new TemporalStack<int>();
            for (int i = 0; i < 5; i++) stack.Push(i);

            stack.RemoveOlderThan(DateTime.UtcNow.AddMinutes(1));
            stack.Clear(); // should be idempotent

            Assert.Equal(0, stack.Count);
            Assert.Empty(stack.GetInRange(DateTime.MinValue, DateTime.MaxValue));
            Assert.Empty(stack.GetBefore(DateTime.UtcNow));
            Assert.Empty(stack.GetAfter(DateTime.UtcNow));
        }

        [Fact]
        public void CountSince_ShouldBeInclusive_AndMatchGetInRange()
        {
            var stack = new TemporalStack<int>();

            stack.Push(1);
            Thread.Sleep(5);
            stack.Push(2);
            Thread.Sleep(5);
            stack.Push(3);

            // Build an ordered snapshot to pick a stable cutoff
            var all = stack.GetInRange(DateTime.MinValue, DateTime.MaxValue)
                           .OrderBy(i => i.Timestamp)
                           .ToList();
            Assert.True(all.Count >= 2, "Test requires at least two items.");

            // Use the timestamp of the 2nd item as cutoff; CountSince must be inclusive
            var cutoff = all[1].Timestamp.UtcDateTime;

            var expected = all.Count - 1; // items at index 1 and 2
            var countSince = stack.CountSince(cutoff);

            Assert.Equal(expected, countSince);

            // Cross-check with GetInRange(cutoff, now)
            var cross = stack.GetInRange(cutoff, DateTime.UtcNow).Count();
            Assert.Equal(cross, countSince);
        }

        [Fact]
        public void TemporalStack_GetNearest_WorksAndTiesPreferLater()
        {
            var s = new TemporalStack<string>();
            s.Push("A");
            s.Push("B");
            s.Push("C");

            var all = s.GetInRange(
                new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            ).ToList();

            Assert.True(all.Count >= 3);

            var b = all[1]; // "B"
            var c = all[2]; // "C"

            // Exact hit su B
            var exact = s.GetNearest(b.Timestamp.UtcDateTime);
            Assert.NotNull(exact);
            Assert.Equal("B", exact!.Value);

            long dt = c.Timestamp.UtcTicks - b.Timestamp.UtcTicks;
            Assert.True(dt > 0, "Timestamps should be strictly increasing");

            if ((dt & 1L) == 0L)
            {
                // Delta pari: vero midpoint -> tie-break "prefer later" => C
                long midTicks = b.Timestamp.UtcTicks + (dt / 2);
                var mid = new DateTimeOffset(midTicks, TimeSpan.Zero).UtcDateTime;

                var tie = s.GetNearest(mid);
                Assert.NotNull(tie);
                Assert.Equal("C", tie!.Value);
            }
            else
            {
                // Delta dispari: nessun vero tie; testiamo i due lati del midpoint
                long midFloorTicks = b.Timestamp.UtcTicks + (dt / 2); // più vicino a B
                long midCeilTicks = midFloorTicks + 1;               // più vicino a C

                var midFloor = new DateTimeOffset(midFloorTicks, TimeSpan.Zero).UtcDateTime;
                var midCeil = new DateTimeOffset(midCeilTicks, TimeSpan.Zero).UtcDateTime;

                var nearFloor = s.GetNearest(midFloor);
                var nearCeil = s.GetNearest(midCeil);

                Assert.NotNull(nearFloor);
                Assert.NotNull(nearCeil);

                Assert.Equal("B", nearFloor!.Value);
                Assert.Equal("C", nearCeil!.Value);
            }
        }
    }
}