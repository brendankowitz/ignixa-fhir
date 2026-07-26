using System.Text.Json;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Evaluation;
using Ignixa.TestScript.FhirFakes;
using Ignixa.TestScript.Fixtures;
using Ignixa.TestScript.Parsing;
using Ignixa.TestScript.Reporting;
using Shouldly;

namespace Ignixa.Api.E2ETests.Conformance;

[Collection(ConformanceTestCollection.Name)]
public sealed class TestScriptConformanceReportTests
{
    private const string EnabledEnvironmentVariable = "IGNIXA_RUN_CONFORMANCE";
    private const string ReportPathEnvironmentVariable = "IGNIXA_CONFORMANCE_REPORT_PATH";
    private const string FhirVersion = "4.0";
    private const string ImplementationName = "ignixa";
    private const string SuitesDirectoryName = "testscripts";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // Suites that legitimately produce no executable checks against this server, because they
    // exercise operations or FHIR releases it does not implement. Everything here is a deliberate
    // decision: a suite that goes fully skipped for any other reason -- typically a capability
    // gate that can never evaluate true -- is a silently disabled test, not an unsupported
    // feature, and must fail this assertion rather than be added to the list.
    private static readonly HashSet<string> SuitesWithNoSupportedFeatures = new(StringComparer.Ordinal)
    {
        // Version-specific corpora that do not apply to the R4 target
        "CRUD/all-resource-types-r4b-plus.json",
        "CRUD/all-resource-types-r5-only.json",
        "CRUD/all-resource-types-stu3-only.json",

        // No vread route exists, so the CapabilityStatement deliberately does not declare it
        "CRUD/vread.json",

        // Bulk/import/export and reindex operations are not implemented
        "Microsoft/ms-bulk-delete.json",
        "Microsoft/ms-bulk-update.json",
        "Microsoft/ms-convert-data.json",
        "Microsoft/ms-import-basic.json",
        "Microsoft/ms-import-history-soft-delete.json",
        "Microsoft/ms-import-rebuild-indexes.json",
        "Microsoft/ms-operation-versions.json",
        "Microsoft/ms-reindex.json",

        // Terminology and document operations are not implemented
        "Operations/docref-operation.json",
        "Operations/everything-operation.json",
        "Operations/expand-operation.json",
        "Operations/lookup-operation.json",
        "Operations/member-match.json",
        "Operations/subsumes-operation.json",
        "Operations/translate-operation.json",
        "Operations/validate-code-operation.json",

        "Subscriptions/basic.json",
        "Validation/validate-op.json",
    };

    private readonly ConformanceApiFixture _fixture;

    public TestScriptConformanceReportTests(ConformanceApiFixture fixture)
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
        var testsDirectory = Path.Combine(AppContext.BaseDirectory, SuitesDirectoryName);
        var evaluator = CreateEvaluator();
        var results = new List<ConformanceResult>();

        foreach (var file in Directory.EnumerateFiles(testsDirectory, "*.json", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var relativeFile = Path.GetRelativePath(testsDirectory, file).Replace('\\', '/');
            await RunTestScriptAsync(evaluator, file, relativeFile, results, _fixture.CapabilityStatement, CancellationToken.None);
        }

        var durationMs = (long)System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        // WebApplicationFactory.CreateClient() always sets BaseAddress; Target's non-nullable
        // type relies on that framework guarantee rather than a silent empty-string fallback.
        var target = _fixture.Client.BaseAddress!.ToString();
        var report = new ConformanceReport(ImplementationName, target, FhirVersion, startedAt, durationMs, results);
        await WriteReportAsync(report, CancellationToken.None);

        results.ShouldNotBeEmpty("No conformance tests were found or executed.");

        // Fail only on infrastructure errors — a suite that won't parse or an evaluator that
        // throws. Behavioral "error" outcomes (a wrong server response cascading a dependent
        // step) are real findings for the matrix, not a broken harness: with an 87-suite
        // cross-vendor corpus the target legitimately lacks operations some suites exercise.
        var infrastructureErrors = results.Where(result => result.IsInfrastructureError).ToList();
        infrastructureErrors.ShouldBeEmpty(
            "TestScript parse/evaluator errors indicate conformance infrastructure failed:\n" +
            string.Join("\n", infrastructureErrors.Select(r => $"  {r.File}: {r.Error?.Assertion} — {r.Error?.Received}")));

        results.Count(result => result.Status is "pass" or "fail").ShouldBeGreaterThan(0,
            "All results were skipped — the conformance corpus produced no executable checks.");

        // Per-suite, not just corpus-wide: one passing test anywhere satisfies the check above,
        // so a suite whose every test is skipped stays invisible. A skip reports neither pass nor
        // fail, so an unsatisfiable capability gate silently deletes its suite from the matrix
        // while CI stays green — which is exactly how several suites sat dead for months.
        var deadSuites = results
            .GroupBy(result => result.File, StringComparer.Ordinal)
            .Where(group => !group.Any(result => result.Status is "pass" or "fail"))
            .Select(group => group.Key)
            .Where(file => !SuitesWithNoSupportedFeatures.Contains(file))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToList();

        deadSuites.ShouldBeEmpty(
            "Suite(s) produced no executable checks — every test was skipped:\n" +
            string.Join("\n", deadSuites.Select(file => $"  {file}")) +
            "\nEither a requiresCapability gate cannot be satisfied by this server's CapabilityStatement " +
            "(fix the gate, or declare the interaction the server actually serves), or the feature is " +
            $"genuinely unsupported and the suite belongs in {nameof(SuitesWithNoSupportedFeatures)}.");
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
        ResourceJsonNode? capabilityStatement,
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
            var report = await evaluator.ExecuteAsync(parseResult.Value!, cancellationToken, FhirVersion, capabilityStatement);
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

}
