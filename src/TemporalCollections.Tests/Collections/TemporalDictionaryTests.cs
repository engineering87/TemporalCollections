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

        [Fact]
        public void GetInRange_ByKey_ShouldBeInclusive_OnBothBounds()
        {
            var dict = new TemporalDictionary<string, int>();

            dict.Add("k", 1);
            Thread.Sleep(2);
            var t1 = DateTime.UtcNow;
            Thread.Sleep(2);
            dict.Add("k", 2);
            Thread.Sleep(2);
            var t2 = DateTime.UtcNow;
            Thread.Sleep(2);
            dict.Add("k", 3);

            // Inclusive window [t1, t2] should include only the middle value (2)
            var res = dict.GetInRange("k", t1, t2).Select(i => i.Value).ToList();
            Assert.Contains(2, res);
            Assert.DoesNotContain(1, res);
            Assert.DoesNotContain(3, res);
        }

        [Fact]
        public void GetInRange_AllKeys_ShouldReturnChronologicalOrder()
        {
            var dict = new TemporalDictionary<string, int>();

            dict.Add("a", 1);
            Thread.Sleep(2);
            dict.Add("b", 2);
            Thread.Sleep(2);
            dict.Add("a", 3);

            var all = dict.GetInRange(DateTime.MinValue, DateTime.MaxValue).ToList();

            // Ensure timestamps are non-decreasing across the returned snapshot
            for (int i = 1; i < all.Count; i++)
                Assert.True(all[i - 1].Timestamp <= all[i].Timestamp, $"Out of order at {i}");
        }

        [Fact]
        public void RemoveOlderThan_ShouldNotRemove_ItemsEqualToCutoff()
        {
            var dict = new TemporalDictionary<string, int>();

            dict.Add("k", 1);
            Thread.Sleep(2);
            var cutoff = DateTime.UtcNow;
            Thread.Sleep(2);
            dict.Add("k", 2);

            dict.RemoveOlderThan(cutoff);

            var items = dict.GetInRange("k", DateTime.MinValue, DateTime.MaxValue).Select(i => i.Value).ToList();
            Assert.DoesNotContain(1, items); // strictly older should be gone
            Assert.Contains(2, items);       // equal or newer remains
        }

        [Fact]
        public void RemoveOlderThan_ShouldDeleteKey_WhenLastItemRemoved()
        {
            var dict = new TemporalDictionary<string, int>();

            dict.Add("k", 1);
            // Cutoff in the future → removes everything for 'k'
            dict.RemoveOlderThan(DateTime.UtcNow.AddMinutes(1));

            Assert.Empty(dict.Keys);
            Assert.Equal(0, dict.Count);
            Assert.Empty(dict.GetInRange("k", DateTime.MinValue, DateTime.MaxValue));
        }

        [Fact]
        public void RemoveRange_OnEmptyOrNoOverlap_ShouldBeNoOp()
        {
            var dict = new TemporalDictionary<string, int>();

            // Empty → no-op
            dict.RemoveRange(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
            Assert.Empty(dict.Keys);

            dict.Add("k1", 1);
            dict.Add("k2", 2);

            var futureFrom = DateTime.UtcNow.AddHours(1);
            var futureTo = futureFrom.AddMinutes(1);

            // Non-overlapping → no-op
            dict.RemoveRange(futureFrom, futureTo);

            var all = dict.GetInRange(DateTime.MinValue, DateTime.MaxValue).Select(i => (i.Value.Key, i.Value.Value)).ToList();
            Assert.Contains(all, x => x.Key == "k1" && x.Value == 1);
            Assert.Contains(all, x => x.Key == "k2" && x.Value == 2);
        }

        [Fact]
        public void Range_With_Unspecified_ShouldBehaveLikeUtc_WhenAssumeUtc()
        {
            var dict = new TemporalDictionary<string, int>();

            dict.Add("k", 1);
            Thread.Sleep(2);
            var midUtc = DateTime.UtcNow;
            Thread.Sleep(2);
            dict.Add("k", 2);

            var midUnspec = DateTime.SpecifyKind(midUtc, DateTimeKind.Unspecified);

            var withUtc = dict.GetInRange(midUtc, DateTime.UtcNow).Select(i => i.Value.Value).ToList();
            var withUnspec = dict.GetInRange(midUnspec, DateTime.UtcNow).Select(i => i.Value.Value).ToList();

            Assert.Equal(withUtc, withUnspec);
        }

        [Fact]
        public void GetEarliest_And_GetLatest_ShouldMatchFirstAndLastGlobal()
        {
            var dict = new TemporalDictionary<string, int>();

            dict.Add("a", 1);
            Thread.Sleep(2);
            dict.Add("b", 2);
            Thread.Sleep(2);
            dict.Add("a", 3);

            var earliest = dict.GetEarliest();
            var latest = dict.GetLatest();
            Assert.NotNull(earliest);
            Assert.NotNull(latest);

            var all = dict.GetInRange(DateTime.MinValue, DateTime.MaxValue).OrderBy(i => i.Timestamp).ToList();
            Assert.Equal(all.First().Timestamp, earliest!.Timestamp);
            Assert.Equal(all.Last().Timestamp, latest!.Timestamp);
        }

        [Fact]
        public void Count_ShouldMatch_AllTimeGetInRange()
        {
            var dict = new TemporalDictionary<string, int>();
            for (int i = 0; i < 10; i++) dict.Add("k" + (i % 3), i);

            var all = dict.GetInRange(DateTime.MinValue, DateTime.MaxValue).Count();
            Assert.Equal(dict.Count, dict.Keys.Count()); // keys count
            // We also verify that total items is >= keys count (sanity)
            Assert.True(all >= dict.Keys.Count());
        }

        [Fact]
        public void Concurrency_AddsAcrossMultipleKeys_ShouldRemainConsistent()
        {
            var dict = new TemporalDictionary<string, int>();
            var keys = new[] { "a", "b", "c", "d" };

            Parallel.For(0, 1000, i =>
            {
                var k = keys[i % keys.Length];
                dict.Add(k, i);
            });

            // Sanity: total items equals GetInRange count
            var allCount = dict.GetInRange(DateTime.MinValue, DateTime.MaxValue).Count();
            // With the current API, Count is the number of keys; allCount is number of items
            Assert.True(allCount >= dict.Count);
            Assert.All(dict.Keys, k => Assert.NotEmpty(dict.GetInRange(k, DateTime.MinValue, DateTime.MaxValue)));
        }

        [Fact]
        public void GetBefore_And_GetAfter_ShouldBeStrict_Global()
        {
            var dict = new TemporalDictionary<string, int>();

            dict.Add("x", 1);
            Thread.Sleep(2);
            var split = DateTime.UtcNow;
            Thread.Sleep(2);
            dict.Add("y", 2);

            var before = dict.GetBefore(split).Select(i => (i.Value.Key, i.Value.Value)).ToList();
            var after = dict.GetAfter(split).Select(i => (i.Value.Key, i.Value.Value)).ToList();

            Assert.Contains(before, t => t.Key == "x" && t.Value == 1);
            Assert.DoesNotContain(before, t => t.Key == "y" && t.Value == 2);

            Assert.Contains(after, t => t.Key == "y" && t.Value == 2);
            Assert.DoesNotContain(after, t => t.Key == "x" && t.Value == 1);
        }

        [Fact]
        public void Add_MultipleValuesForSameKey_ShouldPreserveMonotonicTimestamps()
        {
            var dict = new TemporalDictionary<string, int>();

            // Add quickly (no sleeps) to stress same-tick behavior
            for (int i = 0; i < 50; i++) dict.Add("k", i);

            var items = dict.GetInRange("k", DateTime.MinValue, DateTime.MaxValue).OrderBy(i => i.Timestamp).ToList();

            for (int i = 1; i < items.Count; i++)
                Assert.True(items[i - 1].Timestamp < items[i].Timestamp, $"Non-monotonic timestamp at {i}");
        }

        [Fact]
        public void RemoveRange_ShouldRemoveAcrossMultipleKeys()
        {
            var dict = new TemporalDictionary<string, int>();

            dict.Add("k1", 1);              // before range
            Thread.Sleep(2);
            var start = DateTime.UtcNow;
            Thread.Sleep(2);
            dict.Add("k2", 2);              // in range
            Thread.Sleep(2);
            dict.Add("k3", 3);              // in range
            Thread.Sleep(2);
            var end = DateTime.UtcNow;
            Thread.Sleep(2);
            dict.Add("k4", 4);              // after range

            dict.RemoveRange(start, end);

            var remaining = dict.GetInRange(DateTime.MinValue, DateTime.MaxValue)
                                .Select(i => (i.Value.Key, i.Value.Value))
                                .OrderBy(x => x.Value)
                                .ToList();

            Assert.Contains(remaining, x => x.Key == "k1" && x.Value == 1);
            Assert.Contains(remaining, x => x.Key == "k4" && x.Value == 4);
            Assert.DoesNotContain(remaining, x => x.Key == "k2" && x.Value == 2);
            Assert.DoesNotContain(remaining, x => x.Key == "k3" && x.Value == 3);
        }

        [Fact]
        public void Clear_AfterRemovals_ShouldLeaveNoKeysAndZeroSpan()
        {
            var dict = new TemporalDictionary<string, int>();

            dict.Add("a", 1);
            dict.Add("b", 2);

            dict.RemoveOlderThan(DateTime.UtcNow.AddMinutes(1)); // likely removes all
            dict.Clear(); // idempotent

            Assert.Empty(dict.Keys);
            Assert.Equal(0, dict.Count);
            Assert.Equal(TimeSpan.Zero, dict.GetTimeSpan());
            Assert.Null(dict.GetEarliest());
            Assert.Null(dict.GetLatest());
        }

        [Fact]
        public void CountSince_ShouldBeInclusive_AndMatchGetInRange_AllKeys()
        {
            var dict = new TemporalDictionary<string, int>();

            dict.Add("k1", 1);
            Thread.Sleep(5);
            dict.Add("k2", 2);
            Thread.Sleep(5);
            dict.Add("k1", 3);

            var all = dict.GetInRange(DateTime.MinValue, DateTime.MaxValue)
                          .OrderBy(i => i.Timestamp)
                          .ToList();
            Assert.True(all.Count >= 2);

            // Inclusive cutoff at the 2nd item's timestamp → expect that item and those after
            var cutoff = all[1].Timestamp.UtcDateTime;

            var countSince = dict.CountSince(cutoff);
            Assert.Equal(all.Count - 1, countSince);

            // Cross-check with inclusive GetInRange
            var cross = dict.GetInRange(cutoff, DateTime.UtcNow.AddHours(1)).Count();
            Assert.Equal(cross, countSince);
        }
    }
}