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
    }
}