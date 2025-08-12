// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Collections;
using TemporalCollections.Models;

namespace TemporalCollections.Tests.Collections
{
    public class TemporalSlidingWindowSetTests
    {
        [Fact]
        public void Constructor_ThrowsOnNonPositiveWindow()
        {
            Assert.Throws<ArgumentException>(() => new TemporalSlidingWindowSet<string>(TimeSpan.Zero));
            Assert.Throws<ArgumentException>(() => new TemporalSlidingWindowSet<string>(TimeSpan.FromSeconds(-1)));
        }

        [Fact]
        public void Add_AddsItemsAndPreventsDuplicates()
        {
            var set = new TemporalSlidingWindowSet<string>(TimeSpan.FromMinutes(1));

            bool added1 = set.Add("item1");
            bool added2 = set.Add("item2");
            bool addedAgain = set.Add("item1"); // duplicate

            Assert.True(added1);
            Assert.True(added2);
            Assert.False(addedAgain);
            Assert.Equal(2, set.Count);
        }

        [Fact]
        public void GetItems_ReturnsAllItems()
        {
            var set = new TemporalSlidingWindowSet<string>(TimeSpan.FromMinutes(1));

            set.Add("a");
            set.Add("b");

            var items = set.GetItems().Select(i => i.Value).ToList();

            Assert.Contains("a", items);
            Assert.Contains("b", items);
            Assert.Equal(2, items.Count);
        }

        [Fact]
        public void GetInRange_ReturnsOnlyItemsWithinRange()
        {
            var set = new TemporalSlidingWindowSet<string>(TimeSpan.FromMinutes(10));

            var now = DateTime.UtcNow;
            var oldTimestamp = now - TimeSpan.FromMinutes(20);
            var recentTimestamp = now - TimeSpan.FromMinutes(5);

            // Aggiungo manualmente TemporalItem con timestamp controllati per il test
            var dictField = typeof(TemporalSlidingWindowSet<string>)
                .GetField("_dict", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var dict = (System.Collections.Concurrent.ConcurrentDictionary<string, TemporalItem<string>>)dictField.GetValue(set);

            dict.TryAdd("old", new TemporalItem<string>("old", oldTimestamp));
            dict.TryAdd("recent", new TemporalItem<string>("recent", recentTimestamp));

            var results = set.GetInRange(now - TimeSpan.FromMinutes(6), now);

            var values = results.Select(i => i.Value).ToList();

            Assert.Contains("recent", values);
            Assert.DoesNotContain("old", values);
        }

        [Fact]
        public void RemoveExpired_RemovesOnlyExpiredItems()
        {
            var window = TimeSpan.FromMinutes(10);
            var set = new TemporalSlidingWindowSet<string>(window);

            var now = DateTime.UtcNow;
            var dictField = typeof(TemporalSlidingWindowSet<string>)
                .GetField("_dict", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var dict = (System.Collections.Concurrent.ConcurrentDictionary<string, TemporalItem<string>>)dictField.GetValue(set);

            dict.TryAdd("expired", new TemporalItem<string>("expired", now - TimeSpan.FromMinutes(20)));
            dict.TryAdd("valid", new TemporalItem<string>("valid", now - TimeSpan.FromMinutes(5)));

            Assert.Equal(2, set.Count);

            set.RemoveExpired();

            Assert.Single(set.GetItems());
            Assert.Contains(set.GetItems(), i => i.Value == "valid");
        }

        [Fact]
        public void RemoveOlderThan_RemovesItemsOlderThanCutoff()
        {
            var set = new TemporalSlidingWindowSet<string>(TimeSpan.FromMinutes(10));

            var now = DateTime.UtcNow;

            var dictField = typeof(TemporalSlidingWindowSet<string>)
                .GetField("_dict", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var dict = (System.Collections.Concurrent.ConcurrentDictionary<string, TemporalItem<string>>)dictField.GetValue(set);

            dict.TryAdd("old", new TemporalItem<string>("old", now - TimeSpan.FromMinutes(15)));
            dict.TryAdd("new", new TemporalItem<string>("new", now - TimeSpan.FromMinutes(5)));

            Assert.Equal(2, set.Count);

            set.RemoveOlderThan(now - TimeSpan.FromMinutes(10));

            var values = set.GetItems().Select(i => i.Value).ToList();

            Assert.Single(values);
            Assert.Contains("new", values);
            Assert.DoesNotContain("old", values);
        }
    }
}