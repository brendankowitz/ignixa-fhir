using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace Ignixa.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        // BenchmarkDotNet 0.15.8 cannot build or launch a .NET 11 child process, so every job runs in-process.
        IConfig config = ManualConfig.Create(DefaultConfig.Instance)
            .AddJob(Job.Default.WithToolchain(InProcessEmitToolchain.Instance).AsMutator());

        // Run all benchmarks
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);

        // Or run specific benchmarks:
        // BenchmarkRunner.Run<SerializationBenchmarks>();
        // BenchmarkRunner.Run<NavigationBenchmarks>();
        // BenchmarkRunner.Run<FhirPathBenchmarks>();
    }
}
