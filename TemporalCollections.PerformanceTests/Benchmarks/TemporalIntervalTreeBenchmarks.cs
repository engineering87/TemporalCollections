// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using BenchmarkDotNet.Attributes;
using TemporalCollections.Collections;

namespace TemporalCollections.PerformanceTests.Benchmarks
{
    [MemoryDiagnoser]
    public class TemporalIntervalTreeBenchmarks
    {
        private TemporalIntervalTree<int> _tree;
        private List<int> _testData;
        private List<(DateTime start, DateTime end)> _intervals;

        [GlobalSetup]
        public void Setup()
        {
            _tree = new TemporalIntervalTree<int>();
            _testData = Enumerable.Range(0, 10000).ToList();

            var now = DateTime.UtcNow;
            _intervals = new List<(DateTime, DateTime)>(_testData.Count);

            for (int i = 0; i < _testData.Count; i++)
            {
                var start = now.AddMinutes(-i);
                var end = start.AddMinutes(5);
                _intervals.Add((start, end));
            }
        }

        [Benchmark(Description = "Insert 10,000 intervals")]
        public void AddItems()
        {
            for (int i = 0; i < _testData.Count; i++)
            {
                _tree.Insert(_intervals[i].start, _intervals[i].end, _testData[i]);
            }
        }

        [Benchmark(Description = "Query intervals overlapping last 10 minutes")]
        public void QueryRange()
        {
            var from = DateTime.UtcNow.AddMinutes(-10);
            var to = DateTime.UtcNow;
            var results = _tree.GetInRange(from, to);
        }

        [Benchmark(Description = "Remove intervals older than 5 minutes")]
        public void RemoveOlderThan()
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-5);
            _tree.RemoveOlderThan(cutoff);
        }
    }
}
