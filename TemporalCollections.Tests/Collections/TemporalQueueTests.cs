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
    }
}