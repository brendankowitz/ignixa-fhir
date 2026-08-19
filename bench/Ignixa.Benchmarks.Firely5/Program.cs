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

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
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

        Console.WriteLine($"Distinct R4 search parameter expressions : {summary.DistinctExpressions}");
        Console.WriteLine($"Compiled by both engines                 : {summary.CommonExpressions}");
        Console.WriteLine($"Ignixa compile failures                  : {summary.IgnixaCompileFailures}");
        Console.WriteLine($"Firely 5.11.4 compile failures           : {summary.FirelyCompileFailures}");
        Console.WriteLine($"Patient expressions evaluated            : {summary.PatientExpressions}");
        Console.WriteLine($"Observation expressions evaluated        : {summary.ObservationExpressions}");
    }
}
