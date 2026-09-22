using System.Diagnostics;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace Ignixa.Tests.Compatibility.CLI.Tests;

public class CompatibilityCliExitTests
{
    [Fact]
    public async Task GivenCancellationDuringExecution_WhenInvokingCli_ThenCannotReturnSuccess()
    {
        var output = Path.Combine(AppContext.BaseDirectory, $"cancelled-{Guid.NewGuid():N}.json");
        var previousUrl = Environment.GetEnvironmentVariable("TestEnvironmentUrl_R4_Sql");
        using var cancellation = new CancellationTokenSource();
        var invocation = Task.Run(() => Program.InvokeAsync(
            ["--url", "http://127.0.0.1:1", "--output", output, "--filter", "CancellationSqlServerJson"],
            cancellation.Token));
        try
        {
            await CompatibilityContractFixtures.CancellationContractTests.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

            await cancellation.CancelAsync();
            CompatibilityContractFixtures.CancellationContractTests.Finish.TrySetResult();
            var exitCode = await invocation.WaitAsync(TimeSpan.FromSeconds(10));

            exitCode.ShouldBe(130);
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(output));
            report.RootElement.GetProperty("Cancelled").GetBoolean().ShouldBeTrue();
        }
        finally
        {
            CompatibilityContractFixtures.CancellationContractTests.Finish.TrySetResult();
            await invocation.WaitAsync(TimeSpan.FromSeconds(10));
            Environment.SetEnvironmentVariable("TestEnvironmentUrl_R4_Sql", previousUrl);
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }

    [Fact]
    public async Task GivenFixtureCleanupFailure_WhenRunningCli_ThenReturnsNonzeroDespitePassingTest()
    {
        var result = await RunCliAsync(includeAssembly: true, filter: "PassingBeforeCleanupSqlServerJson");

        result.Passed.ShouldBe(1, result.Output);
        result.ExitCode.ShouldNotBe(0, result.Output);
        result.Output.ShouldContain("Synthetic fixture cleanup failure.");
    }

    [Fact]
    public async Task GivenMissingAssembly_WhenRunningCli_ThenReturnsNonzero()
    {
        var result = await RunCliAsync(includeAssembly: false, filter: "PassingSqlServerJson");

        result.ExitCode.ShouldNotBe(0, result.Output);
        result.Output.ShouldContain("assembly not found");
    }

    [Fact]
    public async Task GivenNoMatchingTests_WhenRunningCli_ThenReturnsNonzeroAndWritesEmptyReport()
    {
        var result = await RunCliAsync(includeAssembly: true, filter: "NoSuchContract");

        result.ExitCode.ShouldNotBe(0, result.Output);
        result.Total.ShouldBe(0);
    }

    [Fact]
    public async Task GivenFailedRequiredTest_WhenRunningCli_ThenReturnsNonzeroAndReportsFailure()
    {
        var result = await RunCliAsync(includeAssembly: true, filter: "FailingRequiredSqlServerJson");

        result.ExitCode.ShouldNotBe(0, result.Output);
        result.Failed.ShouldBe(1);
    }

    [Fact]
    public async Task GivenOnlySkippedTests_WhenRunningCli_ThenReturnsNonzero()
    {
        var result = await RunCliAsync(includeAssembly: true, filter: "SkippedSqlServerJson");

        result.ExitCode.ShouldNotBe(0, result.Output);
        result.Skipped.ShouldBe(1);
        result.Passed.ShouldBe(0);
    }

    [Fact]
    public async Task GivenPassingAndExplicitlySkippedTests_WhenRunningCli_ThenReturnsZero()
    {
        var result = await RunCliAsync(includeAssembly: true, filter: "PassingSqlServerJson,SkippedSqlServerJson");

        result.ExitCode.ShouldBe(0, result.Output);
        result.Passed.ShouldBe(1);
        result.Skipped.ShouldBe(1);
        result.Total.ShouldBe(2);
    }

    [Fact]
    public async Task GivenDefaultExcludedCategory_WhenRunningCli_ThenDoesNotRunThatCategory()
    {
        var result = await RunCliAsync(includeAssembly: true, filter: "PassingSqlServerJson,ImportSqlServerJson");

        result.ExitCode.ShouldBe(0, result.Output);
        result.Passed.ShouldBe(1);
        result.Failed.ShouldBe(0);
        result.Total.ShouldBe(1);
    }

    [Fact]
    public async Task GivenExplicitUnsupportedCategory_WhenRunningCli_ThenRunsOnlySupportedSelection()
    {
        var result = await RunCliAsync(includeAssembly: true,
            filter: "PassingSqlServerJson,FailingRequiredSqlServerJson",
            skipCategories: "FailingRequired");

        result.ExitCode.ShouldBe(0, result.Output);
        result.Passed.ShouldBe(1);
        result.Failed.ShouldBe(0);
        result.Total.ShouldBe(1);
    }

    private static async Task<CliResult> RunCliAsync(bool includeAssembly, string filter, string skipCategories = null)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "cli-contract-runs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory))
            {
                var name = Path.GetFileName(path);
                if (!includeAssembly && name == "Microsoft.Health.Fhir.R4.Tests.E2E.dll")
                {
                    continue;
                }
                File.Copy(path, Path.Combine(directory, name));
            }

            var executable = Path.Combine(directory,
                OperatingSystem.IsWindows() ? "Ignixa.Tests.Compatibility.CLI.Tests.exe" : "Ignixa.Tests.Compatibility.CLI.Tests");
            var outputPath = Path.Combine(directory, "report.json");
            var startInfo = new ProcessStartInfo(executable)
            {
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in new[] { "--url", "http://127.0.0.1:1", "--output", outputPath, "--filter", filter })
            {
                startInfo.ArgumentList.Add(argument);
            }
            if (skipCategories is not null)
            {
                startInfo.ArgumentList.Add("--skip");
                startInfo.ArgumentList.Add(skipCategories);
            }

            using var process = Process.Start(startInfo).ShouldNotBeNull();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
                throw;
            }

            var output = await stdout + await stderr;
            if (!File.Exists(outputPath))
            {
                return new CliResult(process.ExitCode, output, null, null, null, null);
            }
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
            var root = report.RootElement;
            return new CliResult(process.ExitCode, output,
                root.GetProperty("TotalTests").GetInt32(),
                root.GetProperty("Passed").GetInt32(),
                root.GetProperty("Failed").GetInt32(),
                root.GetProperty("Skipped").GetInt32());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed record CliResult(int ExitCode, string Output, int? Total, int? Passed, int? Failed, int? Skipped);
}
