using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Specification.Extensions;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Evaluation;
using Ignixa.TestScript.Fixtures;
using Ignixa.TestScript.Locust.Compilation;
using Ignixa.TestScript.Locust.Ir;
using Ignixa.TestScript.Model;
using Ignixa.TestScript.Parsing;
using Ignixa.TestScript.Reporting;
using Shouldly;

namespace Ignixa.TestScript.Locust.Tests.Contracts;

/// <summary>
/// The .NET half of the cross-language runtime contract. Every case in the reviewed, immutable
/// <c>Contracts/runtime-cases.json</c> is driven through the REAL engines - <see cref="TestScriptParser"/>,
/// <see cref="LocustIrCompiler"/>, and <see cref="TestScriptEvaluator"/> - and the observable results are
/// compared to the committed contract. The Python <c>test_runtime_contract.py</c> drives the identical cases
/// through the Locust runtime against the same contract, so the two engines cannot silently diverge.
/// <para>
/// This suite never writes or regenerates the contract; the committed expected values ARE the real behavior of
/// both engines. Comparisons use only public engine behavior (parse result, compiled IR, outbound requests, and
/// the report), and normalize solely representation noise (JSON whitespace/key-order, header casing, and the
/// default <c>Content-Type</c> the provider never sees) via <see cref="RuntimeContractProjection"/>.
/// </para>
/// </summary>
public class RuntimeContractTests
{
    private static readonly IFhirSchemaProvider s_schema = FhirVersion.R4.GetSchemaProvider();

    public static IEnumerable<object[]> Cases()
    {
        foreach (RuntimeContractCase contractCase in RuntimeContractCases.Load())
        {
            yield return [contractCase.Name];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task GivenSharedRuntimeContractCase_WhenParsed_ThenTestScriptParserSucceeds(string name)
    {
        RuntimeContractCase contractCase = RuntimeContractCases.ByName(name);

        var parse = TestScriptParser.Parse(contractCase.InputJson);

        parse.IsSuccess.ShouldBeTrue(
            $"case '{name}': parse failed: {string.Join("; ", parse.Errors.Select(e => e.Message))}");
        parse.Value.ShouldNotBeNull();
        await Task.CompletedTask;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task GivenSharedRuntimeContractCase_WhenCompiled_ThenCanonicalIrMatchesContract(string name)
    {
        RuntimeContractCase contractCase = RuntimeContractCases.ByName(name);
        TestScriptDefinition definition = ParseOrThrow(contractCase);

        JsonNode actualIr = await CompileToIrAsync(contractCase, definition);

        RuntimeContractProjection.JsonEquivalent(actualIr, contractCase.CanonicalIr).ShouldBeTrue(
            $"case '{name}': compiled IR did not match the contract's canonicalIr.\n" +
            $"actual:   {actualIr.ToJsonString()}\n" +
            $"expected: {contractCase.CanonicalIr.ToJsonString()}");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task GivenSharedRuntimeContractCase_WhenEvaluated_ThenOutboundRequestsMatchContract(string name)
    {
        RuntimeContractCase contractCase = RuntimeContractCases.ByName(name);
        TestScriptDefinition definition = ParseOrThrow(contractCase);
        (_, QueuedTestRequestProvider provider) = await EvaluateAsync(contractCase, definition);

        JsonArray expected = contractCase.ExpectedRequests;
        provider.Requests.Count.ShouldBe(expected.Count,
            $"case '{name}': outbound request count differed from the contract.");

        for (int i = 0; i < expected.Count; i++)
        {
            JsonObject expectedRequest = expected[i]!.AsObject();
            TestRequest actualRequest = provider.Requests[i];
            JsonObject actualNormalized = RuntimeContractProjection.NormalizeRequest(actualRequest);

            actualNormalized["method"]!.GetValue<string>().ShouldBe(
                expectedRequest["method"]!.GetValue<string>(), $"case '{name}': request[{i}].method");
            actualNormalized["url"]!.GetValue<string>().ShouldBe(
                expectedRequest["url"]!.GetValue<string>(), $"case '{name}': request[{i}].url");
            RuntimeContractProjection.JsonEquivalent(actualNormalized["body"], expectedRequest["body"]).ShouldBeTrue(
                $"case '{name}': request[{i}].body\n" +
                $"actual:   {actualNormalized["body"]?.ToJsonString() ?? "null"}\n" +
                $"expected: {expectedRequest["body"]?.ToJsonString() ?? "null"}");

            AssertHeaderContainment(name, i, expectedRequest, actualRequest);
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task GivenSharedRuntimeContractCase_WhenEvaluated_ThenPhaseOutcomesMatchContract(string name)
    {
        RuntimeContractCase contractCase = RuntimeContractCases.ByName(name);
        TestScriptDefinition definition = ParseOrThrow(contractCase);
        (TestScriptReport report, _) = await EvaluateAsync(contractCase, definition);

        JsonObject actualPhases = ProjectPhases(report);

        RuntimeContractProjection.JsonEquivalent(actualPhases, contractCase.ExpectedPhases).ShouldBeTrue(
            $"case '{name}': phase outcomes did not match the contract.\n" +
            $"actual:   {actualPhases.ToJsonString()}\n" +
            $"expected: {contractCase.ExpectedPhases.ToJsonString()}");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task GivenSharedRuntimeContractCase_WhenEvaluated_ThenReportActionsMatchContract(string name)
    {
        RuntimeContractCase contractCase = RuntimeContractCases.ByName(name);
        TestScriptDefinition definition = ParseOrThrow(contractCase);
        (TestScriptReport report, _) = await EvaluateAsync(contractCase, definition);

        JsonObject actualReport = ProjectReport(report);

        RuntimeContractProjection.JsonEquivalent(actualReport, contractCase.ExpectedReport).ShouldBeTrue(
            $"case '{name}': report actions did not match the contract.\n" +
            $"actual:   {actualReport.ToJsonString()}\n" +
            $"expected: {contractCase.ExpectedReport.ToJsonString()}");
    }

    [Fact]
    public async Task GivenPollingTimeoutCase_WhenEvaluated_ThenExhaustionMessageMatchesContract()
    {
        RuntimeContractCase contractCase = RuntimeContractCases.ByName("polling-timeout");
        string expectedMessage = contractCase.PollingTimeoutMessage
            ?? throw new InvalidOperationException("polling-timeout case must pin expectedPollingTimeoutMessage.");
        TestScriptDefinition definition = ParseOrThrow(contractCase);
        (TestScriptReport report, _) = await EvaluateAsync(contractCase, definition);

        ActionResult? erroredOperation = report.TestResults
            .SelectMany(test => test.Actions)
            .FirstOrDefault(action =>
                action.Kind == TestActionKind.Operation && action.Outcome == TestScriptOutcome.Error);

        erroredOperation.ShouldNotBeNull("polling-timeout must record an errored operation action.");
        erroredOperation.Message.ShouldBe(expectedMessage);
    }

    private static TestScriptDefinition ParseOrThrow(RuntimeContractCase contractCase)
    {
        var parse = TestScriptParser.Parse(contractCase.InputJson);
        if (!parse.IsSuccess || parse.Value is null)
        {
            throw new InvalidOperationException(
                $"case '{contractCase.Name}': parse failed: {string.Join("; ", parse.Errors.Select(e => e.Message))}");
        }

        return parse.Value;
    }

    private static async Task<JsonNode> CompileToIrAsync(RuntimeContractCase contractCase, TestScriptDefinition definition)
    {
        LocustIrCompiler compiler = new();
        LocustCompilationResult result = await compiler.CompileAsync(
            definition,
            new LocustCompilerOptions(contractCase.Source, contractCase.FhirVersion, s_schema, FixtureVariants: 0),
            CancellationToken.None);

        result.Document.ShouldNotBeNull($"case '{contractCase.Name}': compiler produced no IR document.");
        return JsonNode.Parse(LocustIrSerializer.Serialize(result.Document))
            ?? throw new InvalidOperationException($"case '{contractCase.Name}': serialized IR parsed to null.");
    }

    private static async Task<(TestScriptReport Report, QueuedTestRequestProvider Provider)> EvaluateAsync(
        RuntimeContractCase contractCase, TestScriptDefinition definition)
    {
        List<object> responses = [];
        foreach (JsonNode? responseNode in contractCase.Responses)
        {
            responses.Add(RuntimeContractProjection.BuildResponse(
                System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(responseNode!.ToJsonString())));
        }

        QueuedTestRequestProvider provider = new(responses);
        TestScriptEvaluator evaluator = new(provider, new InlineFixtureProvider(), s_schema);
        TestScriptReport report = await evaluator.ExecuteAsync(definition, CancellationToken.None, contractCase.FhirVersion);
        return (report, provider);
    }

    private static void AssertHeaderContainment(
        string name, int index, JsonObject expectedRequest, TestRequest actualRequest)
    {
        if (expectedRequest["headers"] is not JsonObject expectedHeaders)
        {
            return;
        }

        Dictionary<string, string> actualHeaders = RuntimeContractProjection.LowerHeaders(actualRequest);
        foreach (KeyValuePair<string, JsonNode?> expectedHeader in expectedHeaders)
        {
            actualHeaders.ShouldContainKey(expectedHeader.Key,
                $"case '{name}': request[{index}] is missing expected header '{expectedHeader.Key}'.");
            actualHeaders[expectedHeader.Key].ShouldBe(expectedHeader.Value!.GetValue<string>(),
                $"case '{name}': request[{index}] header '{expectedHeader.Key}' value differed.");
        }
    }

    private static JsonObject ProjectPhases(TestScriptReport report)
    {
        JsonArray tests = [];
        foreach (TestCaseResult test in report.TestResults)
        {
            tests.Add(new JsonObject
            {
                ["name"] = test.Name,
                ["outcome"] = RuntimeContractProjection.OutcomeToken(test.Outcome),
                ["skipped"] = test.Outcome == TestScriptOutcome.Skip,
                ["failed"] = test.Outcome is TestScriptOutcome.Fail or TestScriptOutcome.Error,
            });
        }

        return new JsonObject
        {
            ["setup"] = ProjectPhaseSummary(report.SetupResult?.Outcome),
            ["tests"] = tests,
            ["teardown"] = ProjectPhaseSummary(report.TeardownResult?.Outcome),
        };
    }

    private static JsonObject ProjectPhaseSummary(TestScriptOutcome? outcome)
    {
        if (outcome is null)
        {
            return new JsonObject { ["outcome"] = "absent", ["failed"] = false };
        }

        return new JsonObject
        {
            ["outcome"] = RuntimeContractProjection.OutcomeToken(outcome.Value),
            ["failed"] = outcome is TestScriptOutcome.Fail or TestScriptOutcome.Error,
        };
    }

    private static JsonObject ProjectReport(TestScriptReport report)
    {
        JsonArray tests = [];
        foreach (TestCaseResult test in report.TestResults)
        {
            tests.Add(new JsonObject
            {
                ["name"] = test.Name,
                ["actions"] = ProjectActions(test.Actions),
            });
        }

        return new JsonObject
        {
            ["setup"] = report.SetupResult is null ? null : ProjectActions(report.SetupResult.Actions),
            ["tests"] = tests,
            ["teardown"] = report.TeardownResult is null ? null : ProjectActions(report.TeardownResult.Actions),
        };
    }

    private static JsonArray ProjectActions(IReadOnlyList<ActionResult> actions)
    {
        JsonArray array = [];
        foreach (ActionResult action in actions)
        {
            array.Add(new JsonObject
            {
                ["kind"] = RuntimeContractProjection.ActionKindToken(action.Kind),
                ["outcome"] = RuntimeContractProjection.OutcomeToken(action.Outcome),
                ["label"] = action.Label,
                ["groupId"] = action.GroupId,
            });
        }

        return array;
    }
}
