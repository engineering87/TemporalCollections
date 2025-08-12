// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using BenchmarkDotNet.Attributes;
using TemporalCollections.Collections;

namespace TemporalCollections.PerformanceTests.Benchmarks
{
    [MemoryDiagnoser]
    public class TemporalPriorityQueueBenchmarks
    {
        private TemporalPriorityQueue<int, int> _queue;
        private List<int> _testData;
        private List<int> _priorities;

        [GlobalSetup]
        public void Setup()
        {
            _queue = new TemporalPriorityQueue<int, int>();
            _testData = Enumerable.Range(0, 10000).ToList();

            var rand = new Random(42);
            _priorities = _testData.Select(_ => rand.Next(0, 1000)).ToList();
        }

        [Benchmark(Description = "Enqueue 10,000 items")]
        public void AddItems()
        {
            for (int i = 0; i < _testData.Count; i++)
            {
                _queue.Enqueue(_testData[i], _priorities[i]);
            }
        }

        [Benchmark(Description = "Query items in last 10 minutes")]
        public void QueryRange()
        {
            var from = DateTime.UtcNow.AddMinutes(-10);
            var to = DateTime.UtcNow;
            var results = _queue.GetInRange(from, to);
        }

        [Benchmark(Description = "Remove items older than 5 minutes")]
        public void RemoveOlderThan()
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-5);
            _queue.RemoveOlderThan(cutoff);
        }
    }
}
