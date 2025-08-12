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
    }
}