using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Specification.Extensions;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Evaluation;
using Ignixa.TestScript.Expressions;
using Ignixa.TestScript.Fixtures;
using Ignixa.TestScript.Model;
using Ignixa.TestScript.Reporting;
using NSubstitute;

namespace Ignixa.TestScript.Tests.Evaluation;

public class VariableExtractorTests
{
    private readonly ITestRequestProvider _mockProvider;
    private readonly IFixtureProvider _fixtureProvider;
    private readonly IFhirSchemaProvider _r4Schema;

    public VariableExtractorTests()
    {
        _mockProvider = Substitute.For<ITestRequestProvider>();
        _fixtureProvider = new InlineFixtureProvider();
        _r4Schema = FhirVersion.R4.GetSchemaProvider();
    }

    [Fact]
    public async Task GivenPathExtractionToNumericLeaf_WhenExtracting_ThenConvertsToString()
    {
        var responses = new Queue<TestResponse>(new[]
        {
            new TestResponse
            {
                StatusCode = 200,
                Body = JsonSourceNodeFactory.Parse("""{"resourceType":"Patient","id":"1","multipleBirthInteger":3}""")
            },
            new TestResponse { StatusCode = 200 }
        });
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => responses.Dequeue());

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "NumericLeaf" },
            Variables = [new VariableDefinition { Name = "birth", Extraction = new PathExtraction("multipleBirthInteger") }],
            Setup = [new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" }],
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "UseBirth",
                    Actions = [new OperationExpression { Type = "read", Resource = "Patient", Params = "/${birth}" }]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _r4Schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
        await _mockProvider.Received().ExecuteAsync(
            Arg.Is<TestRequest>(r => r.Url == "Patient/3"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenExpressionExtractionOfBooleanValue_WhenExtracting_ThenSubstitutesLowercaseString()
    {
        // Same bug class as the fhirpath-value assertion evaluator: a Scalar()?.ToString()
        // would render boolean extractions as "True", not the FhirPath-spec "true".
        var responses = new Queue<TestResponse>(new[]
        {
            new TestResponse
            {
                StatusCode = 200,
                Body = JsonSourceNodeFactory.Parse("""{"resourceType":"Patient","id":"1","active":true}""")
            },
            new TestResponse { StatusCode = 200 }
        });
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => responses.Dequeue());

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "BooleanExpression" },
            Variables = [new VariableDefinition { Name = "isActive", Extraction = new ExpressionExtraction("Patient.active") }],
            Setup = [new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" }],
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "UseIsActive",
                    Actions = [new OperationExpression { Type = "read", Resource = "Patient", Params = "/${isActive}" }]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _r4Schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
        await _mockProvider.Received().ExecuteAsync(
            Arg.Is<TestRequest>(r => r.Url == "Patient/true"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenExpressionExtractionWithBadSyntax_WhenExtracting_ThenRecordsErrorNotSilentlyIgnored()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse
            {
                StatusCode = 200,
                Body = JsonSourceNodeFactory.Parse("""{"resourceType":"Patient","id":"1"}""")
            });

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "BadExpr" },
            Variables = [new VariableDefinition { Name = "broken", Extraction = new ExpressionExtraction("this is (not valid fhirpath") }],
            Setup = [new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" }]
        };

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _r4Schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.SetupResult.ShouldNotBeNull();
        report.SetupResult.Actions.ShouldContain(a =>
            a.Outcome == TestScriptOutcome.Error &&
            a.Label == "variable:broken" &&
            a.Message != null &&
            a.Message.Contains("not valid fhirpath"));
    }

    [Fact]
    public async Task GivenExpressionExtractedAbsoluteNextUrl_WhenUsedAsOperationUrl_ThenPassesThroughUnchanged()
    {
        // history-page-one holds the canonical absolute next URL.
        // history-page-two (the last response / LastResponse) holds a distinct decoy absolute URL.
        // VariableDefinition.SourceId = "history-page-one" must be honoured:
        //   if the extractor ignores SourceId and falls back to LastResponse, it would follow the
        //   decoy and the final Received(1) assertion on the required URL would fail.
        const string requiredAbsoluteUrl =
            "https://example.test/fhir/Patient/123/_history?_count=1&ct=opaque%2Btoken";
        const string decoyAbsoluteUrl =
            "https://decoy.test/fhir/Observation/999/_history?_count=5&ct=other%2Btoken";

        var responses = new Queue<TestResponse>(new[]
        {
            new TestResponse
            {
                StatusCode = 200,
                Body = JsonSourceNodeFactory.Parse(
                    """{"resourceType":"Bundle","id":"history-page-one","link":[{"relation":"next","url":"https://example.test/fhir/Patient/123/_history?_count=1&ct=opaque%2Btoken"}]}""")
            },
            new TestResponse
            {
                StatusCode = 200,
                Body = JsonSourceNodeFactory.Parse(
                    $$"""{"resourceType":"Bundle","id":"history-page-two","link":[{"relation":"next","url":"{{decoyAbsoluteUrl}}"}]}""")
            }
        });
        // The two setup calls drain the queue; the third call (the ${nextUrl} operation) receives a
        // fresh HTTP 200 without a third queued response — keeping the queue size exactly two.
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (responses.Count > 0)
                    return responses.Dequeue();
                return new TestResponse { StatusCode = 200 };
            });

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "AbsoluteNextUrl" },
            Variables =
            [
                new VariableDefinition
                {
                    Name = "nextUrl",
                    SourceId = "history-page-one",
                    Extraction = new ExpressionExtraction("Bundle.link.where(relation = 'next').url")
                }
            ],
            Setup =
            [
                new OperationExpression { Type = "read", Resource = "Patient", ResponseId = "history-page-one" },
                new OperationExpression { Type = "read", Resource = "Patient", ResponseId = "history-page-two" }
            ],
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "UseNextUrl",
                    Actions = [new OperationExpression { Type = "read", Url = "${nextUrl}" }]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _r4Schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
        // Exactly three provider calls: two setup + one test.
        await _mockProvider.Received(3).ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>());
        // Exactly one call carries the required absolute URL — not the decoy.
        await _mockProvider.Received(1).ExecuteAsync(
            Arg.Is<TestRequest>(r => r.Url == requiredAbsoluteUrl),
            Arg.Any<CancellationToken>());
    }
}
