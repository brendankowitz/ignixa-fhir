using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Evaluation;
using Ignixa.TestScript.Expressions;
using Ignixa.TestScript.Fixtures;
using Ignixa.TestScript.Model;
using Ignixa.TestScript.Reporting;
using NSubstitute;

namespace Ignixa.TestScript.Tests.Evaluation;

public class TestScriptEvaluatorTests
{
    private readonly IFhirClient _mockClient;
    private readonly IFhirClientRegistry _registry;
    private readonly IFixtureProvider _fixtureProvider;
    private readonly IFhirSchemaProvider _schema;

    public TestScriptEvaluatorTests()
    {
        _mockClient = Substitute.For<IFhirClient>();
        _mockClient.BaseUrl.Returns("http://localhost");

        _registry = new SingleClientRegistry(_mockClient);
        _fixtureProvider = new InlineFixtureProvider();
        _schema = Substitute.For<IFhirSchemaProvider>();
    }

    [Fact]
    public async Task GivenSimpleReadTest_WhenExecuting_ThenReturnsPassingReport()
    {
        _mockClient.SendAsync(Arg.Any<FhirRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FhirResponse
            {
                StatusCode = 200,
                Body = JsonNode.Parse("""{"resourceType": "Patient", "id": "123"}""")
            });

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "ReadTest" },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "ReadPatient",
                    Actions =
                    [
                        new OperationExpression
                        {
                            Type = "read",
                            Resource = "Patient",
                            Params = "/123",
                            ResponseId = "read-response"
                        },
                        new AssertExpression { Response = "okay" },
                        new AssertExpression { Resource = "Patient" }
                    ]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_registry, _fixtureProvider, _schema);

        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
        report.TestResults.Count.ShouldBe(1);
        report.TestResults[0].Name.ShouldBe("ReadPatient");
    }

    [Fact]
    public async Task GivenOperationWithVariables_WhenExecuting_ThenSubstitutesVariables()
    {
        _mockClient.SendAsync(Arg.Any<FhirRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FhirResponse { StatusCode = 200 });

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "VarTest" },
            Variables = [new VariableDefinition { Name = "id", DefaultValue = "abc" }],
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "ReadWithVar",
                    Actions =
                    [
                        new OperationExpression
                        {
                            Type = "read",
                            Resource = "Patient",
                            Params = "/${id}"
                        }
                    ]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_registry, _fixtureProvider, _schema);

        await evaluator.ExecuteAsync(definition, CancellationToken.None);

        await _mockClient.Received(1).SendAsync(
            Arg.Is<FhirRequest>(r => r.Url == "http://localhost/Patient/abc"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenEmptyTestScript_WhenExecuting_ThenReturnsPassWithNoTests()
    {
        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "Empty" }
        };

        var evaluator = new TestScriptEvaluator(_registry, _fixtureProvider, _schema);

        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
        report.TestResults.ShouldBeEmpty();
    }
}
