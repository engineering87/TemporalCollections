// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using BenchmarkDotNet.Running;

namespace TemporalCollections.PerformanceTests.Benchmarks
{
    public class AllBenchmarks
    {
        public static void RunAll()
        {
            BenchmarkRunner.Run<TemporalSetBenchmarks>();
            BenchmarkRunner.Run<TemporalDictionaryBenchmarks>();
            BenchmarkRunner.Run<TemporalIntervalTreeBenchmarks>();
            BenchmarkRunner.Run<TemporalPriorityQueueBenchmarks>();
            BenchmarkRunner.Run<TemporalSortedListBenchmarks>();
            BenchmarkRunner.Run<TemporalQueueBenchmarks>();
            BenchmarkRunner.Run<TemporalStackBenchmarks>();
            BenchmarkRunner.Run<TemporalSlidingWindowSetBenchmarks>();
            BenchmarkRunner.Run<TemporalCircularBufferBenchmarks>();
            BenchmarkRunner.Run<TemporalSegmentedArrayBenchmarks>();
            BenchmarkRunner.Run<TemporalMultimapBenchmarks>();
        }
    }
}
