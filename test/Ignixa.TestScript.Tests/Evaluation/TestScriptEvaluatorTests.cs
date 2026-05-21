using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Evaluation;
using Ignixa.TestScript.Expressions;
using Ignixa.TestScript.Fixtures;
using Ignixa.TestScript.Model;
using Ignixa.TestScript.Reporting;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

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
                        new AssertExpression { Criteria = new ResponseStatusCriteria("okay") },
                        new AssertExpression { Criteria = new ResourceTypeCriteria("Patient") }
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

    [Fact]
    public async Task GivenSetupOperationFails_WhenExecuting_ThenTestsAreSkipped()
    {
        _mockClient.SendAsync(Arg.Any<FhirRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network failure"));

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "SetupFails" },
            Setup =
            [
                new OperationExpression { Type = "create", Resource = "Patient" }
            ],
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "ShouldBeSkipped",
                    Actions =
                    [
                        new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" }
                    ]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_registry, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.TestResults.ShouldBeEmpty();
        report.SetupResult.ShouldNotBeNull();
        report.SetupResult.Outcome.ShouldBe(TestScriptOutcome.Error);
    }

    [Fact]
    public async Task GivenClientThrows_WhenExecuting_ThenReportsError()
    {
        _mockClient.SendAsync(Arg.Any<FhirRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Boom"));

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "ThrowTest" },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "ReadFails",
                    Actions =
                    [
                        new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" }
                    ]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_registry, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Error);
    }

    [Fact]
    public async Task GivenCancellationRequested_WhenExecuting_ThenThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _mockClient.SendAsync(Arg.Any<FhirRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "Cancellation" },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "ReadCancelled",
                    Actions =
                    [
                        new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" }
                    ]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_registry, _fixtureProvider, _schema);

        await Should.ThrowAsync<OperationCanceledException>(
            () => evaluator.ExecuteAsync(definition, cts.Token));
    }

    [Fact]
    public async Task GivenUnresolvableFixture_WhenExecuting_ThenReportsError()
    {
        var fixtureProvider = Substitute.For<IFixtureProvider>();
#pragma warning disable CA2012
        fixtureProvider.ResolveFixtureAsync(
                Arg.Any<FixtureDefinition>(),
                Arg.Any<FixtureResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns((JsonNode?)null);
#pragma warning restore CA2012

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "BadFixture" },
            Fixtures =
            [
                new FixtureDefinition { Id = "unknown" }
            ]
        };

        var evaluator = new TestScriptEvaluator(_registry, fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.SetupResult.ShouldNotBeNull();
        report.SetupResult.Outcome.ShouldBe(TestScriptOutcome.Error);
    }

    [Fact]
    public async Task GivenVariableWithHeaderExtraction_WhenResponseHasHeader_ThenExtractsValue()
    {
        var responses = new Queue<FhirResponse>(new[]
        {
            new FhirResponse
            {
                StatusCode = 201,
                Headers = new Dictionary<string, string> { ["Location"] = "Patient/created-123" }
            },
            new FhirResponse { StatusCode = 200 }
        });

        _mockClient.SendAsync(Arg.Any<FhirRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => responses.Dequeue());

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "ExtractHeader" },
            Variables =
            [
                new VariableDefinition
                {
                    Name = "createdId",
                    Extraction = new HeaderExtraction("Location")
                }
            ],
            Setup =
            [
                new OperationExpression { Type = "create", Resource = "Patient" }
            ],
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "UseExtractedVariable",
                    Actions =
                    [
                        new OperationExpression { Type = "read", Resource = "Patient", Params = "/${createdId}" }
                    ]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_registry, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
        await _mockClient.Received().SendAsync(
            Arg.Is<FhirRequest>(r => r.Url == "http://localhost/Patient/Patient/created-123"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenVariableWithPathExtraction_WhenResponseHasBody_ThenExtractsValue()
    {
        var responses = new Queue<FhirResponse>(new[]
        {
            new FhirResponse
            {
                StatusCode = 201,
                Body = JsonNode.Parse("""{"resourceType":"Patient","id":"abc-extracted"}""")
            },
            new FhirResponse { StatusCode = 200 }
        });

        _mockClient.SendAsync(Arg.Any<FhirRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => responses.Dequeue());

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "ExtractPath" },
            Variables =
            [
                new VariableDefinition
                {
                    Name = "patientId",
                    Extraction = new PathExtraction("id")
                }
            ],
            Setup =
            [
                new OperationExpression { Type = "create", Resource = "Patient" }
            ],
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "UseExtractedId",
                    Actions =
                    [
                        new OperationExpression { Type = "read", Resource = "Patient", Params = "/${patientId}" }
                    ]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_registry, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
        await _mockClient.Received().SendAsync(
            Arg.Is<FhirRequest>(r => r.Url == "http://localhost/Patient/abc-extracted"),
            Arg.Any<CancellationToken>());
    }
}
