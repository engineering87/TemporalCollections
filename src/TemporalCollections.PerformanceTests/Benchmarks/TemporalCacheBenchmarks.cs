// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using BenchmarkDotNet.Attributes;
using TemporalCollections.Collections;

namespace TemporalCollections.PerformanceTests.Benchmarks
{
    [MemoryDiagnoser]
    public class TemporalCacheBenchmarks
    {
        private TemporalCache<int, int> _cache;
        private TemporalCache<int, int> _slidingCache;
        private List<int> _testData;

        [GlobalSetup]
        public void Setup()
        {
            _cache = new TemporalCache<int, int>(TimeSpan.FromMinutes(10));
            _slidingCache = new TemporalCache<int, int>(TimeSpan.FromMinutes(10), slidingExpiration: true);
            _testData = Enumerable.Range(0, 5000).ToList();

            foreach (var item in _testData)
            {
                _cache.Set(item, item);
                _slidingCache.Set(item, item);
            }
        }

        [Benchmark(Description = "Set 5,000 items")]
        public void SetItems()
        {
            foreach (var item in _testData)
            {
                _cache.Set(item, item);
            }
        }

        [Benchmark(Description = "TryGetValue 5,000 items (absolute)")]
        public int TryGetValueAbsolute()
        {
            int sum = 0;
            foreach (var item in _testData)
            {
                if (_cache.TryGetValue(item, out var v)) sum += v;
            }
            return sum;
        }

        [Benchmark(Description = "TryGetValue 5,000 items (sliding)")]
        public int TryGetValueSliding()
        {
            int sum = 0;
            foreach (var item in _testData)
            {
                if (_slidingCache.TryGetValue(item, out var v)) sum += v;
            }
            return sum;
        }

        [Benchmark(Description = "GetOrAdd 5,000 items")]
        public int GetOrAddItems()
        {
            int sum = 0;
            foreach (var item in _testData)
            {
                sum += _cache.GetOrAdd(item, k => k);
            }
            return sum;
        }

        [Benchmark(Description = "Query items in last 10 minutes")]
        public int QueryRange()
        {
            var from = DateTime.UtcNow.AddMinutes(-10);
            var to = DateTime.UtcNow;
            return _cache.GetInRange(from, to).Count();
        }

        [Benchmark(Description = "RemoveExpired scan")]
        public int RemoveExpired()
        {
            return _cache.RemoveExpired();
        }
    }
}
