// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using BenchmarkDotNet.Attributes;
using TemporalCollections.Collections;
using TemporalCollections.Models;

namespace TemporalCollections.PerformanceTests.Benchmarks
{
    /// <summary>
    /// BenchmarkDotNet benchmarks for TemporalMultimap<TKey, TValue>.
    ///
    /// Scenarios covered:
    /// - Adds: AddValue, Add (pre-built items), AddRange(values), AddRange(items)
    /// - Per-key queries: GetValuesInRange
    /// - Global ITimeQueryable queries: GetInRange, GetBefore, GetAfter, CountInRange, CountSince, GetNearest
    /// - Extremes & span: GetLatest/GetEarliest/GetTimeSpan
    /// - Retention: RemoveOlderThan (per-key & global), RemoveRange (per-key & global), RemoveKey, Clear
    ///
    /// Notes:
    /// - We build fresh instances in IterationSetup to avoid cross-benchmark interference.
    /// - Time-window queries use "last N minutes/seconds" relative to now (creation time is during setup).
    /// </summary>
    [MemoryDiagnoser]
    public class TemporalMultimapBenchmarks
    {
        // ---------- Parameters ----------

        /// <summary>Number of distinct keys in datasets.</summary>
        [Params(10, 100)]
        public int KeyCount { get; set; }

        /// <summary>Number of values per key.</summary>
        [Params(1_000)]
        public int ValuesPerKey { get; set; }

        // ---------- Prepared data ----------

        private string[] _keys = default!;
        private (string Key, int Value)[] _kvData = default!;
        private TemporalItem<KeyValuePair<string, int>>[] _prebuiltItems = default!;

        // ---------- Maps per scenario (fresh each iteration) ----------

        private TemporalMultimap<string, int> _mapForAdds = default!;
        private TemporalMultimap<string, int> _mapForQueries = default!;
        private TemporalMultimap<string, int> _mapForPerKeyRetention = default!;
        private TemporalMultimap<string, int> _mapForGlobalRetention = default!;

        // ---------- Setup ----------

        /// <summary>
        /// Prepare deterministic keys and data layouts that are reused across iterations.
        /// Also prepares a set of pre-built TemporalItems for benchmarking Add(items)/AddRange(items).
        /// </summary>
        [GlobalSetup]
        public void GlobalSetup()
        {
            _keys = Enumerable.Range(0, KeyCount).Select(i => $"K{i}").ToArray();

            // Flattened (key,value) array
            _kvData = new (string, int)[KeyCount * ValuesPerKey];
            int p = 0;
            for (int i = 0; i < KeyCount; i++)
                for (int v = 0; v < ValuesPerKey; v++)
                    _kvData[p++] = (_keys[i], v);

            // Prebuild TemporalItems with strictly increasing timestamps
            // (use a base time and tick increments to avoid Create() during the benchmark body)
            var baseTime = DateTimeOffset.UtcNow.AddMinutes(-30);
            _prebuiltItems = new TemporalItem<KeyValuePair<string, int>>[_kvData.Length];
            long tick = 0;
            for (int i = 0; i < _kvData.Length; i++)
            {
                var (k, v) = _kvData[i];
                var ts = baseTime.AddTicks(tick++);
                _prebuiltItems[i] = new TemporalItem<KeyValuePair<string, int>>(new KeyValuePair<string, int>(k, v), ts);
            }
        }

        /// <summary>
        /// Build fresh maps for each iteration and load the query/retention datasets.
        /// </summary>
        [IterationSetup]
        public void IterationSetup()
        {
            _mapForAdds = new TemporalMultimap<string, int>();

            _mapForQueries = new TemporalMultimap<string, int>();
            _mapForPerKeyRetention = new TemporalMultimap<string, int>();
            _mapForGlobalRetention = new TemporalMultimap<string, int>();

            // Preload maps that are read/modified by query/retention benchmarks
            for (int i = 0; i < _kvData.Length; i++)
            {
                var (k, v) = _kvData[i];
                _mapForQueries.AddValue(k, v);
                _mapForPerKeyRetention.AddValue(k, v);
                _mapForGlobalRetention.AddValue(k, v);
            }
        }

        // ---------- Adds ----------

        /// <summary>Bulk insert all (key,value) pairs into an empty map via AddValue.</summary>
        [Benchmark(Description = "AddValue: insert N×M items across keys")]
        public void Add_AllItems_AddValue()
        {
            var map = _mapForAdds;
            for (int i = 0; i < _kvData.Length; i++)
            {
                var (k, v) = _kvData[i];
                map.AddValue(k, v);
            }
        }

        /// <summary>Bulk insert using pre-built TemporalItem&lt;KeyValuePair&lt;string,int&gt;&gt; via Add(item).</summary>
        [Benchmark(Description = "Add(item): insert pre-built temporal items")]
        public void Add_AllItems_PreBuilt()
        {
            var map = _mapForAdds;
            for (int i = 0; i < _prebuiltItems.Length; i++)
            {
                map.Add(_prebuiltItems[i]);
            }
        }

        /// <summary>Bulk insert per key using AddRange(values).</summary>
        [Benchmark(Description = "AddRange(values): insert per-key batches")]
        public void AddRange_Values()
        {
            var map = _mapForAdds;
            foreach (var k in _keys)
            {
                // Reuse a slice [0..ValuesPerKey) for simplicity
                map.AddRange(k, Enumerable.Range(0, ValuesPerKey));
            }
        }

        /// <summary>Bulk insert using AddRange(items) with pre-built items.</summary>
        [Benchmark(Description = "AddRange(items): insert pre-built temporal items")]
        public void AddRange_Items()
        {
            var map = _mapForAdds;
            map.AddRange(_prebuiltItems);
        }

        // ---------- Per-key query ----------

        /// <summary>Per-key inclusive range query over the last 2 minutes.</summary>
        [Benchmark(Description = "Per-key query: GetValuesInRange(last 2 minutes)")]
        public void PerKey_GetValuesInRange_Last2Minutes()
        {
            string key = _keys[_keys.Length / 2];
            var to = DateTimeOffset.UtcNow;
            var from = to.AddMinutes(-2);
            var _ = _mapForQueries.GetValuesInRange(key, from, to);
        }

        // ---------- Global queries (ITimeQueryable) ----------

        /// <summary>Global inclusive range query (last 2 minutes).</summary>
        [Benchmark(Description = "Global query: GetInRange(last 2 minutes)")]
        public void Global_GetInRange_Last2Minutes()
        {
            var to = DateTimeOffset.UtcNow;
            var from = to.AddMinutes(-2);
            var _ = _mapForQueries.GetInRange(from, to);
        }

        /// <summary>Global strictly-before query using a midpoint cutoff.</summary>
        [Benchmark(Description = "Global query: GetBefore(midpoint cutoff)")]
        public void Global_GetBefore_Midpoint()
        {
            // Use two known items to craft a midpoint
            var to = DateTimeOffset.UtcNow;
            var from = to.AddMinutes(-5);
            var window = _mapForQueries.GetInRange(from, to).ToArray();
            if (window.Length < 2) return;
            var cutoff = Mid(window[0].Timestamp, window[^1].Timestamp);
            var _ = _mapForQueries.GetBefore(cutoff);
        }

        /// <summary>Global strictly-after query using a midpoint cutoff.</summary>
        [Benchmark(Description = "Global query: GetAfter(midpoint cutoff)")]
        public void Global_GetAfter_Midpoint()
        {
            var to = DateTimeOffset.UtcNow;
            var from = to.AddMinutes(-5);
            var window = _mapForQueries.GetInRange(from, to).ToArray();
            if (window.Length < 2) return;
            var cutoff = Mid(window[0].Timestamp, window[^1].Timestamp);
            var _ = _mapForQueries.GetAfter(cutoff);
        }

        /// <summary>Global inclusive count in a 2-minute window.</summary>
        [Benchmark(Description = "Global query: CountInRange(last 2 minutes)")]
        public int Global_CountInRange_Last2Minutes()
        {
            var to = DateTimeOffset.UtcNow;
            var from = to.AddMinutes(-2);
            return _mapForQueries.CountInRange(from, to);
        }

        /// <summary>Global count since (>=) a moving cutoff (~last minute).</summary>
        [Benchmark(Description = "Global query: CountSince(last 1 minute)")]
        public int Global_CountSince_Last1Minute()
        {
            var from = DateTimeOffset.UtcNow.AddMinutes(-1);
            return _mapForQueries.CountSince(from);
        }

        /// <summary>Global nearest-to-time (use midpoint of a recent window).</summary>
        [Benchmark(Description = "Global query: GetNearest(midpoint)")]
        public TemporalItem<KeyValuePair<string, int>>? Global_GetNearest_Midpoint()
        {
            var to = DateTimeOffset.UtcNow;
            var from = to.AddMinutes(-5);
            var window = _mapForQueries.GetInRange(from, to).ToArray();
            if (window.Length < 2) return null;
            var mid = Mid(window[0].Timestamp, window[^1].Timestamp);
            return _mapForQueries.GetNearest(mid);
        }

        /// <summary>Fetch extremes and span in a single call group.</summary>
        [Benchmark(Description = "Global query: GetLatest/GetEarliest/GetTimeSpan")]
        public (TemporalItem<KeyValuePair<string, int>>? latest,
                TemporalItem<KeyValuePair<string, int>>? earliest,
                TimeSpan span) Global_Extremes_And_Span()
        {
            var latest = _mapForQueries.GetLatest();
            var earliest = _mapForQueries.GetEarliest();
            var span = _mapForQueries.GetTimeSpan();
            return (latest, earliest, span);
        }

        // ---------- Retention ----------

        /// <summary>Per-key RemoveOlderThan with cutoff = now - 1 minute.</summary>
        [Benchmark(Description = "Per-key retention: RemoveOlderThan(key, now-1m)")]
        public void PerKey_RemoveOlderThan()
        {
            string key = _keys[_keys.Length / 2];
            var cutoff = DateTimeOffset.UtcNow.AddMinutes(-1);
            _mapForPerKeyRetention.RemoveOlderThan(key, cutoff);
        }

        /// <summary>Per-key RemoveRange over [now-90s .. now-30s].</summary>
        [Benchmark(Description = "Per-key retention: RemoveRange(key, [now-90s..now-30s])")]
        public void PerKey_RemoveRange()
        {
            string key = _keys[_keys.Length / 2];
            var to = DateTimeOffset.UtcNow.AddSeconds(-30);
            var from = DateTimeOffset.UtcNow.AddSeconds(-90);
            _mapForPerKeyRetention.RemoveRange(key, from, to);
        }

        /// <summary>RemoveKey for a middle key.</summary>
        [Benchmark(Description = "Per-key retention: RemoveKey(middle key)")]
        public void PerKey_RemoveKey()
        {
            string key = _keys[_keys.Length / 2];
            _mapForPerKeyRetention.RemoveKey(key);
        }

        /// <summary>Global RemoveOlderThan with cutoff = now - 1 minute.</summary>
        [Benchmark(Description = "Global retention: RemoveOlderThan(now-1m)")]
        public void Global_RemoveOlderThan()
        {
            var cutoff = DateTimeOffset.UtcNow.AddMinutes(-1);
            _mapForGlobalRetention.RemoveOlderThan(cutoff);
        }

        /// <summary>Global RemoveRange over [now-2m .. now-1m].</summary>
        [Benchmark(Description = "Global retention: RemoveRange([now-2m..now-1m])")]
        public void Global_RemoveRange()
        {
            var to = DateTimeOffset.UtcNow.AddMinutes(-1);
            var from = DateTimeOffset.UtcNow.AddMinutes(-2);
            _mapForGlobalRetention.RemoveRange(from, to);
        }

        /// <summary>Global Clear of map.</summary>
        [Benchmark(Description = "Global retention: Clear()")]
        public void Global_Clear()
        {
            _mapForGlobalRetention.Clear();
        }

        // ---------- Utility ----------

        private static DateTimeOffset Mid(DateTimeOffset a, DateTimeOffset b)
        {
            long mid = (a.UtcTicks + b.UtcTicks) / 2;
            return new DateTimeOffset(mid, TimeSpan.Zero);
        }
    }
}