// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using System.Globalization;
using TemporalCollections.PerformanceTests.Benchmarks;

class Program
{
    static void Main(string[] args)
    {
        bool fullRun = args.Contains("--full", StringComparer.OrdinalIgnoreCase);

        // Fast job: fewer warmup/measurement iterations, single launch.
        // Use --full to run the BenchmarkDotNet default (more accurate but much slower) job.
        var job = fullRun
            ? Job.Default
            : Job.Default
                .WithLaunchCount(1)
                .WithWarmupCount(3)
                .WithIterationCount(10)
                .WithId("Fast");

        var config = ManualConfig.Create(DefaultConfig.Instance)
            .AddJob(job.AsDefault())
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
            typeof(TemporalCacheBenchmarks),
        };

        // Run all types in a single invocation so the benchmark project is generated and built only once.
        var allSummaries = BenchmarkRunner.Run(benchmarkTypes.ToArray(), config).ToList();

        PrintCombinedSummaryTable(allSummaries);

        Console.WriteLine("All benchmarks completed.");
    }

    static void PrintCombinedSummaryTable(List<Summary> summaries)
    {
        Console.WriteLine();
        Console.WriteLine("Combined Benchmark Results Summary:");
        Console.WriteLine();
        Console.WriteLine("| Structure | Method | Params | Mean (µs) | StdDev (µs) | Min (µs) | Max (µs) | Ops/sec |");
        Console.WriteLine("|---|---|---|---:|---:|---:|---:|---:|");

        var inv = CultureInfo.InvariantCulture;

        foreach (var summary in summaries)
        {
            if (summary == null) continue;

            foreach (var report in summary.Reports)
            {
                var stats = report.ResultStatistics;
                if (stats == null) continue;

                var descriptor = report.BenchmarkCase.Descriptor;
                string structureName = descriptor.Type.Name.Replace("Benchmarks", string.Empty);
                string method = descriptor.WorkloadMethod.Name;
                string parameters = report.BenchmarkCase.HasParameters
                    ? string.Join(", ", report.BenchmarkCase.Parameters.Items.Select(p => $"{p.Name}={p.Value}"))
                    : "-";

                // ns to µs
                string mean = (stats.Mean / 1_000).ToString("N3", inv);
                string stddev = (stats.StandardDeviation / 1_000).ToString("N3", inv);
                string min = (stats.Min / 1_000).ToString("N3", inv);
                string max = (stats.Max / 1_000).ToString("N3", inv);
                string opsPerSec = (1_000_000_000 / stats.Mean).ToString("N0", inv);

                Console.WriteLine($"| {structureName} | {method} | {parameters} | {mean} | {stddev} | {min} | {max} | {opsPerSec} |");
            }
        }

        Console.WriteLine();
    }
}