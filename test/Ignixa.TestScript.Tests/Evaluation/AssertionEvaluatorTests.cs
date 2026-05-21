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
            new AssertExpression { Criteria = new ResponseStatusCriteria(responseCode) });

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
            new AssertExpression { Criteria = new ResponseCodeCriteria(assertedCode) });

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
            new AssertExpression { Criteria = new ResourceTypeCriteria(expectedType) });

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
                Criteria = new HeaderCriteria("Content-Type", "application/fhir+json", AssertOperator.Equals)
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
                Criteria = new HeaderCriteria("Content-Type", "application/fhir+json", AssertOperator.Contains)
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
            new AssertExpression { Criteria = new FhirPathCriteria("Patient.id.exists()") });

        var evaluator = new TestScriptEvaluator(_registry, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Fail);
    }

    [Fact]
    public async Task GivenFhirPathCriteria_WhenEvaluating_ThenFailsWithDescriptiveMessage()
    {
        _mockClient.SendAsync(Arg.Any<FhirRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FhirResponse
            {
                StatusCode = 200,
                Body = JsonNode.Parse("""{"resourceType": "Patient", "id": "1"}""")
            });

        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
            new AssertExpression { Criteria = new FhirPathCriteria("Patient.name.exists()") });

        var evaluator = new TestScriptEvaluator(_registry, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        var failedAction = report.TestResults[0].Actions[1];
        failedAction.Outcome.ShouldBe(TestScriptOutcome.Fail);
        failedAction.Message.ShouldNotBeNull();
        failedAction.Message.ShouldContain("not yet implemented");
    }

    [Fact]
    public async Task GivenWarningOnlyAssertion_WhenFails_ThenOverallIsWarning()
    {
        _mockClient.SendAsync(Arg.Any<FhirRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FhirResponse { StatusCode = 200 });

        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
            new AssertExpression { Criteria = new ResponseStatusCriteria("okay") },
            new AssertExpression { Criteria = new FhirPathCriteria("Patient.id.exists()"), WarningOnly = true });

        var evaluator = new TestScriptEvaluator(_registry, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Warning);
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
                Criteria = new HeaderCriteria("X-Custom", null, op)
            });

        var evaluator = new TestScriptEvaluator(_registry, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        var outcome = expectedPass ? TestScriptOutcome.Pass : TestScriptOutcome.Fail;
        report.OverallOutcome.ShouldBe(outcome);
    }

    [Theory]
    [InlineData(AssertOperator.GreaterThan, "b", "a", true)]
    [InlineData(AssertOperator.GreaterThan, "a", "b", false)]
    [InlineData(AssertOperator.LessThan, "a", "b", true)]
    [InlineData(AssertOperator.LessThan, "b", "a", false)]
    public async Task GivenGreaterLessThanOperator_WhenEvaluating_ThenComparesCorrectly(
        AssertOperator op, string actualValue, string comparedTo, bool expectedPass)
    {
        _mockClient.SendAsync(Arg.Any<FhirRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FhirResponse
            {
                StatusCode = 200,
                Headers = new Dictionary<string, string> { ["X-Custom"] = actualValue }
            });

        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
            new AssertExpression
            {
                Criteria = new HeaderCriteria("X-Custom", comparedTo, op)
            });

        var evaluator = new TestScriptEvaluator(_registry, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        var outcome = expectedPass ? TestScriptOutcome.Pass : TestScriptOutcome.Fail;
        report.OverallOutcome.ShouldBe(outcome);
    }

    [Theory]
    [InlineData("GET", "GET", true)]
    [InlineData("GET", "POST", false)]
    public async Task GivenRequestMethodCriteria_WhenEvaluating_ThenMatchesMethod(
        string actualMethod, string assertedMethod, bool expectedPass)
    {
        _mockClient.SendAsync(Arg.Any<FhirRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FhirResponse { StatusCode = 200 });

        var opType = actualMethod == "POST" ? "create" : "read";
        var definition = BuildDefinition(
            new OperationExpression { Type = opType, Resource = "Patient", Params = "/1" },
            new AssertExpression
            {
                Criteria = new RequestMethodCriteria(assertedMethod),
                Direction = AssertDirection.Request
            });

        var evaluator = new TestScriptEvaluator(_registry, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        var outcome = expectedPass ? TestScriptOutcome.Pass : TestScriptOutcome.Fail;
        report.OverallOutcome.ShouldBe(outcome);
    }

    [Theory]
    [InlineData("http://localhost/Patient/1", "http://localhost/Patient/1", true)]
    [InlineData("http://localhost/Patient/2", "http://localhost/Patient/1", false)]
    public async Task GivenRequestUrlCriteria_WhenEvaluating_ThenMatchesUrl(
        string actualPath, string assertedUrl, bool expectedPass)
    {
        _mockClient.SendAsync(Arg.Any<FhirRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FhirResponse { StatusCode = 200 });

        var actualParams = actualPath.Replace("http://localhost/Patient", "", StringComparison.Ordinal);
        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = actualParams },
            new AssertExpression
            {
                Criteria = new RequestUrlCriteria(assertedUrl, AssertOperator.Equals),
                Direction = AssertDirection.Request
            });

        var evaluator = new TestScriptEvaluator(_registry, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        var outcome = expectedPass ? TestScriptOutcome.Pass : TestScriptOutcome.Fail;
        report.OverallOutcome.ShouldBe(outcome);
    }

    [Theory]
    [InlineData("a,b,c", "b", AssertOperator.In, true)]
    [InlineData("a,b,c", "d", AssertOperator.In, false)]
    [InlineData("a,b,c", "b", AssertOperator.NotIn, false)]
    [InlineData("a,b,c", "d", AssertOperator.NotIn, true)]
    public async Task GivenInNotInOperator_WhenEvaluating_ThenChecksListMembership(
        string list, string actual, AssertOperator op, bool expectedPass)
    {
        _mockClient.SendAsync(Arg.Any<FhirRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FhirResponse
            {
                StatusCode = 200,
                Headers = new Dictionary<string, string> { ["X-Custom"] = actual }
            });

        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
            new AssertExpression
            {
                Criteria = new HeaderCriteria("X-Custom", list, op)
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
