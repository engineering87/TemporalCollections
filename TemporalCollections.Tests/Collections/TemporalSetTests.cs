// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Collections;

namespace TemporalCollections.Tests.Collections
{
    public class TemporalSetTests
    {
        [Fact]
        public void Add_ShouldAddNewItem_AndReturnTrue()
        {
            var set = new TemporalSet<string>();

            var result = set.Add("A");

            Assert.True(result);
            Assert.True(set.Contains("A"));
            Assert.Equal(1, set.Count);

            var snapshot = set.GetItems().ToList();
            Assert.Single(snapshot);
            Assert.Equal("A", snapshot[0].Value);
            Assert.InRange(snapshot[0].Timestamp, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddSeconds(1));
        }

        [Fact]
        public void Add_Duplicate_ShouldReturnFalse_AndNotIncreaseCount()
        {
            var set = new TemporalSet<string>();

            var firstAdd = set.Add("A");
            var secondAdd = set.Add("A");

            Assert.True(firstAdd);
            Assert.False(secondAdd);
            Assert.Equal(1, set.Count);
        }

        [Fact]
        public void Remove_ShouldRemoveItemIfExists()
        {
            var set = new TemporalSet<int>();
            set.Add(42);

            Assert.True(set.Contains(42));

            var removed = set.Remove(42);

            Assert.True(removed);
            Assert.False(set.Contains(42));
            Assert.Equal(0, set.Count);
        }

        [Fact]
        public void Remove_NonExisting_ShouldReturnFalse()
        {
            var set = new TemporalSet<int>();

            var removed = set.Remove(999);

            Assert.False(removed);
        }

        [Fact]
        public void Add_ShouldBeThreadSafe_WhenManyParallelAdds()
        {
            var set = new TemporalSet<int>();

            Parallel.For(0, 2000, i =>
            {
                set.Add(i);
            });

            // All distinct ints from 0..1999 inserted in parallel -> count should be 2000
            Assert.Equal(2000, set.Count);

            // Spot-check: some values present
            Assert.True(set.Contains(0));
            Assert.True(set.Contains(1999));
        }
    }
}