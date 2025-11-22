// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using BenchmarkDotNet.Attributes;
using TemporalCollections.Collections;
using TemporalCollections.Models;

namespace TemporalCollections.PerformanceTests.Benchmarks
{
    /// <summary>
    /// BenchmarkDotNet benchmarks for TemporalSegmentedArray{T}.
    ///
    /// Scenarios covered:
    /// - Adds: AddValue, Add(pre-built), AddRange(unsorted), AddSorted(sorted)
    /// - Queries (ITimeQueryable): GetInRange, GetBefore, GetAfter, CountInRange, CountSince, GetNearest,
    ///   GetLatest/GetEarliest/GetTimeSpan
    /// - Maintenance: RemoveOlderThan, RemoveRange, Clear, ToArray, TrimExcess
    ///
    /// Notes:
    /// - Fresh instances are prepared per iteration to avoid cross-run interference.
    /// - Time-window queries use windows relative to now; dataset is loaded during IterationSetup.
    /// - Pre-built items use synthetic but strictly increasing timestamps for deterministic costs.
    /// </summary>
    [MemoryDiagnoser]
    public class TemporalSegmentedArrayBenchmarks
    {
        // ---------- Parameters ----------

        /// <summary>Total number of items to insert for the test datasets.</summary>
        [Params(50_000)]
        public int ItemCount { get; set; }

        /// <summary>Per-segment capacity used by TemporalSegmentedArray.</summary>
        [Params(256, 1024)]
        public int SegmentCapacity { get; set; }

        // ---------- Prepared data ----------

        private TemporalItem<int>[] _prebuiltSorted = default!;
        private TemporalItem<int>[] _prebuiltShuffled = default!;

        // ---------- SUTs (fresh per iteration) ----------

        private TemporalSegmentedArray<int> _arrForAdds = default!;
        private TemporalSegmentedArray<int> _arrForQueries = default!;
        private TemporalSegmentedArray<int> _arrForRetention = default!;

        // ---------- Setup ----------

        /// <summary>
        /// Build deterministic pre-built items:
        /// - _prebuiltSorted: strictly increasing timestamps
        /// - _prebuiltShuffled: same items, random order to trigger positional inserts
        /// </summary>
        [GlobalSetup]
        public void GlobalSetup()
        {
            var baseTs = DateTimeOffset.UtcNow.AddMinutes(-60);
            _prebuiltSorted = new TemporalItem<int>[ItemCount];
            for (int i = 0; i < ItemCount; i++)
            {
                // strictly increasing ticks; payload = i
                _prebuiltSorted[i] = new TemporalItem<int>(i, baseTs.AddTicks(i));
            }

            var rng = new Random(42);
            _prebuiltShuffled = _prebuiltSorted.ToArray();
            // Fisher–Yates shuffle
            for (int i = _prebuiltShuffled.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (_prebuiltShuffled[i], _prebuiltShuffled[j]) = (_prebuiltShuffled[j], _prebuiltShuffled[i]);
            }
        }

        /// <summary>
        /// Create fresh arrays per iteration.
        /// _arrForQueries and _arrForRetention are preloaded with sorted data to simulate steady-state workloads.
        /// </summary>
        [IterationSetup]
        public void IterationSetup()
        {
            _arrForAdds = new TemporalSegmentedArray<int>(SegmentCapacity);

            _arrForQueries = new TemporalSegmentedArray<int>(SegmentCapacity);
            _arrForQueries.AddSorted(_prebuiltSorted);

            _arrForRetention = new TemporalSegmentedArray<int>(SegmentCapacity);
            _arrForRetention.AddSorted(_prebuiltSorted);
        }

        // ---------- Add / Insert ----------

        /// <summary>AddValue ItemCount times (monotonic timestamps generated at call-time).</summary>
        [Benchmark(Description = "AddValue: append ItemCount items (monotonic)")]
        public void AddValue_All()
        {
            var arr = _arrForAdds;
            for (int i = 0; i < ItemCount; i++)
            {
                arr.AddValue(i);
            }
        }

        /// <summary>Add pre-built items already carrying timestamps (fast-path append).</summary>
        [Benchmark(Description = "Add(sorted items): pre-built TemporalItem<int> (append path)")]
        public void Add_PreBuilt_Sorted()
        {
            var arr = _arrForAdds;
            for (int i = 0; i < _prebuiltSorted.Length; i++)
            {
                arr.Add(_prebuiltSorted[i]);
            }
        }

        /// <summary>Add pre-built items in random order (forces positional insert / splits).</summary>
        [Benchmark(Description = "Add(shuffled items): positional inserts + potential segment splits")]
        public void Add_PreBuilt_Shuffled()
        {
            var arr = _arrForAdds;
            for (int i = 0; i < _prebuiltShuffled.Length; i++)
            {
                arr.Add(_prebuiltShuffled[i]);
            }
        }

        /// <summary>Bulk add unsorted via AddRange (will insert positionally per item).</summary>
        [Benchmark(Description = "AddRange(unsorted): bulk positional inserts")]
        public void AddRange_Unsorted()
        {
            var arr = _arrForAdds;
            arr.AddRange(_prebuiltShuffled);
        }

        /// <summary>Bulk add sorted via AddSorted (optimized append path across segments).</summary>
        [Benchmark(Description = "AddSorted(sorted): bulk append across segments")]
        public void AddSorted_Sorted()
        {
            var arr = _arrForAdds;
            arr.AddSorted(_prebuiltSorted);
        }

        // ---------- Queries (ITimeQueryable) ----------

        /// <summary>Inclusive range query over the last 2 minutes.</summary>
        [Benchmark(Description = "GetInRange(last 2 minutes)")]
        public void GetInRange_Last2Minutes()
        {
            var to = DateTimeOffset.UtcNow;
            var from = to.AddMinutes(-2);
            var _ = _arrForQueries.GetInRange(from, to);
        }

        /// <summary>Strictly before: cutoff at midpoint of whole dataset time span.</summary>
        [Benchmark(Description = "GetBefore(midpoint of dataset span)")]
        public void GetBefore_Midpoint()
        {
            var first = _prebuiltSorted[0].Timestamp;
            var last = _prebuiltSorted[^1].Timestamp;
            var cutoff = Mid(first, last);
            var _ = _arrForQueries.GetBefore(cutoff);
        }

        /// <summary>Strictly after: cutoff at midpoint of whole dataset time span.</summary>
        [Benchmark(Description = "GetAfter(midpoint of dataset span)")]
        public void GetAfter_Midpoint()
        {
            var first = _prebuiltSorted[0].Timestamp;
            var last = _prebuiltSorted[^1].Timestamp;
            var cutoff = Mid(first, last);
            var _ = _arrForQueries.GetAfter(cutoff);
        }

        /// <summary>Inclusive count in a 2-minute window.</summary>
        [Benchmark(Description = "CountInRange(last 2 minutes)")]
        public int CountInRange_Last2Minutes()
        {
            var to = DateTimeOffset.UtcNow;
            var from = to.AddMinutes(-2);
            return _arrForQueries.CountInRange(from, to);
        }

        /// <summary>Count since (>=) a moving cutoff (~last minute).</summary>
        [Benchmark(Description = "CountSince(last 1 minute)")]
        public int CountSince_Last1Minute()
        {
            var from = DateTimeOffset.UtcNow.AddMinutes(-1);
            return _arrForQueries.CountSince(from);
        }

        /// <summary>Nearest to a timestamp located near the middle of the dataset.</summary>
        [Benchmark(Description = "GetNearest(midpoint of dataset span)")]
        public TemporalItem<int>? GetNearest_Midpoint()
        {
            var first = _prebuiltSorted[0].Timestamp;
            var last = _prebuiltSorted[^1].Timestamp;
            var mid = Mid(first, last);
            return _arrForQueries.GetNearest(mid);
        }

        /// <summary>Fetch extremes and span together.</summary>
        [Benchmark(Description = "GetLatest/GetEarliest/GetTimeSpan")]
        public (TemporalItem<int>? latest, TemporalItem<int>? earliest, TimeSpan span) Extremes_And_Span()
        {
            var latest = _arrForQueries.GetLatest();
            var earliest = _arrForQueries.GetEarliest();
            var span = _arrForQueries.GetTimeSpan();
            return (latest, earliest, span);
        }

        // ---------- Maintenance ----------

        /// <summary>Remove entries strictly older than now-1m (may drop whole segments).</summary>
        [Benchmark(Description = "RemoveOlderThan(now-1m)")]
        public void RemoveOlderThan_1Minute()
        {
            var cutoff = DateTimeOffset.UtcNow.AddMinutes(-1);
            _arrForRetention.RemoveOlderThan(cutoff);
        }

        /// <summary>Remove inclusive range [now-2m .. now-1m] (may drop/trim segments).</summary>
        [Benchmark(Description = "RemoveRange([now-2m .. now-1m])")]
        public void RemoveRange_2m_to_1m()
        {
            var to = DateTimeOffset.UtcNow.AddMinutes(-1);
            var from = DateTimeOffset.UtcNow.AddMinutes(-2);
            _arrForRetention.RemoveRange(from, to);
        }

        /// <summary>Materialize a full snapshot into a flat array.</summary>
        [Benchmark(Description = "ToArray() full snapshot")]
        public TemporalItem<int>[] ToArray_FullSnapshot()
        {
            return _arrForQueries.ToArray();
        }

        /// <summary>Trim internal segment arrays to their actual counts.</summary>
        [Benchmark(Description = "TrimExcess() on preloaded array")]
        public void TrimExcess_OnPreloaded()
        {
            _arrForRetention.TrimExcess();
        }

        /// <summary>Clear the entire structure.</summary>
        [Benchmark(Description = "Clear() on preloaded array")]
        public void Clear_Preloaded()
        {
            _arrForRetention.Clear();
        }

        // ---------- Utility ----------

        private static DateTimeOffset Mid(DateTimeOffset a, DateTimeOffset b)
        {
            long mid = (a.UtcTicks + b.UtcTicks) / 2;
            return new DateTimeOffset(mid, TimeSpan.Zero);
        }
    }
}