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
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Ignixa.TestScript.Tests.Evaluation;

public class TestScriptEvaluatorTests
{
    private readonly ITestRequestProvider _mockProvider;
    private readonly IFixtureProvider _fixtureProvider;
    private readonly IFhirSchemaProvider _schema;

    public TestScriptEvaluatorTests()
    {
        _mockProvider = Substitute.For<ITestRequestProvider>();
        _fixtureProvider = new InlineFixtureProvider();
        _schema = Substitute.For<IFhirSchemaProvider>();
    }

    [Fact]
    public async Task GivenSimpleReadTest_WhenExecuting_ThenReturnsPassingReport()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse
            {
                StatusCode = 200,
                Body = JsonSourceNodeFactory.Parse("""{"resourceType": "Patient", "id": "123"}""")
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

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);

        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
        report.TestResults.Count.ShouldBe(1);
        report.TestResults[0].Name.ShouldBe("ReadPatient");
    }

    [Fact]
    public async Task GivenOperationWithVariables_WhenExecuting_ThenSubstitutesVariables()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 200 });

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

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);

        await evaluator.ExecuteAsync(definition, CancellationToken.None);

        await _mockProvider.Received(1).ExecuteAsync(
            Arg.Is<TestRequest>(r => r.Url == "Patient/abc"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenParametrizedTest_WhenExecuting_ThenEmitsOneResultPerValueWithSubstitution()
    {
        var requestedUrls = new List<string>();
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                requestedUrls.Add(call.Arg<TestRequest>().Url);
                return new TestResponse { StatusCode = 200 };
            });

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "Parametrized" },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "Observation date ge",
                    Parameters =
                    [
                        new ParametrizeDefinition("searchDate", ["2028", "2028-06", "2028-06-15"])
                    ],
                    Actions =
                    [
                        new OperationExpression
                        {
                            Type = "search",
                            Resource = "Observation",
                            Params = "?date=ge${searchDate}"
                        }
                    ]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.TestResults.Count.ShouldBe(3);
        report.TestResults.Select(r => r.Name).ShouldBe(
        [
            "Observation date ge [2028]",
            "Observation date ge [2028-06]",
            "Observation date ge [2028-06-15]"
        ]);

        requestedUrls.ShouldBe(
        [
            "Observation?date=ge2028",
            "Observation?date=ge2028-06",
            "Observation?date=ge2028-06-15"
        ]);
    }

    [Fact]
    public async Task GivenParametrizedTest_WhenExecuting_ThenInjectedVariableDoesNotLeakAcrossIterations()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 200 });

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "NoLeak" },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "Parametrized",
                    Parameters = [new ParametrizeDefinition("searchDate", ["a", "b"])],
                    Actions =
                    [
                        new OperationExpression { Type = "search", Resource = "Observation", Params = "?d=${searchDate}" }
                    ]
                },
                new TestPhaseDefinition
                {
                    Name = "PlainAfter",
                    Actions =
                    [
                        new OperationExpression { Type = "search", Resource = "Observation", Params = "?d=${searchDate}" }
                    ]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.TestResults.Count.ShouldBe(3);
        report.TestResults[2].Name.ShouldBe("PlainAfter");

        // searchDate was injected only for the parametrized iterations; the later plain test must
        // not see it, so resolving ${searchDate} fails and the operation records an Error.
        report.TestResults[2].Outcome.ShouldBe(TestScriptOutcome.Error);
    }

    [Fact]
    public async Task GivenEmptyTestScript_WhenExecuting_ThenReturnsPassWithNoTests()
    {
        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "Empty" }
        };

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);

        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
        report.TestResults.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenSetupOperationFails_WhenExecuting_ThenTestsAreSkipped()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
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

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.TestResults.ShouldBeEmpty();
        report.SetupResult.ShouldNotBeNull();
        report.SetupResult.Outcome.ShouldBe(TestScriptOutcome.Error);
    }

    [Fact]
    public async Task GivenClientThrows_WhenExecuting_ThenReportsError()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
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

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Error);
    }

    [Fact]
    public async Task GivenOperationWithDestinationGreaterThanOne_WhenExecuting_ThenReportsError()
    {
        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "DestinationTest" },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "MultiDestination",
                    Actions =
                    [
                        new OperationExpression { Type = "read", Resource = "Patient", Params = "/1", Destination = 2 }
                    ]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Error);
    }

    [Fact]
    public async Task GivenOperationWithDestinationEqualToOne_WhenExecuting_ThenProceedsNormally()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 200 });

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "DestinationOneTest" },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "SingleDestination",
                    Actions =
                    [
                        new OperationExpression { Type = "read", Resource = "Patient", Params = "/1", Destination = 1 }
                    ]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
    }

    [Fact]
    public async Task GivenCancellationRequested_WhenExecuting_ThenThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
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

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);

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
            .Returns((ResourceJsonNode?)null);
#pragma warning restore CA2012

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "BadFixture" },
            Fixtures =
            [
                new FixtureDefinition { Id = "unknown" }
            ]
        };

        var evaluator = new TestScriptEvaluator(_mockProvider, fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.SetupResult.ShouldNotBeNull();
        report.SetupResult.Outcome.ShouldBe(TestScriptOutcome.Error);
    }

    [Fact]
    public async Task GivenVariableWithHeaderExtraction_WhenResponseHasHeader_ThenExtractsValue()
    {
        var responses = new Queue<TestResponse>(new[]
        {
            new TestResponse
            {
                StatusCode = 201,
                Headers = new Dictionary<string, string> { ["Location"] = "Patient/created-123" }
            },
            new TestResponse { StatusCode = 200 }
        });

        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
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

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
        await _mockProvider.Received().ExecuteAsync(
            Arg.Is<TestRequest>(r => r.Url == "Patient/Patient/created-123"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenVariableWithPathExtraction_WhenResponseHasBody_ThenExtractsValue()
    {
        var responses = new Queue<TestResponse>(new[]
        {
            new TestResponse
            {
                StatusCode = 201,
                Body = JsonSourceNodeFactory.Parse("""{"resourceType":"Patient","id":"abc-extracted"}""")
            },
            new TestResponse { StatusCode = 200 }
        });

        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
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

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
        await _mockProvider.Received().ExecuteAsync(
            Arg.Is<TestRequest>(r => r.Url == "Patient/abc-extracted"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenAutocreateFixture_WhenVariableDefinedWithFixtureSourceId_ThenCapturesServerId()
    {
        var fixtureResource = JsonSourceNodeFactory.Parse("""{"resourceType":"Patient"}""");
        var fixtureProvider = Substitute.For<IFixtureProvider>();
#pragma warning disable CA2012
        fixtureProvider.ResolveFixtureAsync(
                Arg.Any<FixtureDefinition>(),
                Arg.Any<FixtureResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(fixtureResource);
#pragma warning restore CA2012

        var responses = new Queue<TestResponse>(new[]
        {
            new TestResponse
            {
                StatusCode = 201,
                Body = JsonSourceNodeFactory.Parse("""{"resourceType":"Patient","id":"server-assigned-99"}""")
            },
            new TestResponse { StatusCode = 200 }
        });

        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => responses.Dequeue());

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "AutocreateExtract" },
            Fixtures =
            [
                new FixtureDefinition { Id = "patient-fixture", Autocreate = true }
            ],
            Variables =
            [
                new VariableDefinition
                {
                    Name = "createdId",
                    SourceId = "patient-fixture",
                    Extraction = new PathExtraction("id")
                }
            ],
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "ReadCreated",
                    Actions =
                    [
                        new OperationExpression { Type = "read", Resource = "Patient", Params = "/${createdId}" }
                    ]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_mockProvider, fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
        await _mockProvider.Received().ExecuteAsync(
            Arg.Is<TestRequest>(r => r.Url == "Patient/server-assigned-99"),
            Arg.Any<CancellationToken>());
    }
}
