using System;
using System.CommandLine;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using Xunit.Runners;
using Task = System.Threading.Tasks.Task;

namespace Ignixa.Tests.Compatibility.CLI;

class Program
{
    private const string ESC = "\x1b";
    private static Dictionary<string, string> s_traitCache = new();

    private static class Color
    {
        public const string Reset = ESC + "[0m";
        public const string Bold = ESC + "[1m";
        public const string Cyan = ESC + "[36m";
        public const string Green = ESC + "[32m";
        public const string Red = ESC + "[31m";
        public const string Yellow = ESC + "[33m";
        public const string Blue = ESC + "[34m";
        public const string Gray = ESC + "[90m";
    }

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
            description: "Filter test names (e.g., 'CreateTests' or comma-separated: 'CreateTests,UpdateTests,SearchTests')",
            getDefaultValue: () => string.Empty);

        var skipOption = new Option<string>(
            name: "--skip",
            description: "Skip test categories (comma-separated). Options: Import, Export, Convert, Bulk, Auth, Metrics, Audit, CustomConvert, CustomImport, CustomExport",
            getDefaultValue: () => "Import,Export,Convert,Bulk,CustomConvert,CustomImport,CustomExport");

        // Viewer command options
        var viewerCommand = new Command("viewer", "Launch the test results viewer UI");

        var portOption = new Option<int>(
            name: "--port",
            description: "HTTP server port for the viewer",
            getDefaultValue: () => 8080);

        var reportOption = new Option<string>(
            name: "--report",
            description: "Auto-load a test report JSON file",
            getDefaultValue: () => string.Empty);

        viewerCommand.AddOption(portOption);
        viewerCommand.AddOption(reportOption);

        viewerCommand.SetHandler(async (port, report) =>
        {
            await TestResultsViewerCommand.RunViewerAsync(port, string.IsNullOrEmpty(report) ? null : report);
        }, portOption, reportOption);

        var rootCommand = new RootCommand("FHIR Compatibility Test Tool - Runs Microsoft.Health.Fhir.R4.Tests.E2E against target server")
        {
            urlOption,
            outputOption,
            filterOption,
            skipOption,
            viewerCommand
        };

        rootCommand.SetHandler(async (url, output, filter, skip) =>
        {
            await RunCompatibilityTests(url, output, filter, skip);
        }, urlOption, outputOption, filterOption, skipOption);

        return await rootCommand.InvokeAsync(args);
    }

    static async Task RunCompatibilityTests(string baseUrl, string outputPath, string testFilter, string skipCategories)
    {
        PrintHeader();
        Console.WriteLine($"{Color.Blue}▶ Target Server:{Color.Reset}   {Color.Cyan}{baseUrl}{Color.Reset}");
        Console.WriteLine($"{Color.Blue}▶ Output Report:{Color.Reset}   {Color.Cyan}{outputPath}{Color.Reset}");
        if (!string.IsNullOrEmpty(testFilter))
        {
            Console.WriteLine($"{Color.Blue}▶ Test Filter:{Color.Reset}    {Color.Yellow}{testFilter}{Color.Reset}");
        }
        if (!string.IsNullOrEmpty(skipCategories))
        {
            Console.WriteLine($"{Color.Blue}▶ Skip Categories:{Color.Reset}  {Color.Yellow}{skipCategories}{Color.Reset}");
        }
        Console.WriteLine();

        // Set environment variable for RemoteTestFhirServer
        Environment.SetEnvironmentVariable("TestEnvironmentUrl_R4_Sql", baseUrl);
        Console.WriteLine($"{Color.Gray}[*] Set environment variable: TestEnvironmentUrl_R4_Sql={baseUrl}{Color.Reset}");
        Console.WriteLine();

        // Find the E2E test assembly from NuGet package
        var e2eAssemblyPath = Path.Combine(
            AppContext.BaseDirectory,
            "Microsoft.Health.Fhir.R4.Tests.E2E.dll");

        e2eAssemblyPath = Path.GetFullPath(e2eAssemblyPath);

        if (!File.Exists(e2eAssemblyPath))
        {
            Console.WriteLine($"{Color.Red}✗ ERROR:{Color.Reset} E2E test assembly not found at: {e2eAssemblyPath}");
            return;
        }

        Console.WriteLine($"{Color.Gray}[*] Loading test assembly...{Color.Reset}");
        Console.WriteLine();

        // Load traits from test assembly using reflection
        try
        {
            var assembly = Assembly.LoadFrom(e2eAssemblyPath);
            ExtractTraitsFromAssembly(assembly);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{Color.Gray}[!] Warning: Could not extract traits: {ex.Message}{Color.Reset}");
        }

        var report = new CompatibilityReport
        {
            ServerUrl = baseUrl,
            TestRunDate = DateTime.UtcNow,
            Results = new List<TestResult>()
        };

        var finished = new ManualResetEvent(false);

        using (var runner = AssemblyRunner.WithoutAppDomain(e2eAssemblyPath))
        {
            // Parse skip categories
            var categoriesToSkip = string.IsNullOrEmpty(skipCategories)
                ? new List<string>()
                : skipCategories.Split(',').Select(c => c.Trim()).ToList();

            // Filter to only run SqlServer and Json tests
            runner.TestCaseFilter = testCase =>
            {
                var displayName = testCase.DisplayName;

                // Check if test matches any skip category
                if (categoriesToSkip.Count > 0)
                {
                    foreach (var skipCategory in categoriesToSkip)
                    {
                        if (displayName.Contains(skipCategory, StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                    }
                }

                // Filter for tests with (SqlServer, Json) or similar patterns
                // Also exclude tests that explicitly use CosmosDb
                bool isSqlServer = displayName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase);
                bool isJson = displayName.Contains("Json", StringComparison.OrdinalIgnoreCase);
                bool isCosmosDb = displayName.Contains("CosmosDb", StringComparison.OrdinalIgnoreCase);

                // Include if SqlServer AND Json, but NOT CosmosDb
                bool matchesDataStore = isSqlServer && isJson && !isCosmosDb;

                // Apply additional test name filter if specified (supports comma-separated OR logic)
                if (!string.IsNullOrEmpty(testFilter))
                {
                    var filters = testFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    bool matchesAnyFilter = filters.Any(f => displayName.Contains(f, StringComparison.OrdinalIgnoreCase));
                    matchesDataStore = matchesDataStore && matchesAnyFilter;
                }

                return matchesDataStore;
            };

            var discoveredCount = 0;
            runner.OnDiscoveryComplete = info =>
            {
                discoveredCount = info.TestCasesDiscovered;
                Console.WriteLine($"{Color.Cyan}✓ Discovery Complete{Color.Reset}");
                Console.WriteLine($"  {Color.Bold}{info.TestCasesDiscovered}{Color.Reset} test cases discovered (filtered for {Color.Yellow}SqlServer + Json{Color.Reset})");
                Console.WriteLine();
            };

            runner.OnExecutionComplete = info =>
            {
                Console.WriteLine();
                PrintSeparator();
                Console.WriteLine($"{Color.Bold}Execution Summary{Color.Reset}");
                PrintSeparator();
                var passed = info.TotalTests - info.TestsFailed - info.TestsSkipped;
                var passPercentage = info.TotalTests > 0 ? (double)passed / info.TotalTests * 100 : 0;
                Console.WriteLine($"  {Color.Bold}Total:{Color.Reset}   {info.TotalTests}");
                Console.WriteLine($"  {Color.Green}✓ Passed:{Color.Reset}  {Color.Green}{Color.Bold}{passed}{Color.Reset} ({passPercentage:F1}%)");
                Console.WriteLine($"  {Color.Red}✗ Failed:{Color.Reset}  {Color.Red}{Color.Bold}{info.TestsFailed}{Color.Reset}");
                Console.WriteLine($"  {Color.Yellow}⊘ Skipped:{Color.Reset} {Color.Yellow}{Color.Bold}{info.TestsSkipped}{Color.Reset}");
                Console.WriteLine($"  {Color.Blue}⏱ Time:{Color.Reset}    {info.ExecutionTime:F2}s");
                PrintSeparator();

                report.TotalTests = info.TotalTests;
                report.Passed = passed;
                report.Failed = info.TestsFailed;
                report.Skipped = info.TestsSkipped;

                finished.Set();
            };

            runner.OnTestStarting = info =>
            {
                // Minimal output during test execution
            };

            runner.OnTestPassed = info =>
            {
                Console.WriteLine($"{Color.Green}✓{Color.Reset} {GetTestCategory(info.TestDisplayName)} - {Color.Gray}{info.ExecutionTime:F2}s{Color.Reset}");

                report.Results.Add(new TestResult
                {
                    TestName = info.TestDisplayName,
                    Category = GetTestCategory(info.TestDisplayName),
                    Trait = ExtractTestTrait(info.TestDisplayName),
                    Status = "Passed",
                    Duration = info.ExecutionTime,
                    Output = info.Output
                });
            };

            runner.OnTestFailed = info =>
            {
                Console.WriteLine($"{Color.Red}✗{Color.Reset} {GetTestCategory(info.TestDisplayName)} - {Color.Red}{info.ExecutionTime:F2}s{Color.Reset}");
                if (!string.IsNullOrEmpty(info.ExceptionMessage))
                {
                    var shortError = info.ExceptionMessage.Length > 100 ? info.ExceptionMessage[..97] + "..." : info.ExceptionMessage;
                    Console.WriteLine($"  {Color.Gray}→ {shortError}{Color.Reset}");
                }

                report.Results.Add(new TestResult
                {
                    TestName = info.TestDisplayName,
                    Category = GetTestCategory(info.TestDisplayName),
                    Trait = ExtractTestTrait(info.TestDisplayName),
                    Status = "Failed",
                    Duration = info.ExecutionTime,
                    ErrorMessage = info.ExceptionMessage,
                    StackTrace = info.ExceptionStackTrace,
                    Output = info.Output
                });
            };

            runner.OnTestSkipped = info =>
            {
                Console.WriteLine($"{Color.Yellow}⊘{Color.Reset} {GetTestCategory(info.TestDisplayName)}");

                report.Results.Add(new TestResult
                {
                    TestName = info.TestDisplayName,
                    Category = GetTestCategory(info.TestDisplayName),
                    Trait = ExtractTestTrait(info.TestDisplayName),
                    Status = "Skipped",
                    ErrorMessage = info.SkipReason
                });
            };

            Console.WriteLine($"{Color.Bold}{Color.Cyan}▶ Running {discoveredCount} tests...{Color.Reset}");
            Console.WriteLine();

            runner.Start();

            finished.WaitOne();
            finished.Dispose();
        }

        // Save JSON report
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(outputPath, json);

        Console.WriteLine();
        Console.WriteLine($"{Color.Green}✓{Color.Reset} Report saved to: {Color.Cyan}{outputPath}{Color.Reset}");

        // Print summary
        Console.WriteLine();
        PrintSeparator();
        Console.WriteLine($"{Color.Bold}{Color.Cyan}COMPATIBILITY REPORT SUMMARY{Color.Reset}");
        PrintSeparator();
        Console.WriteLine($"  {Color.Blue}Server:{Color.Reset}              {report.ServerUrl}");
        Console.WriteLine($"  {Color.Blue}Test Timestamp:{Color.Reset}       {report.TestRunDate:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine($"  {Color.Blue}Total Tests:{Color.Reset}          {report.TotalTests}");
        if (report.Passed > 0)
            Console.WriteLine($"  {Color.Green}✓ Passed:{Color.Reset}            {Color.Green}{Color.Bold}{report.Passed}{Color.Reset} ({report.PassRate:P1})");
        if (report.Failed > 0)
            Console.WriteLine($"  {Color.Red}✗ Failed:{Color.Reset}            {Color.Red}{Color.Bold}{report.Failed}{Color.Reset}");
        if (report.Skipped > 0)
            Console.WriteLine($"  {Color.Yellow}⊘ Skipped:{Color.Reset}           {Color.Yellow}{Color.Bold}{report.Skipped}{Color.Reset}");
        PrintSeparator();
    }

    static void PrintHeader()
    {
        Console.WriteLine($"{Color.Bold}{Color.Cyan}╔══════════════════════════════════════════════════════════════╗{Color.Reset}");
        Console.WriteLine($"{Color.Bold}{Color.Cyan}║     FHIR Compatibility Test Runner (E2E Testing)             ║{Color.Reset}");
        Console.WriteLine($"{Color.Bold}{Color.Cyan}╚══════════════════════════════════════════════════════════════╝{Color.Reset}");
        Console.WriteLine();
    }

    static void PrintSeparator()
    {
        Console.WriteLine($"{Color.Gray}─────────────────────────────────────────────────────────────{Color.Reset}");
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
                    return className[..^5];
                }
                return className;
            }
        }
        return "General";
    }

    static string ExtractTestTrait(string testName)
    {
        // Extract test class name from full test display name
        // Format: "Namespace.ClassName(Parameterization).MethodName(...)"
        if (testName.Contains('.'))
        {
            var parts = testName.Split('.');
            if (parts.Length >= 2)
            {
                var className = parts[parts.Length - 2];

                // Check if we have cached traits for this class
                if (s_traitCache.TryGetValue(className, out var traits))
                {
                    return traits;
                }

                return className;
            }
        }
        return "Unknown";
    }

    static void ExtractTraitsFromAssembly(Assembly assembly)
    {
        try
        {
            var types = assembly.GetTypes();
            foreach (var type in types)
            {
                var traitAttributes = type.GetCustomAttributes(false)
                    .Where(a => a.GetType().Name == "TraitAttribute")
                    .ToList();

                if (traitAttributes.Count > 0)
                {
                    var traits = new List<string>();
                    foreach (var attr in traitAttributes)
                    {
                        var nameProp = attr.GetType().GetProperty("Name");
                        var valueProp = attr.GetType().GetProperty("Value");

                        if (nameProp != null && valueProp != null)
                        {
                            var name = nameProp.GetValue(attr)?.ToString();
                            var value = valueProp.GetValue(attr)?.ToString();
                            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value))
                            {
                                traits.Add($"{name}={value}");
                            }
                        }
                    }

                    if (traits.Count > 0)
                    {
                        s_traitCache[type.Name] = string.Join(", ", traits);
                    }
                }
            }
        }
        catch (Exception)
        {
            // Silently ignore errors - trait extraction is optional
        }
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
    public string Trait { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Duration { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string StackTrace { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
}
