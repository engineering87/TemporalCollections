// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Collections;
using TemporalCollections.Models;

namespace TemporalCollections.Tests.Collections
{
    /// <summary>
    /// xUnit test suite for TemporalSegmentedArray{T} using autonomous timestamps.
    /// IMPORTANT: TemporalItem{T}.Create (used by AddValue) stamps items with strictly
    /// increasing UTC ticks per closed T. Tests therefore derive ranges and cutoffs
    /// from the actual produced timestamps instead of fabricating DateTimeOffset values.
    /// </summary>
    public class TemporalSegmentedArray_AutonomousTimestamp_Tests
    {
        // ---------- Helpers ----------

        /// <summary>
        /// Adds values via AddValue and returns the created items (to capture real timestamps).
        /// </summary>
        private static TemporalItem<int>[] AddValues(TemporalSegmentedArray<int> col, params int[] values)
        {
            var list = new List<TemporalItem<int>>(values.Length);
            foreach (var v in values)
                list.Add(col.AddValue(v));
            return list.ToArray();
        }

        /// <summary>
        /// Asserts that items are strictly ordered by Timestamp.UtcTicks ascending.
        /// </summary>
        private static void AssertStrictlyIncreasing(IReadOnlyList<TemporalItem<int>> items)
        {
            for (int i = 1; i < items.Count; i++)
            {
                Assert.True(items[i - 1].Timestamp.UtcTicks < items[i].Timestamp.UtcTicks,
                    $"Not strictly increasing at {i - 1}->{i}: {items[i - 1].Timestamp:o} !< {items[i].Timestamp:o}");
            }
        }

        /// <summary>
        /// Returns the midpoint (in UTC ticks) between a and b.
        /// </summary>
        private static DateTimeOffset Mid(DateTimeOffset a, DateTimeOffset b)
        {
            long mid = (a.UtcTicks + b.UtcTicks) / 2;
            return new DateTimeOffset(mid, TimeSpan.Zero);
        }
        private static readonly int[] expected = [1, 2, 3, 4];

        // ---------- Tests ----------

        [Fact(DisplayName = "AddValue increments count and preserves strict timestamp monotonicity")]
        public void AddValue_InOrder_Monotonic()
        {
            var col = new TemporalSegmentedArray<int>(segmentCapacity: 4);

            var created = AddValues(col, 1, 2, 3, 4);
            Assert.Equal(4, col.Count);

            var all = col.ToArray();
            Assert.Equal(created.Length, all.Length);
            AssertStrictlyIncreasing(all);
            Assert.Equal(expected, all.Select(x => x.Value).ToArray());
        }

        [Fact(DisplayName = "GetInRange is inclusive using real item timestamps")]
        public void GetInRange_Inclusive_WithRealTimestamps()
        {
            var col = new TemporalSegmentedArray<int>(segmentCapacity: 2);
            var items = AddValues(col, 10, 20, 30, 40);

            // Inclusive range: [items[1].ts, items[2].ts] -> expect values 20 and 30
            var res = col.GetInRange(items[1].Timestamp, items[2].Timestamp).ToArray();
            Assert.Equal([20, 30], res.Select(x => x.Value).ToArray());

            // Swap bounds should yield same result
            var res2 = col.GetInRange(items[2].Timestamp, items[1].Timestamp).ToArray();
            Assert.Equal([20, 30], res2.Select(x => x.Value).ToArray());
        }

        [Fact(DisplayName = "GetBefore is strictly before (exclusive) using midpoint cutoffs")]
        public void GetBefore_Exclusive_WithMidpoint()
        {
            var col = new TemporalSegmentedArray<int>(segmentCapacity: 2);
            var items = AddValues(col, 10, 20, 30);

            var cutoff = Mid(items[0].Timestamp, items[1].Timestamp); // strictly between 10 and 20
            var res = col.GetBefore(cutoff).ToArray();
            Assert.Single(res);
            Assert.Equal(10, res[0].Value);

            // Using the exact timestamp of items[1] excludes it
            var res2 = col.GetBefore(items[1].Timestamp).ToArray();
            Assert.Single(res2);
            Assert.Equal(10, res2[0].Value);
        }

        [Fact(DisplayName = "GetAfter is strictly after (exclusive) using midpoint cutoffs")]
        public void GetAfter_Exclusive_WithMidpoint()
        {
            var col = new TemporalSegmentedArray<int>(segmentCapacity: 2);
            var items = AddValues(col, 10, 20, 30);

            var cutoff = Mid(items[1].Timestamp, items[2].Timestamp); // strictly between 20 and 30
            var res = col.GetAfter(cutoff).ToArray();
            Assert.Single(res);
            Assert.Equal(30, res[0].Value);

            // Using the exact timestamp of items[1] excludes 20 (exclusive)
            var res2 = col.GetAfter(items[1].Timestamp).ToArray();
            Assert.Equal([30], res2.Select(x => x.Value).ToArray());
        }

        [Fact(DisplayName = "CountInRange counts inclusively with autonomous timestamps")]
        public void CountInRange_Inclusive_WithRealTimestamps()
        {
            var col = new TemporalSegmentedArray<int>(segmentCapacity: 3);
            var items = AddValues(col, 10, 20, 30, 40);

            Assert.Equal(3, col.CountInRange(items[1].Timestamp, items[3].Timestamp)); // 20..40 inclusive
            Assert.Equal(2, col.CountInRange(items[0].Timestamp, items[1].Timestamp)); // 10..20 inclusive
            Assert.Equal(0, col.CountInRange(items[3].Timestamp.AddSeconds(1), items[3].Timestamp.AddSeconds(2)));
        }

        [Fact(DisplayName = "CountSince is >= (inclusive lower bound)")]
        public void CountSince_Inclusive()
        {
            var col = new TemporalSegmentedArray<int>(segmentCapacity: 4);
            var items = AddValues(col, 10, 20, 30, 40);

            Assert.Equal(2, col.CountSince(items[2].Timestamp)); // from 30 inclusive: 30, 40
            Assert.Equal(4, col.CountSince(items[0].Timestamp)); // from 10 inclusive: 10, 20, 30, 40

            // After the last item -> zero
            var afterLast = new DateTimeOffset(items[^1].Timestamp.UtcTicks + 1, TimeSpan.Zero);
            Assert.Equal(0, col.CountSince(afterLast));
        }

        [Fact(DisplayName = "RemoveOlderThan removes strictly older items and can drop whole segments")]
        public void RemoveOlderThan_StrictAndDropsSegments()
        {
            var col = new TemporalSegmentedArray<int>(segmentCapacity: 2);
            var items = AddValues(col, 10, 20, 30, 40, 50);

            // Remove strictly older than items[2] (value 30) -> removes 10, 20
            col.RemoveOlderThan(items[2].Timestamp);

            var all = col.ToArray();
            Assert.Equal([30, 40, 50], all.Select(x => x.Value).ToArray());
            AssertStrictlyIncreasing(all);
        }

        [Fact(DisplayName = "RemoveRange removes inclusively and can drop whole segments")]
        public void RemoveRange_InclusiveAndDropsSegments()
        {
            var col = new TemporalSegmentedArray<int>(segmentCapacity: 2);
            var items = AddValues(col, 10, 20, 30, 40, 50, 60);

            // Remove [items[1], items[4]] inclusive -> remove 20,30,40,50
            col.RemoveRange(items[1].Timestamp, items[4].Timestamp);

            var all = col.ToArray();
            Assert.Equal([10, 60], all.Select(x => x.Value).ToArray());
            AssertStrictlyIncreasing(all);
        }

        [Fact(DisplayName = "GetNearest returns nearest by ticks; in tie it prefers the earlier item")]
        public void GetNearest_TiePrefersEarlier()
        {
            var col = new TemporalSegmentedArray<int>(segmentCapacity: 3);
            var items = AddValues(col, 100, 200, 300);

            // Midpoint between items[1] and items[2] → tie, expect earlier (items[1])
            var mid = Mid(items[1].Timestamp, items[2].Timestamp);
            var nearest = col.GetNearest(mid);
            Assert.NotNull(nearest);
            Assert.Equal(200, nearest!.Value);

            // Midpoint between items[0] and items[1] → earlier (items[0])
            var mid2 = Mid(items[0].Timestamp, items[1].Timestamp);
            nearest = col.GetNearest(mid2);
            Assert.NotNull(nearest);
            Assert.Equal(100, nearest!.Value);
        }

        [Fact(DisplayName = "GetLatest and GetEarliest return correct items")]
        public void LatestAndEarliest_Work()
        {
            var col = new TemporalSegmentedArray<int>(segmentCapacity: 3);
            Assert.Null(col.GetLatest());
            Assert.Null(col.GetEarliest());

            var a = col.AddValue(10);
            var b = col.AddValue(20);
            var c = col.AddValue(5); // created after b, but still later in time (timestamps are monotonic by creation)

            // Earliest is 'a' (first inserted), latest is 'c' (last inserted)
            var earliest = col.GetEarliest();
            var latest = col.GetLatest();

            Assert.NotNull(earliest);
            Assert.NotNull(latest);
            Assert.Equal(a.Value, earliest!.Value);
            Assert.Equal(c.Value, latest!.Value);
        }

        [Fact(DisplayName = "GetTimeSpan returns zero for empty/singleton and correct span otherwise")]
        public void GetTimeSpan_Works()
        {
            var col = new TemporalSegmentedArray<int>(segmentCapacity: 2);
            Assert.Equal(TimeSpan.Zero, col.GetTimeSpan());

            var x = col.AddValue(10);
            Assert.Equal(TimeSpan.Zero, col.GetTimeSpan());

            var y = col.AddValue(20);
            Assert.Equal(y.Timestamp - x.Timestamp, col.GetTimeSpan());
        }

        [Fact(DisplayName = "Clear removes all items")]
        public void Clear_Works()
        {
            var col = new TemporalSegmentedArray<int>(segmentCapacity: 2);
            AddValues(col, 10, 20);
            Assert.Equal(2, col.Count);

            col.Clear();
            Assert.Equal(0, col.Count);
            Assert.Empty(col.GetInRange(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
        }

        [Fact(DisplayName = "ToArray returns full snapshot in chronological order")]
        public void ToArray_ReturnsSnapshot()
        {
            var col = new TemporalSegmentedArray<int>(segmentCapacity: 2);
            var a = col.AddValue(20);
            var b = col.AddValue(10);
            var c = col.AddValue(30);

            var arr = col.ToArray();
            AssertStrictlyIncreasing(arr);
            Assert.Equal([20, 10, 30], new[] { a.Value, b.Value, c.Value }); // sanity on creation
            Assert.Equal([a.Value, b.Value, c.Value], arr.Select(x => x.Value).ToArray()); // creation order equals time order here
        }

        [Fact(DisplayName = "Parallel AddValue is thread-safe and produces strictly increasing ticks")]
        public void Concurrency_AddValue_Works()
        {
            var col = new TemporalSegmentedArray<int>(segmentCapacity: 64);
            int writers = 8;
            int perWriter = 300;

            Parallel.For(0, writers, _ =>
            {
                for (int i = 0; i < perWriter; i++)
                    col.AddValue(i);
            });

            Assert.Equal(writers * perWriter, col.Count);

            var arr = col.ToArray();
            AssertStrictlyIncreasing(arr); // TemporalItem<T>.Create ensures monotonic UTC ticks per T
        }
    }
}