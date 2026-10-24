// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using System.Globalization;
using TemporalCollections.Collections;
using TemporalCollections.Models;

namespace TemporalCollections.Tests.Collections
{
    /// <summary>
    /// xUnit test suite for TemporalMultimap<TKey, TValue>.
    ///
    /// Focus areas:
    /// - Per-key and global inserts (AddValue, Add, AddRange)
    /// - Per-key queries (GetValuesInRange, CountForKey, ContainsKey)
    /// - Global queries (GetInRange, GetBefore, GetAfter, CountInRange, CountSince, GetNearest)
    /// - Retention operations (RemoveOlderThan, RemoveRange, RemoveKey, Clear)
    /// - Time span and latest/earliest calculations
    /// - Concurrency safety for AddValue across multiple keys
    /// - Ordering guarantees by timestamp (strictly increasing within a key; globally sorted results)
    /// </summary>
    public sealed class TemporalMultimapTests
    {
        // ---------- Helpers ----------

        /// <summary>
        /// Adds values for a specific key using AddValue and returns the created items (to capture real timestamps).
        /// </summary>
        private static TemporalItem<KeyValuePair<string, int>>[] AddValuesForKey(TemporalMultimap<string, int> map, string key, params int[] values)
        {
            var list = new List<TemporalItem<KeyValuePair<string, int>>>(values.Length);
            foreach (var v in values)
                list.Add(map.AddValue(key, v));
            return list.ToArray();
        }

        /// <summary>
        /// Asserts that items are strictly ordered by Timestamp.UtcTicks ascending.
        /// </summary>
        private static void AssertStrictlyIncreasing<T>(IReadOnlyList<TemporalItem<T>> items)
        {
            for (int i = 1; i < items.Count; i++)
            {
                Assert.True(items[i - 1].Timestamp.UtcTicks < items[i].Timestamp.UtcTicks,
                    $"Not strictly increasing at {i - 1}->{i}: {items[i - 1].Timestamp:o} !< {items[i].Timestamp:o}");
            }
        }

        /// <summary>
        /// Midpoint between two timestamps (UTC ticks).
        /// </summary>
        private static DateTimeOffset Mid(DateTimeOffset a, DateTimeOffset b)
        {
            long m = (a.UtcTicks + b.UtcTicks) / 2;
            return new DateTimeOffset(m, TimeSpan.Zero);
        }

        // ---------- Tests ----------

        [Fact(DisplayName = "AddValue stores (key,value) with monotonic timestamps per closed type")]
        public void AddValue_Basic()
        {
            var map = new TemporalMultimap<string, int>();

            var a = map.AddValue("A", 1);
            var b = map.AddValue("A", 2);
            var c = map.AddValue("B", 10);

            Assert.Equal(3, map.Count);
            Assert.Equal(2, map.KeyCount);

            // Per-key fetch
            var aVals = map.GetValuesInRange("A", DateTimeOffset.MinValue, DateTimeOffset.MaxValue).ToArray();
            AssertStrictlyIncreasing(aVals);
            Assert.Equal([1, 2], aVals.Select(x => x.Value).ToArray());

            var bVals = map.GetValuesInRange("B", DateTimeOffset.MinValue, DateTimeOffset.MaxValue).ToArray();
            Assert.Single(bVals);
            Assert.Equal(10, bVals[0].Value);
        }

        [Fact(DisplayName = "Add with pre-built temporal item maintains per-key order (binary insert)")]
        public void Add_PreBuilt_OutOfOrder_InsertsCorrectly()
        {
            var map = new TemporalMultimap<string, int>();

            // Append some values via AddValue (monotonic creation)
            var i1 = map.AddValue("A", 100);
            var i2 = map.AddValue("A", 300);

            // Create a manual item with an intermediate timestamp to force binary insertion
            var midTs = Mid(i1.Timestamp, i2.Timestamp);
            var manual = new TemporalItem<KeyValuePair<string, int>>(
                new KeyValuePair<string, int>("A", 200),
                midTs);

            map.Add(manual);

            var vals = map.GetValuesInRange("A", DateTimeOffset.MinValue, DateTimeOffset.MaxValue).ToArray();
            AssertStrictlyIncreasing(vals);
            Assert.Equal([100, 200, 300], vals.Select(x => x.Value).ToArray());
        }

        [Fact(DisplayName = "Per-key RemoveOlderThan drops strictly older entries")]
        public void RemoveOlderThan_PerKey()
        {
            var map = new TemporalMultimap<string, int>();
            var items = AddValuesForKey(map, "A", 10, 20, 30, 40);

            // Remove items strictly older than the third one (value 30): drop 10,20
            int removed = map.RemoveOlderThan("A", items[2].Timestamp);
            Assert.Equal(2, removed);

            var vals = map.GetValuesInRange("A", DateTimeOffset.MinValue, DateTimeOffset.MaxValue).ToArray();
            AssertStrictlyIncreasing(vals);
            Assert.Equal([30, 40], vals.Select(x => x.Value).ToArray());
        }

        [Fact(DisplayName = "Per-key RemoveRange removes inclusive range")]
        public void RemoveRange_PerKey_Inclusive()
        {
            var map = new TemporalMultimap<string, int>();
            var items = AddValuesForKey(map, "A", 10, 20, 30, 40, 50);

            // Remove [20..40] inclusive => remaining 10, 50
            int removed = map.RemoveRange("A", items[1].Timestamp, items[3].Timestamp);
            Assert.Equal(3, removed);

            var vals = map.GetValuesInRange("A", DateTimeOffset.MinValue, DateTimeOffset.MaxValue).ToArray();
            Assert.Equal([10, 50], vals.Select(x => x.Value).ToArray());
            AssertStrictlyIncreasing(vals);
        }

        [Fact(DisplayName = "RemoveKey deletes all values for a key")]
        public void RemoveKey_Works()
        {
            var map = new TemporalMultimap<string, int>();
            AddValuesForKey(map, "A", 1, 2, 3);
            AddValuesForKey(map, "B", 4);

            Assert.True(map.RemoveKey("A"));
            Assert.False(map.ContainsKey("A"));
            Assert.Equal(1, map.Count);
            Assert.Equal(1, map.KeyCount);
        }

        [Fact(DisplayName = "Global GetInRange is inclusive and globally sorted by timestamp")]
        public void Global_GetInRange_Inclusive_AndSorted()
        {
            var map = new TemporalMultimap<string, int>();
            var a = AddValuesForKey(map, "A", 10, 20, 30);
            var b = AddValuesForKey(map, "B", 100, 200);

            var from = a[1].Timestamp; // A:20
            var to = b[0].Timestamp; // B:100

            var res = map.GetInRange(from, to).ToArray();

            // Values included: A:20, A:30 (if <= B:100), B:100
            // Timestamps are strictly increasing globally; we check sorting by ticks
            Assert.True(res.Length >= 2);
            for (int i = 1; i < res.Length; i++)
                Assert.True(res[i - 1].Timestamp.UtcTicks <= res[i].Timestamp.UtcTicks);

            // Ensure each KVP matches chronological order
            Assert.Contains(res, it => it.Value.Key == "A" && it.Value.Value == 20);
            Assert.Contains(res, it => it.Value.Key == "B" && it.Value.Value == 100);
        }

        [Fact(DisplayName = "Global GetBefore/GetAfter honor exclusivity semantics")]
        public void Global_GetBefore_After_Semantics()
        {
            var map = new TemporalMultimap<string, int>();
            var a = AddValuesForKey(map, "A", 10, 20, 30);
            var b = AddValuesForKey(map, "B", 40);

            // Strictly before A:20 → expect only A:10
            var before = map.GetBefore(a[1].Timestamp).ToArray();
            Assert.Contains(before, it => it.Value.Key == "A" && it.Value.Value == 10);
            Assert.DoesNotContain(before, it => it.Value.Value == 20);

            // Strictly after A:30 → expect B:40 only
            var after = map.GetAfter(a[2].Timestamp).ToArray();
            Assert.Single(after);
            Assert.Equal("B", after[0].Value.Key);
            Assert.Equal(40, after[0].Value.Value);
        }

        [Fact(DisplayName = "Global CountInRange and CountSince compute inclusive counts")]
        public void Global_Counts()
        {
            var map = new TemporalMultimap<string, int>();
            var a = AddValuesForKey(map, "A", 10, 20, 30);
            var b = AddValuesForKey(map, "B", 40, 50);

            Assert.Equal(3, map.CountInRange(a[0].Timestamp, a[2].Timestamp)); // 10..30 inclusive
            Assert.Equal(2, map.CountSince(b[0].Timestamp));                     // >= 40 → 40,50
        }

        [Fact(DisplayName = "GetNearest picks the nearest by ticks; on tie prefers the later (>= time)")]
        public void Global_GetNearest_TiePrefersLater()
        {
            var map = new TemporalMultimap<string, int>();
            var items = AddValuesForKey(map, "A", 100, 200, 300);

            var mid = Mid(items[1].Timestamp, items[2].Timestamp); // between 200 and 300
            var nearest = map.GetNearest(mid);
            Assert.NotNull(nearest);

            long dBefore = Math.Abs(mid.UtcTicks - items[1].Timestamp.UtcTicks);
            long dAfter = Math.Abs(items[2].Timestamp.UtcTicks - mid.UtcTicks);
            // If true tie, prefer later; otherwise prefer closer
            int expected1 = (dAfter <= dBefore) ? 300 : 200;
            Assert.Equal(expected1, nearest!.Value.Value);

            var mid2 = Mid(items[0].Timestamp, items[1].Timestamp);
            nearest = map.GetNearest(mid2);
            Assert.NotNull(nearest);

            long dBefore2 = Math.Abs(mid2.UtcTicks - items[0].Timestamp.UtcTicks);
            long dAfter2 = Math.Abs(items[1].Timestamp.UtcTicks - mid2.UtcTicks);
            int expected2 = (dAfter2 <= dBefore2) ? 200 : 100;
            Assert.Equal(expected2, nearest!.Value.Value);
        }

        [Fact(DisplayName = "GetLatest/GetEarliest/ GetTimeSpan are correct")]
        public void Latest_Earliest_TimeSpan()
        {
            var map = new TemporalMultimap<string, int>();
            Assert.Null(map.GetLatest());
            Assert.Null(map.GetEarliest());
            Assert.Equal(TimeSpan.Zero, map.GetTimeSpan());

            var a = AddValuesForKey(map, "A", 1);
            var b = AddValuesForKey(map, "B", 2);

            var earliest = map.GetEarliest();
            var latest = map.GetLatest();
            Assert.NotNull(earliest);
            Assert.NotNull(latest);
            Assert.True(earliest!.Timestamp <= latest!.Timestamp);

            var span = map.GetTimeSpan();
            Assert.Equal(latest!.Timestamp - earliest!.Timestamp, span);
        }

        [Fact(DisplayName = "Global RemoveOlderThan/RemoveRange remove items across keys")]
        public void Global_Retention()
        {
            var map = new TemporalMultimap<string, int>();
            var a = AddValuesForKey(map, "A", 10, 20, 30);
            var b = AddValuesForKey(map, "B", 40, 50);

            // Remove everything strictly older than A:30 → drops A:10,A:20
            map.RemoveOlderThan(a[2].Timestamp);

            Assert.Equal(3, map.Count);
            var allAfterFirstPurge = map.GetInRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue).ToArray();
            Assert.DoesNotContain(allAfterFirstPurge, it => it.Value.Key == "A" && it.Value.Value == 10);
            Assert.DoesNotContain(allAfterFirstPurge, it => it.Value.Key == "A" && it.Value.Value == 20);

            // Remove [A:30 .. B:40] inclusive → drops A:30 and B:40
            map.RemoveRange(a[2].Timestamp, b[0].Timestamp);

            Assert.Equal(1, map.Count);
            var left = map.GetInRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue).ToArray();
            Assert.Single(left);
            Assert.Equal("B", left[0].Value.Key);
            Assert.Equal(50, left[0].Value.Value);
        }

        [Fact(DisplayName = "Clear removes all keys and items")]
        public void Clear_Works()
        {
            var map = new TemporalMultimap<string, int>();
            AddValuesForKey(map, "A", 1, 2);
            AddValuesForKey(map, "B", 3);

            map.Clear();
            Assert.Equal(0, map.Count);
            Assert.Equal(0, map.KeyCount);
            Assert.Empty(map.GetInRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
        }

        [Fact(DisplayName = "Concurrency: parallel AddValue across keys is thread-safe")]
        public void Concurrency_AddValue_MultiKey()
        {
            var map = new TemporalMultimap<string, int>();
            string[] keys = Enumerable.Range(0, 8).Select(i => $"K{i}").ToArray();
            int perKey = 300;

            Parallel.ForEach(keys, k =>
            {
                for (int i = 0; i < perKey; i++)
                    map.AddValue(k, i);
            });

            Assert.Equal(keys.Length * perKey, map.Count);
            Assert.Equal(keys.Length, map.KeyCount);

            // Verify each key list is strictly increasing in time
            foreach (var k in keys)
            {
                var vals = map.GetValuesInRange(k, DateTimeOffset.MinValue, DateTimeOffset.MaxValue).ToArray();
                Assert.Equal(perKey, vals.Length);
                AssertStrictlyIncreasing(vals);
            }

            // Global snapshot is sorted by timestamp
            var all = map.GetInRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue).ToArray();
            for (int i = 1; i < all.Length; i++)
                Assert.True(all[i - 1].Timestamp.UtcTicks <= all[i].Timestamp.UtcTicks);
        }

        [Fact(DisplayName = "Empty collection: queries return empty / counts 0 / nearest null")]
        public void Empty_Behavior_IsConsistent()
        {
            var map = new TemporalMultimap<string, int>();

            Assert.Equal(0, map.Count);
            Assert.Equal(0, map.KeyCount);

            Assert.Empty(map.GetInRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
            Assert.Empty(map.GetBefore(DateTimeOffset.UtcNow));
            Assert.Empty(map.GetAfter(DateTimeOffset.UtcNow));
            Assert.Equal(0, map.CountInRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
            Assert.Equal(0, map.CountSince(DateTimeOffset.UtcNow.AddDays(-1)));
            Assert.Null(map.GetNearest(DateTimeOffset.UtcNow));
            Assert.Null(map.GetLatest());
            Assert.Null(map.GetEarliest());
            Assert.Equal(TimeSpan.Zero, map.GetTimeSpan());
        }

        [Fact(DisplayName = "GetValuesInRange is inclusive on boundaries (from/to)")]
        public void PerKey_GetValuesInRange_InclusiveBoundaries()
        {
            var map = new TemporalMultimap<string, int>();
            var items = AddValuesForKey(map, "A", 10, 20, 30);

            var exactFirst = map.GetValuesInRange("A", items[0].Timestamp, items[0].Timestamp).ToArray();
            Assert.Single(exactFirst);
            Assert.Equal(10, exactFirst[0].Value);

            var exactLast = map.GetValuesInRange("A", items[2].Timestamp, items[2].Timestamp).ToArray();
            Assert.Single(exactLast);
            Assert.Equal(30, exactLast[0].Value);

            var middle = map.GetValuesInRange("A", items[1].Timestamp, items[1].Timestamp).ToArray();
            Assert.Single(middle);
            Assert.Equal(20, middle[0].Value);
        }

        [Fact(DisplayName = "Range arguments can be swapped: GetInRange/RemoveRange/CountInRange behave same")]
        public void RangeArguments_Swapped_AreHandled()
        {
            var map = new TemporalMultimap<string, int>();
            var a = AddValuesForKey(map, "A", 1, 2, 3, 4, 5);

            // Same results even if from > to
            var r1 = map.GetInRange(a[1].Timestamp, a[3].Timestamp).Select(x => x.Value.Value).ToArray();
            var r2 = map.GetInRange(a[3].Timestamp, a[1].Timestamp).Select(x => x.Value.Value).ToArray();
            Assert.Equal(r1, r2);

            var c1 = map.CountInRange(a[1].Timestamp, a[3].Timestamp);
            var c2 = map.CountInRange(a[3].Timestamp, a[1].Timestamp);
            Assert.Equal(c1, c2);

            // RemoveRange swapped should remove the same number of items (inclusive)
            var map2 = new TemporalMultimap<string, int>();
            var b = AddValuesForKey(map2, "A", 1, 2, 3, 4, 5);
            map2.RemoveRange(b[3].Timestamp, b[1].Timestamp); // swapped
            var left = map2.GetValuesInRange("A", DateTimeOffset.MinValue, DateTimeOffset.MaxValue).Select(x => x.Value).ToArray();
            Assert.Equal(new[] { 1, 5 }, left);
        }

        [Fact(DisplayName = "Per-key RemoveOlderThan is strictly older: cutoff timestamp remains")]
        public void RemoveOlderThan_StrictlyOlder_CutoffStays()
        {
            var map = new TemporalMultimap<string, int>();
            var items = AddValuesForKey(map, "A", 10, 20, 30);

            // cutoff = timestamp of 20, must remove only 10
            int removed = map.RemoveOlderThan("A", items[1].Timestamp);
            Assert.Equal(1, removed);

            var left = map.GetValuesInRange("A", DateTimeOffset.MinValue, DateTimeOffset.MaxValue).ToArray();
            Assert.Equal(new[] { 20, 30 }, left.Select(x => x.Value).ToArray());
        }

        [Fact(DisplayName = "Global RemoveOlderThan is strictly older: cutoff timestamp remains (across keys)")]
        public void Global_RemoveOlderThan_StrictlyOlder_CutoffStays()
        {
            var map = new TemporalMultimap<string, int>();
            var a = AddValuesForKey(map, "A", 1, 2, 3);
            var b = AddValuesForKey(map, "B", 10, 20);

            // cutoff = A:2 timestamp => remove only A:1 (strictly older)
            map.RemoveOlderThan(a[1].Timestamp);

            var all = map.GetInRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue).ToArray();
            Assert.DoesNotContain(all, it => it.Value.Key == "A" && it.Value.Value == 1);
            Assert.Contains(all, it => it.Value.Key == "A" && it.Value.Value == 2);
            Assert.Contains(all, it => it.Value.Key == "B" && it.Value.Value == 10);
        }

        [Fact(DisplayName = "CountForKey/ContainsKey reflect key removal after last item deleted")]
        public void KeyBookkeeping_AfterDeletions_IsCorrect()
        {
            var map = new TemporalMultimap<string, int>();
            var a = AddValuesForKey(map, "A", 1, 2, 3);

            Assert.True(map.ContainsKey("A"));
            Assert.Equal(3, map.CountForKey("A"));
            Assert.Equal(1, map.KeyCount);

            // Remove all via per-key range
            int removed = map.RemoveRange("A", a[0].Timestamp, a[2].Timestamp);
            Assert.Equal(3, removed);

            Assert.False(map.ContainsKey("A"));
            Assert.Equal(0, map.CountForKey("A"));
            Assert.Equal(0, map.KeyCount);
            Assert.Equal(0, map.Count);
        }

        [Fact(DisplayName = "GetNearest tie across keys prefers later timestamp (global tie-break rule)")]
        public void GetNearest_TieAcrossKeys_PrefersLater()
        {
            var map = new TemporalMultimap<string, int>();

            // Build two items around a midpoint by crafting a manual timestamp between two AddValue calls
            var a1 = map.AddValue("A", 100);
            var a2 = map.AddValue("A", 200);

            // Create B item exactly at midpoint between a1 and a2 (manual add)
            var mid = Mid(a1.Timestamp, a2.Timestamp);
            map.Add(new TemporalItem<KeyValuePair<string, int>>(new KeyValuePair<string, int>("B", 999), mid));

            // Query at same midpoint: nearest should be the midpoint item itself (diff=0)
            var nearest = map.GetNearest(mid);
            Assert.NotNull(nearest);
            Assert.Equal("B", nearest!.Value.Key);
            Assert.Equal(999, nearest.Value.Value);

            // Now query at a time equidistant between a1 and mid:
            // nearest should be the later one on tie (consistent with all other collections)
            var mid2 = Mid(a1.Timestamp, mid);
            nearest = map.GetNearest(mid2);
            Assert.NotNull(nearest);

            // Tie scenario depends on integer tick division; enforce the intended rule:
            // if equal diff, implementation prefers later timestamp (>= time).
            var cand1 = map.GetInRange(a1.Timestamp, a1.Timestamp).Single(); // A:100
            var cand2 = map.GetInRange(mid, mid).Single();                   // B:999

            long d1 = Math.Abs(cand1.Timestamp.UtcTicks - mid2.UtcTicks);
            long d2 = Math.Abs(cand2.Timestamp.UtcTicks - mid2.UtcTicks);

            if (d1 == d2)
                Assert.Equal(cand2.Timestamp.UtcTicks, nearest.Timestamp.UtcTicks); // later wins
            else
                Assert.Equal(d1 < d2 ? cand1.Timestamp.UtcTicks : cand2.Timestamp.UtcTicks, nearest.Timestamp.UtcTicks);
        }

        [Fact(DisplayName = "DateTime overloads: Unspecified is treated as UTC (AssumeUtc policy)")]
        public void DateTimeOverloads_Unspecified_AssumeUtc()
        {
            var map = new TemporalMultimap<string, int>();
            var items = AddValuesForKey(map, "A", 1, 2, 3);

            // Use DateTime overloads with Kind=Unspecified but same ticks as UTC DateTime
            DateTime fromUnspec = DateTime.SpecifyKind(items[0].Timestamp.UtcDateTime, DateTimeKind.Unspecified);
            DateTime toUnspec = DateTime.SpecifyKind(items[2].Timestamp.UtcDateTime, DateTimeKind.Unspecified);

            var res = map.GetInRange(fromUnspec, toUnspec).ToArray();
            Assert.Equal(3, res.Length);

            // RemoveOlderThan via DateTime overload: cutoff stays (strictly older)
            DateTime cutoffUnspec = DateTime.SpecifyKind(items[1].Timestamp.UtcDateTime, DateTimeKind.Unspecified);
            map.RemoveOlderThan(cutoffUnspec);

            var left = map.GetValuesInRange("A", DateTimeOffset.MinValue, DateTimeOffset.MaxValue).Select(x => x.Value).ToArray();
            Assert.Equal(new[] { 2, 3 }, left);
        }

        [Fact(DisplayName = "Global ordering: GetInRange returns results globally sorted even with many keys")]
        public void Global_GetInRange_IsGloballySorted_ManyKeys()
        {
            var map = new TemporalMultimap<string, int>();
            for (int k = 0; k < 20; k++)
            {
                string key = "K" + k.ToString(CultureInfo.InvariantCulture);
                for (int i = 0; i < 15; i++)
                    map.AddValue(key, i);
            }

            var all = map.GetInRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue).ToArray();
            Assert.Equal(20 * 15, all.Length);

            for (int i = 1; i < all.Length; i++)
                Assert.True(all[i - 1].Timestamp.UtcTicks <= all[i].Timestamp.UtcTicks);
        }

        [Fact(DisplayName = "AddRange(items): supports mixed keys and preserves order within each key")]
        public void AddRange_Items_MixedKeys_PreservesPerKeyOrder()
        {
            var map = new TemporalMultimap<string, int>();

            // Create a bunch of items with explicit timestamps (some interleaving)
            var baseTicks = DateTimeOffset.UtcNow.UtcTicks;
            var items = new[]
            {
                new TemporalItem<KeyValuePair<string,int>>(new("A", 1), new DateTimeOffset(baseTicks + 10, TimeSpan.Zero)),
                new TemporalItem<KeyValuePair<string,int>>(new("B", 1), new DateTimeOffset(baseTicks + 11, TimeSpan.Zero)),
                new TemporalItem<KeyValuePair<string,int>>(new("A", 2), new DateTimeOffset(baseTicks + 12, TimeSpan.Zero)),
                // Out of order within A to force binary insert
                new TemporalItem<KeyValuePair<string,int>>(new("A", 99), new DateTimeOffset(baseTicks + 9, TimeSpan.Zero)),
                new TemporalItem<KeyValuePair<string,int>>(new("B", 2), new DateTimeOffset(baseTicks + 13, TimeSpan.Zero)),
            };

            map.AddRange(items);

            var a = map.GetValuesInRange("A", DateTimeOffset.MinValue, DateTimeOffset.MaxValue).ToArray();
            AssertStrictlyIncreasing(a);
            Assert.Equal(new[] { 99, 1, 2 }, a.Select(x => x.Value).ToArray());

            var b = map.GetValuesInRange("B", DateTimeOffset.MinValue, DateTimeOffset.MaxValue).ToArray();
            AssertStrictlyIncreasing(b);
            Assert.Equal(new[] { 1, 2 }, b.Select(x => x.Value).ToArray());
        }

        [Fact(DisplayName = "GetTimeSpan: single item returns zero, multiple items returns (max-min) across keys")]
        public void GetTimeSpan_SingleAndMulti()
        {
            var map = new TemporalMultimap<string, int>();
            map.AddValue("A", 1);
            Assert.Equal(TimeSpan.Zero, map.GetTimeSpan());

            var a2 = map.AddValue("A", 2);
            var b1 = map.AddValue("B", 10);

            var earliest = map.GetEarliest()!;
            var latest = map.GetLatest()!;
            Assert.Equal(latest.Timestamp - earliest.Timestamp, map.GetTimeSpan());

            // sanity: latest should be >= both
            Assert.True(latest.Timestamp.UtcTicks >= a2.Timestamp.UtcTicks);
            Assert.True(latest.Timestamp.UtcTicks >= b1.Timestamp.UtcTicks);
        }
    }
}