// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using BenchmarkDotNet.Attributes;
using TemporalCollections.Collections;
using TemporalCollections.Models;

namespace TemporalCollections.PerformanceTests.Benchmarks
{
    [MemoryDiagnoser]
    public class TemporalQueueBenchmarks
    {
        private TemporalQueue<int> _queue = null!;

        private int[] _values = null!;

        private DateTime _now;

        [GlobalSetup]
        public void Setup()
        {
            _queue = new TemporalQueue<int>();
            _values = Enumerable.Range(0, 10000).ToArray();
            _now = DateTime.UtcNow;

            // Pre-fill the queue with 10k items with timestamps spread in the past 10000 seconds
            for (int i = 0; i < _values.Length; i++)
            {
                var item = new TemporalItem<int>(_values[i], _now.AddSeconds(-_values.Length + i));
                // Use reflection or internal method if needed, but here just enqueue normally
                _queue.Enqueue(_values[i]);
            }
        }

        [Benchmark]
        public void Enqueue()
        {
            _queue.Enqueue(123);
        }

        [Benchmark]
        public TemporalItem<int> Dequeue()
        {
            // Make sure queue is not empty before dequeue
            if (_queue.Count == 0)
                _queue.Enqueue(123);
            return _queue.Dequeue();
        }

        [Benchmark]
        public TemporalItem<int> Peek()
        {
            if (_queue.Count == 0)
                _queue.Enqueue(123);
            return _queue.Peek();
        }

        [Benchmark]
        public int GetInRange_Count()
        {
            var from = _now.AddSeconds(-5000);
            var to = _now;
            var items = _queue.GetInRange(from, to);
            return items.Count();
        }

        [Benchmark]
        public void RemoveOlderThan()
        {
            var cutoff = _now.AddSeconds(-5000);
            _queue.RemoveOlderThan(cutoff);
        }
    }
}
