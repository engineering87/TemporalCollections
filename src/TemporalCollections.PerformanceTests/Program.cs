// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using TemporalCollections.PerformanceTests.Benchmarks;

class Program
{
    static void Main(string[] args)
    {
        var config = ManualConfig.Create(DefaultConfig.Instance)
            .AddLogger(ConsoleLogger.Default)
            .AddColumn(TargetMethodColumn.Method)
            .AddColumn(StatisticColumn.Mean)
            .AddColumn(StatisticColumn.StdDev)
            .AddColumn(StatisticColumn.Min)
            .AddColumn(StatisticColumn.Max)
            .AddColumn(StatisticColumn.OperationsPerSecond)
            .AddDiagnoser(MemoryDiagnoser.Default)
            .WithOrderer(new DefaultOrderer(SummaryOrderPolicy.FastestToSlowest));

        Console.WriteLine("Starting benchmarks for all structures...\n");

        var benchmarkTypes = new List<Type>
        {
            typeof(TemporalSetBenchmarks),
            typeof(TemporalDictionaryBenchmarks),
            typeof(TemporalIntervalTreeBenchmarks),
            typeof(TemporalPriorityQueueBenchmarks),
            typeof(TemporalSortedListBenchmarks),
            typeof(TemporalQueueBenchmarks),
            typeof(TemporalStackBenchmarks),
            typeof(TemporalSlidingWindowSetBenchmarks),
            typeof(TemporalCircularBufferBenchmarks),
            typeof(TemporalMultimapBenchmarks),
            typeof(TemporalSegmentedArrayBenchmarks),
        };

        var allSummaries = new List<Summary>();

        foreach (var type in benchmarkTypes)
        {
            Console.WriteLine($"Running benchmarks for {type.Name}...");
            var summary = BenchmarkRunner.Run(type, config);
            allSummaries.Add(summary);
            Console.WriteLine();
        }

        PrintCombinedSummaryTable(allSummaries);

        Console.WriteLine("All benchmarks completed.");
    }

    static void PrintCombinedSummaryTable(List<Summary> summaries)
    {
        Console.WriteLine();
        Console.WriteLine("Combined Benchmark Results Summary:");
        Console.WriteLine(new string('-', 100));
        Console.WriteLine($"| {"Structure",-25} | {"Method",-30} | {"Mean (ms)",10} | {"StdDev (ms)",12} | {"Min (ms)",10} | {"Max (ms)",10} | {"Ops/sec",10} |");
        Console.WriteLine(new string('-', 100));

        foreach (var summary in summaries)
        {
            int reportsCount = 0;
            if (summary != null)
            {
                reportsCount = summary.Reports.Count();
            }

            string structureName = reportsCount > 0
                ? summary.Reports.First().BenchmarkCase.Descriptor.Type.Name
                : "Unknown";

            foreach (var report in summary.Reports)
            {
                var stats = report.ResultStatistics;
                if (stats == null) continue;

                string method = report.BenchmarkCase.Descriptor.WorkloadMethod.Name;
                string mean = (stats.Mean / 1_000_000).ToString("F4"); // ns to ms
                string stddev = (stats.StandardDeviation / 1_000_000).ToString("F4");
                string min = (stats.Min / 1_000_000).ToString("F4");
                string max = (stats.Max / 1_000_000).ToString("F4");
                string opsPerSec = (1_000_000_000 / stats.Mean).ToString("F0");

                Console.WriteLine($"| {structureName,-25} | {method,-30} | {mean,10} | {stddev,12} | {min,10} | {max,10} | {opsPerSec,10} |");
            }
        }

        Console.WriteLine(new string('-', 100));
    }
}