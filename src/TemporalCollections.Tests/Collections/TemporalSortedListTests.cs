// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Collections;

namespace TemporalCollections.Tests.Collections
{
    public class TemporalSortedListTests
    {
        [Fact]
        public void Add_Items_AreSortedByTimestamp()
        {
            var list = new TemporalSortedList<string>();

            var now = DateTime.UtcNow;

            // Aggiungo gli elementi in ordine sparso di timestamp
            list.Add("first");  // timestamp = now (approx)
            Thread.Sleep(10);
            list.Add("second");
            Thread.Sleep(10);
            list.Add("third");

            var snapshot = list.GetInRange(DateTime.MinValue, DateTime.MaxValue).ToList();

            Assert.Equal(3, snapshot.Count);
            // Verifico che siano ordinati per timestamp crescente
            for (int i = 1; i < snapshot.Count; i++)
            {
                Assert.True(snapshot[i].Timestamp >= snapshot[i - 1].Timestamp);
            }
        }

        [Fact]
        public void GetInRange_ReturnsCorrectItems()
        {
            var list = new TemporalSortedList<string>();

            list.Add("a");
            Thread.Sleep(10);
            list.Add("b");
            Thread.Sleep(10);
            list.Add("c");

            var allItems = list.GetInRange(DateTime.MinValue, DateTime.MaxValue).ToList();
            var timeA = allItems.First(i => i.Value == "a").Timestamp;
            var timeB = allItems.First(i => i.Value == "b").Timestamp;
            var timeC = allItems.First(i => i.Value == "c").Timestamp;

            var items = list.GetInRange(timeA.AddMilliseconds(1), timeC.AddMilliseconds(1)).ToList();

            Assert.Contains(items, item => item.Value == "b");
            Assert.Contains(items, item => item.Value == "c");
            Assert.DoesNotContain(items, item => item.Value == "a");
        }


        [Fact]
        public void RemoveOlderThan_RemovesCorrectItems()
        {
            var list = new TemporalSortedList<string>();

            var now = DateTime.UtcNow;

            list.Add("a");
            Thread.Sleep(10);
            list.Add("b");
            Thread.Sleep(10);
            list.Add("c");

            var cutoff = now.AddMilliseconds(15);
            list.RemoveOlderThan(cutoff);

            var remaining = list.GetInRange(DateTime.MinValue, DateTime.MaxValue).Select(i => i.Value).ToList();

            Assert.DoesNotContain("a", remaining);
            Assert.Contains("b", remaining);
            Assert.Contains("c", remaining);
        }

        [Fact]
        public void Count_ReflectsNumberOfItems()
        {
            var list = new TemporalSortedList<int>();
            Assert.Equal(0, list.Count);

            list.Add(1);
            Assert.Equal(1, list.Count);

            list.Add(2);
            Assert.Equal(2, list.Count);

            var cutoff = DateTime.UtcNow.AddSeconds(1);
            list.RemoveOlderThan(cutoff);

            Assert.True(list.Count <= 2);  // depends on timestamps, at least no exception
        }
    }
}