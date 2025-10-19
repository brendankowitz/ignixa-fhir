using BenchmarkDotNet.Running;

namespace Ignixa.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        // Run all benchmarks
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

        // Or run specific benchmarks:
        // BenchmarkRunner.Run<SerializationBenchmarks>();
        // BenchmarkRunner.Run<NavigationBenchmarks>();
        // BenchmarkRunner.Run<FhirPathBenchmarks>();
    }
}
