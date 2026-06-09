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
}
