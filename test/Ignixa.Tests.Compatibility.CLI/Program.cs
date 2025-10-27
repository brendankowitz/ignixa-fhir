using System;
using System.CommandLine;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Xunit.Runners;
using Task = System.Threading.Tasks.Task;

namespace Ignixa.Tests.Compatibility.CLI;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var urlOption = new Option<string>(
            name: "--url",
            description: "FHIR server base URL",
            getDefaultValue: () => "http://localhost:5000");

        var outputOption = new Option<string>(
            name: "--output",
            description: "Output JSON report file path",
            getDefaultValue: () => "compatibility-report.json");

        var filterOption = new Option<string>(
            name: "--filter",
            description: "Filter test names (e.g., 'CreateTests' or 'Metadata')",
            getDefaultValue: () => string.Empty);

        var rootCommand = new RootCommand("FHIR Compatibility Test Tool - Runs Microsoft.Health.Fhir.R4.Tests.E2E against target server")
        {
            urlOption,
            outputOption,
            filterOption
        };

        rootCommand.SetHandler(async (url, output, filter) =>
        {
            await RunCompatibilityTests(url, output, filter);
        }, urlOption, outputOption, filterOption);

        return await rootCommand.InvokeAsync(args);
    }

    static async Task RunCompatibilityTests(string baseUrl, string outputPath, string testFilter)
    {
        Console.WriteLine("=== FHIR Compatibility Test Runner (Programmatic) ===");
        Console.WriteLine($"Target Server: {baseUrl}");
        Console.WriteLine($"Output Report: {outputPath}");
        if (!string.IsNullOrEmpty(testFilter))
        {
            Console.WriteLine($"Test Filter: {testFilter}");
        }
        Console.WriteLine();

        // Set environment variable for RemoteTestFhirServer
        Environment.SetEnvironmentVariable("TestEnvironmentUrl_R4_Sql", baseUrl);
        Console.WriteLine($"Set environment variable: TestEnvironmentUrl_R4_Sql={baseUrl}");
        Console.WriteLine();

        // Find the E2E test assembly from NuGet package
        var e2eAssemblyPath = Path.Combine(
            AppContext.BaseDirectory,
            "Microsoft.Health.Fhir.R4.Tests.E2E.dll");

        e2eAssemblyPath = Path.GetFullPath(e2eAssemblyPath);

        if (!File.Exists(e2eAssemblyPath))
        {
            Console.WriteLine($"ERROR: E2E test assembly not found at: {e2eAssemblyPath}");
            return;
        }

        Console.WriteLine($"Loading test assembly: {e2eAssemblyPath}");
        Console.WriteLine();

        var report = new CompatibilityReport
        {
            ServerUrl = baseUrl,
            TestRunDate = DateTime.UtcNow,
            Results = new List<TestResult>()
        };

        var finished = new ManualResetEvent(false);

        using (var runner = AssemblyRunner.WithoutAppDomain(e2eAssemblyPath))
        {
            // Filter to only run SqlServer and Json tests
            runner.TestCaseFilter = testCase =>
            {
                var displayName = testCase.DisplayName;
                // Filter for tests with (SqlServer, Json) or similar patterns
                // Also exclude tests that explicitly use CosmosDb
                bool isSqlServer = displayName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase);
                bool isJson = displayName.Contains("Json", StringComparison.OrdinalIgnoreCase);
                bool isCosmosDb = displayName.Contains("CosmosDb", StringComparison.OrdinalIgnoreCase);

                // Include if SqlServer AND Json, but NOT CosmosDb
                bool matchesDataStore = isSqlServer && isJson && !isCosmosDb;

                // Apply additional test name filter if specified
                if (!string.IsNullOrEmpty(testFilter))
                {
                    matchesDataStore = matchesDataStore && displayName.Contains(testFilter, StringComparison.OrdinalIgnoreCase);
                }

                return matchesDataStore;
            };

            runner.OnDiscoveryComplete = info =>
            {
                Console.WriteLine($"Discovery complete: {info.TestCasesDiscovered} test cases discovered (filtered for SqlServer + Json)");
                Console.WriteLine();
            };

            runner.OnExecutionComplete = info =>
            {
                Console.WriteLine();
                Console.WriteLine($"Execution complete:");
                Console.WriteLine($"  Total: {info.TotalTests}");
                Console.WriteLine($"  Passed: {info.TotalTests - info.TestsFailed - info.TestsSkipped}");
                Console.WriteLine($"  Failed: {info.TestsFailed}");
                Console.WriteLine($"  Skipped: {info.TestsSkipped}");
                Console.WriteLine($"  Time: {info.ExecutionTime:F2}s");

                report.TotalTests = info.TotalTests;
                report.Passed = info.TotalTests - info.TestsFailed - info.TestsSkipped;
                report.Failed = info.TestsFailed;
                report.Skipped = info.TestsSkipped;

                finished.Set();
            };

            runner.OnTestStarting = info =>
            {
                Console.Write($"  Starting: {info.TestDisplayName}...");
            };

            runner.OnTestPassed = info =>
            {
                Console.WriteLine($" PASSED ({info.ExecutionTime:F2}s)");

                report.Results.Add(new TestResult
                {
                    TestName = info.TestDisplayName,
                    Category = GetTestCategory(info.TestDisplayName),
                    Status = "Passed",
                    Duration = info.ExecutionTime,
                    Output = info.Output
                });
            };

            runner.OnTestFailed = info =>
            {
                Console.WriteLine($" FAILED ({info.ExecutionTime:F2}s)");
                Console.WriteLine($"    Error: {info.ExceptionMessage}");

                report.Results.Add(new TestResult
                {
                    TestName = info.TestDisplayName,
                    Category = GetTestCategory(info.TestDisplayName),
                    Status = "Failed",
                    Duration = info.ExecutionTime,
                    ErrorMessage = info.ExceptionMessage,
                    StackTrace = info.ExceptionStackTrace,
                    Output = info.Output
                });
            };

            runner.OnTestSkipped = info =>
            {
                Console.WriteLine($" SKIPPED: {info.SkipReason}");

                report.Results.Add(new TestResult
                {
                    TestName = info.TestDisplayName,
                    Category = GetTestCategory(info.TestDisplayName),
                    Status = "Skipped",
                    ErrorMessage = info.SkipReason
                });
            };

            Console.WriteLine("Running tests...");
            Console.WriteLine();

            runner.Start();

            finished.WaitOne();
            finished.Dispose();
        }

        // Save JSON report
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(outputPath, json);

        Console.WriteLine();
        Console.WriteLine($"Report saved to: {outputPath}");

        // Print summary
        Console.WriteLine();
        Console.WriteLine("=== Summary ===");
        Console.WriteLine($"Server: {report.ServerUrl}");
        Console.WriteLine($"Total Tests: {report.TotalTests}");
        Console.WriteLine($"Passed: {report.Passed} ({report.PassRate:P1})");
        Console.WriteLine($"Failed: {report.Failed}");
        Console.WriteLine($"Skipped: {report.Skipped}");
    }

    static string GetTestCategory(string testName)
    {
        if (testName.Contains('.'))
        {
            var parts = testName.Split('.');
            if (parts.Length >= 2)
            {
                var className = parts[parts.Length - 2];
                if (className.EndsWith("Tests"))
                {
                    return className.Substring(0, className.Length - 5);
                }
                return className;
            }
        }
        return "General";
    }
}

class CompatibilityReport
{
    public string ServerUrl { get; set; } = string.Empty;
    public DateTime TestRunDate { get; set; }
    public int TotalTests { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public List<TestResult> Results { get; set; } = new();
    public double PassRate => TotalTests > 0 ? (double)Passed / TotalTests : 0;
}

class TestResult
{
    public string TestName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Duration { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string StackTrace { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
}
