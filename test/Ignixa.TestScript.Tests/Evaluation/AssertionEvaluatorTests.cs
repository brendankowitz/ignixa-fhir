using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Evaluation;
using Ignixa.TestScript.Expressions;
using Ignixa.TestScript.Fixtures;
using Ignixa.TestScript.Model;
using Ignixa.TestScript.Reporting;
using Ignixa.Specification.Extensions;
using NSubstitute;

namespace Ignixa.TestScript.Tests.Evaluation;

public class AssertionEvaluatorTests
{
    private readonly ITestRequestProvider _mockProvider;
    private readonly IFixtureProvider _fixtureProvider;
    private readonly IFhirSchemaProvider _schema;
    private readonly IFhirSchemaProvider _r4Schema;

    public AssertionEvaluatorTests()
    {
        _mockProvider = Substitute.For<ITestRequestProvider>();
        _fixtureProvider = new InlineFixtureProvider();
        _schema = Substitute.For<IFhirSchemaProvider>();
        _r4Schema = FhirVersion.R4.GetSchemaProvider();
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
    [InlineData("notModified", 304, true)]
    [InlineData("notModified", 200, false)]
    [InlineData("bad", 400, true)]
    [InlineData("bad", 200, false)]
    [InlineData("forbidden", 403, true)]
    [InlineData("forbidden", 200, false)]
    [InlineData("notFound", 404, true)]
    [InlineData("notFound", 200, false)]
    [InlineData("methodNotAllowed", 405, true)]
    [InlineData("methodNotAllowed", 200, false)]
    [InlineData("conflict", 409, true)]
    [InlineData("conflict", 200, false)]
    [InlineData("gone", 410, true)]
    [InlineData("gone", 200, false)]
    [InlineData("preconditionFailed", 412, true)]
    [InlineData("preconditionFailed", 200, false)]
    [InlineData("unprocessable", 422, true)]
    [InlineData("unprocessable", 200, false)]
    [InlineData("notAKeyword", 200, false)]
    [InlineData("notAKeyword", 404, false)]
    public async Task GivenResponseCodeAssertion_WhenEvaluating_ThenMatchesCategory(
        string responseCode, int statusCode, bool expectedPass)
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = statusCode });

        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
            new AssertExpression { Criteria = new ResponseStatusCriteria(responseCode) });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
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
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = actualCode });

        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
            new AssertExpression { Criteria = new ResponseCodeCriteria(assertedCode) });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
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
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse
            {
                StatusCode = 200,
                Body = JsonSourceNodeFactory.Parse($$"""{ "resourceType": "{{actualType}}", "id": "1" }""")
            });

        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
            new AssertExpression { Criteria = new ResourceTypeCriteria(expectedType) });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        var outcome = expectedPass ? TestScriptOutcome.Pass : TestScriptOutcome.Fail;
        report.OverallOutcome.ShouldBe(outcome);
    }

    [Fact]
    public async Task GivenHeaderAssertion_WhenHeaderPresent_ThenPasses()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse
            {
                StatusCode = 200,
                Headers = new Dictionary<string, string> { ["Content-Type"] = "application/fhir+json" }.ToImmutableDictionary()
            });

        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
            new AssertExpression
            {
                Criteria = new HeaderCriteria("Content-Type", "application/fhir+json", AssertOperator.Equals)
            });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
    }

    [Fact]
    public async Task GivenHeaderContainsAssertion_WhenHeaderMatches_ThenPasses()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse
            {
                StatusCode = 200,
                Headers = new Dictionary<string, string> { ["Content-Type"] = "application/fhir+json; charset=utf-8" }.ToImmutableDictionary()
            });

        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
            new AssertExpression
            {
                Criteria = new HeaderCriteria("Content-Type", "application/fhir+json", AssertOperator.Contains)
            });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
    }

    [Fact]
    public async Task GivenFhirPathCriteria_WhenExpressionIsTrue_ThenPasses()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse
            {
                StatusCode = 200,
                Body = JsonSourceNodeFactory.Parse("""{"resourceType": "Patient", "id": "1"}""")
            });

        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
            new AssertExpression { Criteria = new FhirPathCriteria("Patient.id.exists()") });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _r4Schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
    }

    [Fact]
    public async Task GivenFhirPathCriteria_WhenEvaluating_ThenFailsWithDescriptiveMessage()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse
            {
                StatusCode = 200,
                Body = JsonSourceNodeFactory.Parse("""{"resourceType": "Patient", "id": "1"}""")
            });

        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
            new AssertExpression { Criteria = new FhirPathCriteria("Patient.name.exists()") });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _r4Schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        var failedAction = report.TestResults[0].Actions[1];
        failedAction.Outcome.ShouldBe(TestScriptOutcome.Fail);
        failedAction.Message.ShouldNotBeNull();
        failedAction.Message.ShouldNotContain("not yet implemented");
        failedAction.Message.ShouldContain("did not evaluate to true");
    }

    [Fact]
    public async Task GivenWarningOnlyAssertion_WhenFails_ThenOverallIsWarning()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse
            {
                StatusCode = 200,
                Body = JsonSourceNodeFactory.Parse("""{"resourceType": "Patient", "id": "1"}""")
            });

        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
            new AssertExpression { Criteria = new ResponseStatusCriteria("okay") },
            new AssertExpression { Criteria = new FhirPathCriteria("Patient.name.exists()"), WarningOnly = true });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _r4Schema);
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

        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 200, Headers = headers.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase) });

        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
            new AssertExpression
            {
                Criteria = new HeaderCriteria("X-Custom", null, op)
            });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
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
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse
            {
                StatusCode = 200,
                Headers = new Dictionary<string, string> { ["X-Custom"] = actualValue }.ToImmutableDictionary()
            });

        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
            new AssertExpression
            {
                Criteria = new HeaderCriteria("X-Custom", comparedTo, op)
            });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
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
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 200 });

        var opType = actualMethod == "POST" ? "create" : "read";
        var definition = BuildDefinition(
            new OperationExpression { Type = opType, Resource = "Patient", Params = "/1" },
            new AssertExpression
            {
                Criteria = new RequestMethodCriteria(assertedMethod),
                Direction = AssertDirection.Request
            });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        var outcome = expectedPass ? TestScriptOutcome.Pass : TestScriptOutcome.Fail;
        report.OverallOutcome.ShouldBe(outcome);
    }

    [Theory]
    [InlineData("Patient/1", "Patient/1", true)]
    [InlineData("Patient/2", "Patient/1", false)]
    public async Task GivenRequestUrlCriteria_WhenEvaluating_ThenMatchesUrl(
        string actualPath, string assertedUrl, bool expectedPass)
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 200 });

        var actualParams = actualPath.Replace("Patient", "", StringComparison.Ordinal);
        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = actualParams },
            new AssertExpression
            {
                Criteria = new RequestUrlCriteria(assertedUrl, AssertOperator.Equals),
                Direction = AssertDirection.Request
            });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
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
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse
            {
                StatusCode = 200,
                Headers = new Dictionary<string, string> { ["X-Custom"] = actual }.ToImmutableDictionary()
            });

        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
            new AssertExpression
            {
                Criteria = new HeaderCriteria("X-Custom", list, op)
            });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        var outcome = expectedPass ? TestScriptOutcome.Pass : TestScriptOutcome.Fail;
        report.OverallOutcome.ShouldBe(outcome);
    }

    [Fact]
    public async Task GivenFhirPathCriteria_WhenExpressionEvaluatesToTrue_ThenPasses()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse
            {
                StatusCode = 200,
                Body = JsonSourceNodeFactory.Parse("""{"resourceType": "Patient", "id": "1", "active": true}""")
            });

        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
            new AssertExpression { Criteria = new FhirPathCriteria("Patient.active = true") });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _r4Schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
    }

    [Fact]
    public async Task GivenFhirPathCriteria_WhenExpressionEvaluatesToFalse_ThenFails()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse
            {
                StatusCode = 200,
                Body = JsonSourceNodeFactory.Parse("""{"resourceType": "Patient", "id": "1", "active": false}""")
            });

        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
            new AssertExpression { Criteria = new FhirPathCriteria("Patient.active = true") });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _r4Schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Fail);
    }

    [Fact]
    public async Task GivenFhirPathCriteria_WhenNoResponseBody_ThenFails()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 200 });

        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
            new AssertExpression { Criteria = new FhirPathCriteria("Patient.id.exists()") });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _r4Schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        var failedAction = report.TestResults[0].Actions[1];
        failedAction.Outcome.ShouldBe(TestScriptOutcome.Fail);
        failedAction.Message.ShouldNotBeNull();
        failedAction.Message.ShouldContain("No response body");
    }

    [Fact]
    public async Task GivenFhirPathValueCriteria_WhenValueMatches_ThenPasses()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse
            {
                StatusCode = 200,
                Body = JsonSourceNodeFactory.Parse("""{"resourceType": "Patient", "id": "abc"}""")
            });

        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/abc" },
            new AssertExpression
            {
                Criteria = new FhirPathValueCriteria("Patient.id", "abc", AssertOperator.Equals)
            });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _r4Schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
    }

    [Fact]
    public async Task GivenFhirPathValueCriteria_WhenValueDoesNotMatch_ThenFails()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse
            {
                StatusCode = 200,
                Body = JsonSourceNodeFactory.Parse("""{"resourceType": "Patient", "id": "abc"}""")
            });

        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/abc" },
            new AssertExpression
            {
                Criteria = new FhirPathValueCriteria("Patient.id", "xyz", AssertOperator.Equals)
            });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _r4Schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Fail);
    }

    [Fact]
    public async Task GivenFhirPathValueCriteria_WhenContainsOperator_ThenEvaluatesCorrectly()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse
            {
                StatusCode = 200,
                Body = JsonSourceNodeFactory.Parse("""{"resourceType": "Patient", "id": "patient-12345"}""")
            });

        var definition = BuildDefinition(
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/patient-12345" },
            new AssertExpression
            {
                Criteria = new FhirPathValueCriteria("Patient.id", "12345", AssertOperator.Contains)
            });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _r4Schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
    }

    [Fact]
    public async Task GivenFhirPathValueCriteria_WhenVariableInExpression_ThenResolvesVariable()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse
            {
                StatusCode = 200,
                Body = JsonSourceNodeFactory.Parse("""{"resourceType": "Patient", "id": "abc"}""")
            });

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "AssertionTest" },
            Variables =
            [
                new VariableDefinition { Name = "expectedId", DefaultValue = "abc" }
            ],
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "Test",
                    Actions =
                    [
                        new OperationExpression { Type = "read", Resource = "Patient", Params = "/abc" },
                        new AssertExpression
                        {
                            Criteria = new FhirPathValueCriteria("Patient.id", "${expectedId}", AssertOperator.Equals)
                        }
                    ]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _r4Schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
    }

    [Fact]
    public async Task GivenCustomOperation_WhenTypeIsUnknown_ThenDefaultsToPost()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 200 });

        var definition = BuildDefinition(
            new OperationExpression { Type = "some-custom-op", Url = "Patient/1/$custom" });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        await evaluator.ExecuteAsync(definition, CancellationToken.None);

        await _mockProvider.Received(1).ExecuteAsync(
            Arg.Is<TestRequest>(r => r.Method == HttpMethod.Post),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenValidateOperation_WhenExecuted_ThenBuildsCorrectUrl()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 200 });

        var definition = BuildDefinition(
            new OperationExpression { Type = "validate", Url = "Patient/$validate" });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        await evaluator.ExecuteAsync(definition, CancellationToken.None);

        await _mockProvider.Received(1).ExecuteAsync(
            Arg.Is<TestRequest>(r => r.Method == HttpMethod.Post && r.Url == "Patient/$validate"),
            Arg.Any<CancellationToken>());
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
