using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

namespace Ignixa.Benchmarks.Firely5;

public static class Program
{
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "corpus")
        {
            PrintCorpusSummary();
            return;
        }

        var quick = args.Length > 0 && args[0] == "quick";
        var benchmarkArgs = quick ? args[1..] : args;
        Job job = (quick ? Job.ShortRun : Job.Default)
            .WithRuntime(CoreRuntime.Core10_0)
            .WithId(quick ? "ShortRun" : "Full");
        IConfig config = ManualConfig.Create(DefaultConfig.Instance).AddJob(job);

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(benchmarkArgs, config);
    }

    /// <summary>
    /// Prints the corpus shape without running a measurement, so the benchmark numbers can be read
    /// against how many expressions they cover.
    /// </summary>
    private static void PrintCorpusSummary()
    {
        var benchmarks = new IndexingHeadToHeadBenchmarks();
        benchmarks.Setup();
        SearchParameterExpressionCorpusSummary summary = benchmarks.Summary;

        Console.WriteLine($"FHIR versions                             : {summary.FhirVersions}");
        Console.WriteLine($"Shipped search parameters                 : {summary.ShippedSearchParameters}");
        Console.WriteLine($"Distinct search parameter expressions     : {summary.DistinctExpressions}");
        Console.WriteLine($"Distinct expressions compiled by both     : {summary.CommonExpressions}");
        Console.WriteLine($"Ignixa compile failures across versions   : {summary.IgnixaCompileFailures}");
        Console.WriteLine($"Firely 5.11.4 failures across versions    : {summary.FirelyCompileFailures}");

        foreach (var (family, count) in summary.EvaluationCounts)
        {
            Console.WriteLine($"{family,-28} evaluations : {count}");
        }
    }
}
