// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using BenchmarkDotNet.Attributes;
using TemporalCollections.Collections;

namespace TemporalCollections.PerformanceTests.Benchmarks
{
    [MemoryDiagnoser]
    public class TemporalCircularBufferBenchmarks
    {
        private TemporalCircularBuffer<int> _buffer;
        private List<int> _testData;

        [GlobalSetup]
        public void Setup()
        {
            _buffer = new TemporalCircularBuffer<int>(capacity: 20000);
            _testData = Enumerable.Range(0, 10000).ToList();
        }

        [Benchmark(Description = "Add 10,000 items")]
        public void AddItems()
        {
            foreach (var item in _testData)
            {
                _buffer.Add(item);
            }
        }

        [Benchmark(Description = "Query items in last 10 minutes")]
        public void QueryRange()
        {
            var from = DateTime.UtcNow.AddMinutes(-10);
            var to = DateTime.UtcNow;
            var results = _buffer.GetInRange(from, to);
        }

        [Benchmark(Description = "Remove expired items older than 5 minutes")]
        public void RemoveOlderThan()
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-5);
            _buffer.RemoveOlderThan(cutoff);
        }
    }
}
