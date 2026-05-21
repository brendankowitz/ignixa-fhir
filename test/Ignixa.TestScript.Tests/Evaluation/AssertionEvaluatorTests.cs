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

public class AssertionEvaluatorTests
{
    private readonly IFhirClient _mockClient;
    private readonly IFhirClientRegistry _registry;
    private readonly IFixtureProvider _fixtureProvider;
    private readonly IFhirSchemaProvider _schema;

    public AssertionEvaluatorTests()
    {
        _mockClient = Substitute.For<IFhirClient>();
        _mockClient.BaseUrl.Returns("http://localhost");
        _registry = new SingleClientRegistry(_mockClient);
        _fixtureProvider = new InlineFixtureProvider();
        _schema = Substitute.For<IFhirSchemaProvider>();
    }

    [Theory]
    [InlineData("okay", 200, true)]
    [InlineData("okay", 201, true)]
    [InlineData("okay", 204, true)]
    [InlineData("okay", 400, false)]
    [InlineData("created", 201, true)]
    [InlineData("created", 200, false)]
    [InlineData("noContent", 204, true)]
    [InlineData("noContent", 200, false)]
    [InlineData("bad", 400, true)]
    [InlineData("bad", 200, false)]
    [InlineData("notFound", 404, true)]
    [InlineData("notFound", 200, false)]
    public async Task GivenResponseCodeAssertion_WhenEvaluating_ThenMatchesCategory(
        string responseCode, int statusCode, bool expectedPass)
    {
        _mockClient.SendAsync(Arg.Any<FhirRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FhirResponse { StatusCode = statusCode });

        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
            new AssertExpression { Response = responseCode });

        var evaluator = new TestScriptEvaluator(_registry, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        var outcome = expectedPass ? TestScriptOutcome.Pass : TestScriptOutcome.Fail;
        report.OverallOutcome.ShouldBe(outcome);
    }

    [Theory]
    [InlineData("200", 200, true)]
    [InlineData("201", 201, true)]
    [InlineData("404", 404, true)]
    [InlineData("200", 404, false)]
    public async Task GivenExactResponseCode_WhenEvaluating_ThenMatchesExact(
        string assertedCode, int actualCode, bool expectedPass)
    {
        _mockClient.SendAsync(Arg.Any<FhirRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FhirResponse { StatusCode = actualCode });

        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
            new AssertExpression { ResponseCode = assertedCode });

        var evaluator = new TestScriptEvaluator(_registry, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        var outcome = expectedPass ? TestScriptOutcome.Pass : TestScriptOutcome.Fail;
        report.OverallOutcome.ShouldBe(outcome);
    }

    [Theory]
    [InlineData("Patient", "Patient", true)]
    [InlineData("Observation", "Patient", false)]
    public async Task GivenResourceTypeAssertion_WhenEvaluating_ThenMatchesResourceType(
        string expectedType, string actualType, bool expectedPass)
    {
        _mockClient.SendAsync(Arg.Any<FhirRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FhirResponse
            {
                StatusCode = 200,
                Body = JsonNode.Parse($$"""{ "resourceType": "{{actualType}}", "id": "1" }""")
            });

        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
            new AssertExpression { Resource = expectedType });

        var evaluator = new TestScriptEvaluator(_registry, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        var outcome = expectedPass ? TestScriptOutcome.Pass : TestScriptOutcome.Fail;
        report.OverallOutcome.ShouldBe(outcome);
    }

    [Fact]
    public async Task GivenHeaderAssertion_WhenHeaderPresent_ThenPasses()
    {
        _mockClient.SendAsync(Arg.Any<FhirRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FhirResponse
            {
                StatusCode = 200,
                Headers = new Dictionary<string, string> { ["Content-Type"] = "application/fhir+json" }
            });

        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
            new AssertExpression
            {
                HeaderField = "Content-Type",
                Value = "application/fhir+json",
                Operator = AssertOperator.Equals
            });

        var evaluator = new TestScriptEvaluator(_registry, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
    }

    [Fact]
    public async Task GivenHeaderContainsAssertion_WhenHeaderMatches_ThenPasses()
    {
        _mockClient.SendAsync(Arg.Any<FhirRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FhirResponse
            {
                StatusCode = 200,
                Headers = new Dictionary<string, string> { ["Content-Type"] = "application/fhir+json; charset=utf-8" }
            });

        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
            new AssertExpression
            {
                HeaderField = "Content-Type",
                Value = "application/fhir+json",
                Operator = AssertOperator.Contains
            });

        var evaluator = new TestScriptEvaluator(_registry, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
    }

    [Fact]
    public async Task GivenExpressionAssertion_WhenNotImplemented_ThenFails()
    {
        _mockClient.SendAsync(Arg.Any<FhirRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FhirResponse
            {
                StatusCode = 200,
                Body = JsonNode.Parse("""{"resourceType": "Patient", "id": "1"}""")
            });

        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
            new AssertExpression { Expression = "Patient.id.exists()" });

        var evaluator = new TestScriptEvaluator(_registry, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Fail);
    }

    [Fact]
    public async Task GivenWarningOnlyAssertion_WhenFails_ThenOverallStillPasses()
    {
        _mockClient.SendAsync(Arg.Any<FhirRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FhirResponse { StatusCode = 200 });

        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
            new AssertExpression { Response = "okay" },
            new AssertExpression { Expression = "Patient.id.exists()", WarningOnly = true });

        var evaluator = new TestScriptEvaluator(_registry, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
    }

    [Theory]
    [InlineData(AssertOperator.Empty, null, true)]
    [InlineData(AssertOperator.Empty, "", true)]
    [InlineData(AssertOperator.Empty, "value", false)]
    [InlineData(AssertOperator.NotEmpty, "value", true)]
    [InlineData(AssertOperator.NotEmpty, null, false)]
    [InlineData(AssertOperator.NotEmpty, "", false)]
    public async Task GivenEmptyNotEmptyOperator_WhenEvaluating_ThenMatchesCorrectly(
        AssertOperator op, string? headerValue, bool expectedPass)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (headerValue is not null)
            headers["X-Custom"] = headerValue;

        _mockClient.SendAsync(Arg.Any<FhirRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FhirResponse { StatusCode = 200, Headers = headers });

        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
            new AssertExpression
            {
                HeaderField = "X-Custom",
                Operator = op
            });

        var evaluator = new TestScriptEvaluator(_registry, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        var outcome = expectedPass ? TestScriptOutcome.Pass : TestScriptOutcome.Fail;
        report.OverallOutcome.ShouldBe(outcome);
    }

    private static TestScriptDefinition BuildDefinition(params ActionExpression[] actions)
    {
        return new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "AssertionTest" },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "Test",
                    Actions = actions.ToList()
                }
            ]
        };
    }
}
