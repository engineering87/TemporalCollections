// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Collections;

namespace TemporalCollections.Tests.Collections
{
    public class TemporalDictionaryTests
    {
        [Fact]
        public void Add_IncreasesCountAndKeys()
        {
            var dict = new TemporalDictionary<string, int>();
            Assert.Equal(0, dict.Count);

            dict.Add("key1", 42);
            Assert.Equal(1, dict.Count);
            Assert.Contains("key1", dict.Keys);
        }

        [Fact]
        public void GetInRange_ByKey_ReturnsCorrectItems()
        {
            var dict = new TemporalDictionary<string, string>();
            var now = DateTime.UtcNow;

            dict.Add("a", "first");
            Thread.Sleep(10);
            dict.Add("a", "second");
            Thread.Sleep(10);
            dict.Add("b", "third");

            var from = now.AddMilliseconds(5);
            var to = DateTime.UtcNow;

            var results = dict.GetInRange("a", from, to).ToList();

            Assert.All(results, item => Assert.InRange(item.Timestamp, from, to));
            Assert.Contains(results, i => i.Value == "second");
            Assert.DoesNotContain(results, i => i.Value == "first");
        }

        [Fact]
        public void GetInRange_OverAllKeys_ReturnsCorrectItems()
        {
            var dict = new TemporalDictionary<string, int>();

            dict.Add("k1", 1);
            Thread.Sleep(10);
            dict.Add("k2", 2);

            var from = DateTime.UtcNow.AddMilliseconds(-20);
            var to = DateTime.UtcNow.AddMilliseconds(20);

            var results = dict.GetInRange(from, to).ToList();

            Assert.Contains(results, i => i.Value.Key == "k1" && i.Value.Value == 1);
            Assert.Contains(results, i => i.Value.Key == "k2" && i.Value.Value == 2);
        }

        [Fact]
        public void RemoveOlderThan_RemovesOldItemsAndKeys()
        {
            var dict = new TemporalDictionary<string, int>();

            dict.Add("key1", 1);
            Thread.Sleep(10);
            var cutoff = DateTime.UtcNow;
            Thread.Sleep(10);
            dict.Add("key1", 2);
            dict.Add("key2", 3);

            dict.RemoveOlderThan(cutoff);

            // Older item for key1 removed, but key1 still exists due to newer item
            var key1Items = dict.GetInRange("key1", DateTime.MinValue, DateTime.MaxValue).ToList();
            Assert.DoesNotContain(key1Items, i => i.Value == 1);
            Assert.Contains(key1Items, i => i.Value == 2);

            // key2 should remain untouched
            var key2Items = dict.GetInRange("key2", DateTime.MinValue, DateTime.MaxValue).ToList();
            Assert.Contains(key2Items, i => i.Value == 3);

            // Now remove all items older than future date to remove everything
            dict.RemoveOlderThan(DateTime.UtcNow.AddMinutes(1));
            Assert.Empty(dict.Keys);
            Assert.Equal(0, dict.Count);
        }

        [Fact]
        public void GetInRange_ByKey_ReturnsEmptyForUnknownKey()
        {
            var dict = new TemporalDictionary<string, int>();
            var result = dict.GetInRange("missing", DateTime.MinValue, DateTime.MaxValue);
            Assert.Empty(result);
        }

        [Fact]
        public void GetTimeSpan_ShouldBeDifferenceBetweenEarliestAndLatest()
        {
            var dict = new TemporalDictionary<string, int>();

            dict.Add("k1", 1);
            Thread.Sleep(5);
            dict.Add("k2", 2);

            var all = dict.GetInRange(DateTime.MinValue, DateTime.MaxValue).OrderBy(i => i.Timestamp).ToList();
            Assert.True(all.Count >= 2, "Need at least two items for a non-zero span.");

            var expected = all[^1].Timestamp - all[0].Timestamp;
            Assert.Equal(expected, dict.GetTimeSpan());
        }

        [Fact]
        public void CountInRange_OverAllKeys_ReturnsCorrectCount()
        {
            var dict = new TemporalDictionary<string, int>();

            dict.Add("k1", 1);
            Thread.Sleep(5);
            var midStart = DateTime.UtcNow;
            Thread.Sleep(5);
            dict.Add("k2", 2);
            Thread.Sleep(5);
            dict.Add("k3", 3);

            var from = midStart;
            var to = DateTime.UtcNow.AddMinutes(1);

            var expected = dict.GetInRange(from, to).Count();
            var counted = dict.CountInRange(from, to);

            Assert.Equal(expected, counted);
        }

        [Fact]
        public void GetBefore_ShouldReturnItemsStrictlyBeforeTime()
        {
            var dict = new TemporalDictionary<string, int>();

            dict.Add("k1", 10);
            Thread.Sleep(5);
            var split = DateTime.UtcNow;
            Thread.Sleep(5);
            dict.Add("k2", 20);

            var before = dict.GetBefore(split).ToList();

            Assert.Contains(before, i => i.Value.Key == "k1" && i.Value.Value == 10);
            Assert.DoesNotContain(before, i => i.Value.Key == "k2" && i.Value.Value == 20);
        }

        [Fact]
        public void GetAfter_ShouldReturnItemsStrictlyAfterTime()
        {
            var dict = new TemporalDictionary<string, int>();

            dict.Add("k1", 10);
            Thread.Sleep(5);
            var split = DateTime.UtcNow;
            Thread.Sleep(5);
            dict.Add("k2", 20);

            var after = dict.GetAfter(split).ToList();

            Assert.Contains(after, i => i.Value.Key == "k2" && i.Value.Value == 20);
            Assert.DoesNotContain(after, i => i.Value.Key == "k1" && i.Value.Value == 10);
        }

        [Fact]
        public void GetEarliest_And_GetLatest_ShouldReturnFirstAndLastAcrossAllKeys()
        {
            var dict = new TemporalDictionary<string, int>();

            dict.Add("k1", 1);
            Thread.Sleep(5);
            dict.Add("k2", 2);
            Thread.Sleep(5);
            dict.Add("k1", 3);

            var earliest = dict.GetEarliest();
            var latest = dict.GetLatest();

            Assert.NotNull(earliest);
            Assert.NotNull(latest);

            var all = dict.GetInRange(DateTime.MinValue, DateTime.MaxValue).OrderBy(i => i.Timestamp).ToList();
            Assert.Equal(all.First().Timestamp, earliest!.Timestamp);
            Assert.Equal(all.Last().Timestamp, latest!.Timestamp);
        }

        [Fact]
        public void RemoveRange_ShouldDeleteItemsWithinInclusiveBounds()
        {
            var dict = new TemporalDictionary<string, int>();

            dict.Add("k1", 1);              // A
            Thread.Sleep(5);
            var tBStart = DateTime.UtcNow;
            Thread.Sleep(5);
            dict.Add("k2", 2);              // B
            Thread.Sleep(5);
            dict.Add("k1", 3);              // C
            Thread.Sleep(5);
            var tDEnd = DateTime.UtcNow;
            Thread.Sleep(5);
            dict.Add("k3", 4);

            // Remove all between tBStart e tDEnd
            dict.RemoveRange(tBStart, tDEnd);

            var remaining = dict.GetInRange(DateTime.MinValue, DateTime.MaxValue).Select(i => (i.Value.Key, i.Value.Value)).ToList();

            Assert.Contains(remaining, x => x.Key == "k1" && x.Value == 1);
            Assert.Contains(remaining, x => x.Key == "k3" && x.Value == 4);
            Assert.DoesNotContain(remaining, x => x.Key == "k2" && x.Value == 2);
            Assert.DoesNotContain(remaining, x => x.Key == "k1" && x.Value == 3);
        }

        [Fact]
        public void Clear_ShouldEmptyDictionaryAndResetQueryableState()
        {
            var dict = new TemporalDictionary<string, int>();

            dict.Add("k1", 1);
            dict.Add("k2", 2);

            dict.Clear();

            Assert.Equal(0, dict.Count);
            Assert.Empty(dict.Keys);
            Assert.Empty(dict.GetInRange(DateTime.MinValue, DateTime.MaxValue));
            Assert.Equal(TimeSpan.Zero, dict.GetTimeSpan());
            Assert.Null(dict.GetEarliest());
            Assert.Null(dict.GetLatest());
        }

    }
}