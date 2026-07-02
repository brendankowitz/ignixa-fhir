using System.Text.Json;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Evaluation;
using Ignixa.TestScript.FhirFakes;
using Ignixa.TestScript.Fixtures;
using Ignixa.TestScript.Parsing;
using Ignixa.TestScript.Reporting;
using Shouldly;

namespace Ignixa.Api.E2ETests.Conformance;

[Collection(E2ETestCollection.Name)]
public sealed class TestScriptConformanceReportTests
{
    private const string EnabledEnvironmentVariable = "IGNIXA_RUN_CONFORMANCE";
    private const string ReportPathEnvironmentVariable = "IGNIXA_CONFORMANCE_REPORT_PATH";
    private const string FhirVersion = "4.0";
    private const string ImplementationName = "ignixa";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IgnixaApiFixture _fixture;

    public TestScriptConformanceReportTests(IgnixaApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenConformanceRunEnabled_WhenRunningRepositoryTestScripts_ThenWritesLatestReport()
    {
        if (!IsEnabled())
            return;

        var startedAt = DateTimeOffset.UtcNow;
        var startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        var testsDirectory = FindRepositoryDirectory("conformance-tests");
        var evaluator = CreateEvaluator();
        var results = new List<ConformanceResult>();

        foreach (var file in Directory.EnumerateFiles(testsDirectory, "*.json", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var relativeFile = Path.GetRelativePath(testsDirectory, file).Replace('\\', '/');
            await RunTestScriptAsync(evaluator, file, relativeFile, results, CancellationToken.None);
        }

        var durationMs = (long)System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        // WebApplicationFactory.CreateClient() always sets BaseAddress; Target's non-nullable
        // type relies on that framework guarantee rather than a silent empty-string fallback.
        var target = _fixture.Client.BaseAddress!.ToString();
        var report = new ConformanceReport(ImplementationName, target, FhirVersion, startedAt, durationMs, results);
        await WriteReportAsync(report, CancellationToken.None);

        results.ShouldNotBeEmpty("No conformance tests were found or executed.");
        results.Where(result => result.Status == "error")
            .ShouldBeEmpty("TestScript parse/evaluator errors indicate conformance infrastructure failed.");
        results.Count(result => result.Status is "pass" or "fail").ShouldBeGreaterThan(0,
            "All results were skipped — the conformance corpus produced no executable checks.");
    }

    private static bool IsEnabled() =>
        Environment.GetEnvironmentVariable(EnabledEnvironmentVariable)
            ?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

    private TestScriptEvaluator CreateEvaluator()
    {
        var provider = new HttpTestRequestProvider(_fixture.Client);
        var fixtureProvider = new CompositeFixtureProvider(
        [
            new FhirFakesFixtureProvider(),
            new InlineFixtureProvider()
        ]);

        return new TestScriptEvaluator(provider, fixtureProvider, _fixture.SchemaProvider);
    }

    private static async Task RunTestScriptAsync(
        TestScriptEvaluator evaluator,
        string file,
        string relativeFile,
        List<ConformanceResult> results,
        CancellationToken cancellationToken)
    {
        var (suite, category) = ConformanceReportMapper.DescribeSuite(relativeFile);

        Ignixa.TestScript.Parsing.ParseResult<Ignixa.TestScript.Model.TestScriptDefinition> parseResult;
        try
        {
            parseResult = TestScriptParser.ParseFile(file);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            results.Add(ConformanceResult.CreateError(relativeFile, suite, category, "Parse exception", ex.Message));
            return;
        }

        if (!parseResult.IsSuccess)
        {
            var messages = string.Join("; ", parseResult.Errors.Select(error => error.Message));
            results.Add(ConformanceResult.CreateError(relativeFile, suite, category, "Parse error", messages));
            return;
        }

        try
        {
            var report = await evaluator.ExecuteAsync(parseResult.Value!, cancellationToken, FhirVersion);
            results.AddRange(ConformanceReportMapper.Map(report, relativeFile));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            results.Add(ConformanceResult.CreateError(relativeFile, suite, category, "Evaluator error", ex.Message));
        }
    }

    private static async Task WriteReportAsync(ConformanceReport report, CancellationToken cancellationToken)
    {
        var reportPath = Environment.GetEnvironmentVariable(ReportPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(reportPath))
            return;

        var directory = Path.GetDirectoryName(Path.GetFullPath(reportPath));
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(report, JsonOptions);
        await File.WriteAllTextAsync(reportPath, json, cancellationToken);
    }

    private static string FindRepositoryDirectory(string directoryName)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, directoryName);
            if (Directory.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find '{directoryName}' from '{AppContext.BaseDirectory}'.");
    }
}
