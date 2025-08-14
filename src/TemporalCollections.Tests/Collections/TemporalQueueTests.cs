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
    }
}