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
    }
}