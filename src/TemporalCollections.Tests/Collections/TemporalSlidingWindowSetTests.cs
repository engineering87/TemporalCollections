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

        [Fact]
        public void GetTimeSpan_ShouldBeDifferenceBetweenEarliestAndLatest()
        {
            var set = new TemporalSlidingWindowSet<string>(TimeSpan.FromMinutes(60));

            // Build a deterministic snapshot via reflection to control timestamps.
            var now = DateTime.UtcNow;
            var dictField = typeof(TemporalSlidingWindowSet<string>)
                .GetField("_dict", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var dict = (System.Collections.Concurrent.ConcurrentDictionary<string, TemporalItem<string>>)dictField!.GetValue(set)!;

            var t1 = now.AddMinutes(-30);
            var t2 = now.AddMinutes(-10);
            var t3 = now.AddMinutes(-5);

            dict.TryAdd("a", new TemporalItem<string>("a", t1));
            dict.TryAdd("b", new TemporalItem<string>("b", t2));
            dict.TryAdd("c", new TemporalItem<string>("c", t3));

            var expected = t3 - t1;
            Assert.Equal(expected, set.GetTimeSpan());
        }

        [Fact]
        public void CountInRange_ShouldReturnCorrectCount()
        {
            var set = new TemporalSlidingWindowSet<string>(TimeSpan.FromHours(1));
            var now = DateTime.UtcNow;

            var dictField = typeof(TemporalSlidingWindowSet<string>)
                .GetField("_dict", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var dict = (System.Collections.Concurrent.ConcurrentDictionary<string, TemporalItem<string>>)dictField!.GetValue(set)!;

            var t1 = now.AddMinutes(-40);
            var t2 = now.AddMinutes(-20);
            var t3 = now.AddMinutes(-10);

            dict.TryAdd("x", new TemporalItem<string>("x", t1));
            dict.TryAdd("y", new TemporalItem<string>("y", t2));
            dict.TryAdd("z", new TemporalItem<string>("z", t3));

            // Inclusive range [t2, t3] should count y and z
            Assert.Equal(2, set.CountInRange(t2, t3));
        }

        [Fact]
        public void GetBefore_ShouldReturnItemsStrictlyBeforeTime()
        {
            var set = new TemporalSlidingWindowSet<string>(TimeSpan.FromHours(1));
            var now = DateTime.UtcNow;

            var dictField = typeof(TemporalSlidingWindowSet<string>)
                .GetField("_dict", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var dict = (System.Collections.Concurrent.ConcurrentDictionary<string, TemporalItem<string>>)dictField!.GetValue(set)!;

            var t1 = now.AddMinutes(-30);
            var t2 = now.AddMinutes(-15);

            dict.TryAdd("old", new TemporalItem<string>("old", t1));
            dict.TryAdd("new", new TemporalItem<string>("new", t2));

            var before = set.GetBefore(t2).Select(i => i.Value).ToList();

            Assert.Contains("old", before);
            Assert.DoesNotContain("new", before);
        }

        [Fact]
        public void GetAfter_ShouldReturnItemsStrictlyAfterTime()
        {
            var set = new TemporalSlidingWindowSet<string>(TimeSpan.FromHours(1));
            var now = DateTime.UtcNow;

            var dictField = typeof(TemporalSlidingWindowSet<string>)
                .GetField("_dict", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var dict = (System.Collections.Concurrent.ConcurrentDictionary<string, TemporalItem<string>>)dictField!.GetValue(set)!;

            var t1 = now.AddMinutes(-30);
            var t2 = now.AddMinutes(-15);

            dict.TryAdd("old", new TemporalItem<string>("old", t1));
            dict.TryAdd("new", new TemporalItem<string>("new", t2));

            var after = set.GetAfter(t1).Select(i => i.Value).ToList();

            Assert.Contains("new", after);
            Assert.DoesNotContain("old", after);
        }

        [Fact]
        public void GetEarliest_And_GetLatest_ShouldReturnOldestAndNewest()
        {
            var set = new TemporalSlidingWindowSet<string>(TimeSpan.FromHours(1));
            var now = DateTime.UtcNow;

            var dictField = typeof(TemporalSlidingWindowSet<string>)
                .GetField("_dict", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var dict = (System.Collections.Concurrent.ConcurrentDictionary<string, TemporalItem<string>>)dictField!.GetValue(set)!;

            var t1 = now.AddMinutes(-45);
            var t2 = now.AddMinutes(-10);
            var t3 = now.AddMinutes(-5);

            dict.TryAdd("earliest", new TemporalItem<string>("earliest", t1));
            dict.TryAdd("middle", new TemporalItem<string>("middle", t2));
            dict.TryAdd("latest", new TemporalItem<string>("latest", t3));

            var earliest = set.GetEarliest();
            var latest = set.GetLatest();

            Assert.NotNull(earliest);
            Assert.NotNull(latest);
            Assert.Equal("earliest", earliest!.Value);
            Assert.Equal(t1, earliest.Timestamp);
            Assert.Equal("latest", latest!.Value);
            Assert.Equal(t3, latest.Timestamp);
        }

        [Fact]
        public void RemoveRange_ShouldDeleteItemsWhoseTimestampFallsInInclusiveBounds()
        {
            var set = new TemporalSlidingWindowSet<string>(TimeSpan.FromHours(1));
            var now = DateTime.UtcNow;

            var dictField = typeof(TemporalSlidingWindowSet<string>)
                .GetField("_dict", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var dict = (System.Collections.Concurrent.ConcurrentDictionary<string, TemporalItem<string>>)dictField!.GetValue(set)!;

            var tA = now.AddMinutes(-50);
            var tB = now.AddMinutes(-30);
            var tC = now.AddMinutes(-20);
            var tD = now.AddMinutes(-5);

            dict.TryAdd("A", new TemporalItem<string>("A", tA));
            dict.TryAdd("B", new TemporalItem<string>("B", tB));
            dict.TryAdd("C", new TemporalItem<string>("C", tC));
            dict.TryAdd("D", new TemporalItem<string>("D", tD));

            // Remove [tB, tC] inclusive -> removes B and C
            set.RemoveRange(tB, tC);

            var remaining = set.GetItems().Select(i => i.Value).ToList();
            Assert.Contains("A", remaining);
            Assert.Contains("D", remaining);
            Assert.DoesNotContain("B", remaining);
            Assert.DoesNotContain("C", remaining);
        }

        [Fact]
        public void Clear_ShouldEmptySetAndResetQueryableState()
        {
            var set = new TemporalSlidingWindowSet<string>(TimeSpan.FromMinutes(10));

            set.Add("x");
            set.Add("y");

            set.Clear();

            Assert.Equal(0, set.Count);
            Assert.Empty(set.GetItems());
            Assert.Empty(set.GetInRange(DateTime.MinValue, DateTime.MaxValue));
            Assert.Equal(TimeSpan.Zero, set.GetTimeSpan());
            Assert.Null(set.GetEarliest());
            Assert.Null(set.GetLatest());
        }

        [Fact]
        public void Add_ShouldBeThreadSafe_WhenManyParallelAdds()
        {
            var set = new TemporalSlidingWindowSet<int>(TimeSpan.FromMinutes(5));

            // Insert many distinct items concurrently; each Add returns true once.
            Parallel.For(0, 2000, i => set.Add(i));

            Assert.Equal(2000, set.Count);

            // Sanity: earliest <= latest by timestamp
            var earliest = set.GetEarliest();
            var latest = set.GetLatest();
            Assert.NotNull(earliest);
            Assert.NotNull(latest);
            Assert.True(earliest!.Timestamp <= latest!.Timestamp);
        }
    }
}