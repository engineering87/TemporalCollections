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
    }
}