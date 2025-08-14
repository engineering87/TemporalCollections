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
    }
}