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
                    Parameters = new ParametrizeDefinition("searchDate", ["2028", "2028-06", "2028-06-15"]),
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
                    Parameters = new ParametrizeDefinition("searchDate", ["a", "b"]),
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
    public async Task GivenTestTaggedForR4_WhenExecutingAgainstR5_ThenTestIsSkipped()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 200 });

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "VersionGated" },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "R4Only",
                    FhirVersions = ["4.0"],
                    Actions =
                    [
                        new OperationExpression { Type = "search", Resource = "Patient", Params = "?identifier:of-type=x" }
                    ]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None, fhirVersion: "5.0");

        report.TestResults.Count.ShouldBe(1);
        report.TestResults[0].Outcome.ShouldBe(TestScriptOutcome.Skip);
        await _mockProvider.DidNotReceive().ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenTestTaggedForR4_WhenExecutingWithNoVersion_ThenTestRuns()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 200 });

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "VersionGatedNoVersion" },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "R4Only",
                    FhirVersions = ["4.0"],
                    Actions =
                    [
                        new OperationExpression { Type = "search", Resource = "Patient", Params = "?identifier:of-type=x" }
                    ]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None, fhirVersion: null);

        report.TestResults.Count.ShouldBe(1);
        report.TestResults[0].Outcome.ShouldNotBe(TestScriptOutcome.Skip);
        await _mockProvider.Received(1).ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenTestWithNoVersionTag_WhenExecutingAgainstR5_ThenTestRuns()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 200 });

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "VersionAgnostic" },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "AnyVersion",
                    Actions =
                    [
                        new OperationExpression { Type = "search", Resource = "Patient", Params = "?name=smith" }
                    ]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None, fhirVersion: "5.0");

        report.TestResults.Count.ShouldBe(1);
        report.TestResults[0].Outcome.ShouldNotBe(TestScriptOutcome.Skip);
        await _mockProvider.Received(1).ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>());
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

        report.SetupResult.ShouldNotBeNull();
        report.SetupResult.Outcome.ShouldBe(TestScriptOutcome.Error);
        report.TestResults.Count.ShouldBe(1);
        report.TestResults[0].Name.ShouldBe("ShouldBeSkipped");
        report.TestResults[0].Outcome.ShouldBe(TestScriptOutcome.Skip);
        report.TestResults[0].Actions[0].Message.ShouldNotBeNull();
        report.TestResults[0].Actions[0].Message!.ShouldContain("setup failed");
    }

    [Fact]
    public async Task GivenSetupAssertPasses_WhenExecuting_ThenTestsRun()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 201 });

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "SetupAssertPass" },
            Setup =
            [
                new OperationExpression { Type = "create", Resource = "Patient" },
                new AssertExpression { Criteria = new ResponseStatusCriteria("created") }
            ],
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "ShouldRun",
                    Actions =
                    [
                        new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" }
                    ]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.SetupResult.ShouldNotBeNull();
        report.SetupResult.Outcome.ShouldBe(TestScriptOutcome.Pass);
        report.TestResults.Count.ShouldBe(1);
        report.TestResults[0].Name.ShouldBe("ShouldRun");
    }

    [Fact]
    public async Task GivenSetupAssertFails_WhenExecuting_ThenSetupFailsAndTestsAreSkipped()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 500 });

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "SetupAssertFail" },
            Setup =
            [
                new OperationExpression { Type = "create", Resource = "Patient" },
                new AssertExpression { Criteria = new ResponseStatusCriteria("created") }
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

        report.SetupResult.ShouldNotBeNull();
        report.SetupResult.Outcome.ShouldBe(TestScriptOutcome.Fail);
        report.TestResults.Count.ShouldBe(1);
        report.TestResults[0].Outcome.ShouldBe(TestScriptOutcome.Skip);
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
                Headers = new Dictionary<string, string> { ["Location"] = "Patient/created-123" }.ToImmutableDictionary()
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

    [Fact]
    public async Task GivenSearchOperation_WhenMethodIsPost_ThenUrlIsResourceSlashSearch()
    {
        TestRequest? capturedRequest = null;
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                capturedRequest = call.Arg<TestRequest>();
                return new TestResponse { StatusCode = 200 };
            });

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "PostSearch" },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "PostSearchUrl",
                    Actions =
                    [
                        new OperationExpression
                        {
                            Type = "search",
                            Resource = "Patient",
                            Params = "?_has:Observation:subject:code=8867-4",
                            Method = HttpMethod.Post,
                            ResponseId = "post-search-response"
                        }
                    ]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        await evaluator.ExecuteAsync(definition, CancellationToken.None);

        capturedRequest.ShouldNotBeNull();
        capturedRequest.Url.ShouldBe("Patient/_search");
        capturedRequest.Method.ShouldBe(HttpMethod.Post);
    }

    [Fact]
    public async Task GivenAssertWithBodyParseError_WhenEvaluatingFhirPath_ThenFailsWithParseErrorMessage()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 200, Body = null, BodyParseError = "Unexpected token at position 0" });

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "ParseErrorTest" },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "FhirPathOnBadJson",
                    Actions =
                    [
                        new OperationExpression { Type = "read", Resource = "Patient" },
                        new AssertExpression { Criteria = new FhirPathCriteria("Patient.id.exists()") }
                    ]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        var assertAction = report.TestResults[0].Actions[1];
        assertAction.Outcome.ShouldBe(TestScriptOutcome.Fail);
        assertAction.Message.ShouldNotBeNullOrEmpty();
        assertAction.Message!.ShouldContain("Unexpected token at position 0");
    }

    [Fact]
    public async Task GivenAutodeleteFixtureWithNoId_WhenTearingDown_ThenRecordsError()
    {
        var fixtureProvider = Substitute.For<IFixtureProvider>();
#pragma warning disable CA2012
        fixtureProvider.ResolveFixtureAsync(
                Arg.Any<FixtureDefinition>(),
                Arg.Any<FixtureResolutionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(JsonSourceNodeFactory.Parse("""{"resourceType":"Patient"}"""));
#pragma warning restore CA2012

        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse
            {
                StatusCode = 201,
                Body = JsonSourceNodeFactory.Parse("""{"resourceType":"Patient"}""")
            });

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "AutodeleteNoId" },
            Fixtures = [new FixtureDefinition { Id = "no-id-fixture", Autocreate = true, Autodelete = true }]
        };

        var evaluator = new TestScriptEvaluator(_mockProvider, fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.TeardownResult.ShouldNotBeNull();
        report.TeardownResult!.Actions.ShouldContain(a =>
            a.Outcome == TestScriptOutcome.Error &&
            a.Message != null &&
            a.Message.Contains("no server-assigned id"));
    }

    [Fact]
    public async Task GivenAssertWithUnknownCriteriaType_WhenEvaluating_ThenRecordsErrorNotCrash()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 200 });

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "UnknownCriteria" },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "BadAssert",
                    Actions =
                    [
                        new OperationExpression { Type = "read", Resource = "Patient" },
                        new AssertExpression { Criteria = new UnknownCriteria() }
                    ]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        var assertAction = report.TestResults[0].Actions[1];
        assertAction.Outcome.ShouldBe(TestScriptOutcome.Error);
        assertAction.Message.ShouldNotBeNullOrEmpty();
    }

    private sealed record UnknownCriteria : AssertCriteria;

    [Fact]
    public async Task GivenSearchOperation_WhenMethodIsPost_ThenParamsAreFormEncoded()
    {
        TestRequest? capturedRequest = null;
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                capturedRequest = call.Arg<TestRequest>();
                return new TestResponse { StatusCode = 200 };
            });

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "PostSearchFormBody" },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "PostSearchBody",
                    Actions =
                    [
                        new OperationExpression
                        {
                            Type = "search",
                            Resource = "Patient",
                            Params = "?_has:Observation:subject:code=8867-4",
                            Method = HttpMethod.Post,
                            ResponseId = "post-search-response"
                        }
                    ]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        await evaluator.ExecuteAsync(definition, CancellationToken.None);

        capturedRequest.ShouldNotBeNull();
        capturedRequest.FormBody.ShouldBe("_has:Observation:subject:code=8867-4");
        capturedRequest.Body.ShouldBeNull();
    }

    [Fact]
    public async Task GivenProviderThrowsTaskCanceled_WhenTokenNotCancelled_ThenRecordsErrorAndSuiteContinues()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "Timeout" },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "ReadTimesOut",
                    Actions = [new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" }]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);

        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Error);
        report.TestResults.Count.ShouldBe(1);
        var action = report.TestResults[0].Actions[0];
        action.Outcome.ShouldBe(TestScriptOutcome.Error);
        action.Message.ShouldNotBeNull();
        action.Message!.ShouldContain("timed out");
    }

    [Fact]
    public async Task GivenAssertWithUnknownSourceId_WhenEvaluating_ThenRecordsErrorNamingSourceId()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 200 });

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "UnknownAssertSource" },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "BadSource",
                    Actions =
                    [
                        new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
                        new AssertExpression
                        {
                            Criteria = new ResponseStatusCriteria("okay"),
                            SourceId = "does-not-exist"
                        }
                    ]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        var assertAction = report.TestResults[0].Actions[1];
        assertAction.Outcome.ShouldBe(TestScriptOutcome.Error);
        assertAction.Message.ShouldNotBeNull();
        assertAction.Message!.ShouldContain("does-not-exist");
    }

    [Fact]
    public async Task GivenAssertWithSourceId_WhenEarlierResponseMatchesButLastDoesNot_ThenHonorsSourceId()
    {
        var responses = new Queue<TestResponse>(new[]
        {
            new TestResponse { StatusCode = 201 },
            new TestResponse { StatusCode = 500 }
        });
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => responses.Dequeue());

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "SourceIdHonored" },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "TwoOps",
                    Actions =
                    [
                        new OperationExpression { Type = "create", Resource = "Patient", ResponseId = "first" },
                        new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
                        new AssertExpression
                        {
                            Criteria = new ResponseStatusCriteria("created"),
                            SourceId = "first"
                        }
                    ]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.TestResults[0].Actions[2].Outcome.ShouldBe(TestScriptOutcome.Pass);
    }

    [Fact]
    public async Task GivenOperationWithUnknownSourceId_WhenBuildingRequest_ThenRecordsOperationError()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 200 });

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "UnknownOpSource" },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "PostUnknownBody",
                    Actions =
                    [
                        new OperationExpression { Type = "create", Resource = "Patient", SourceId = "no-such-fixture" }
                    ]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        var action = report.TestResults[0].Actions[0];
        action.Outcome.ShouldBe(TestScriptOutcome.Error);
        action.Message.ShouldNotBeNull();
        action.Message!.ShouldContain("no-such-fixture");
        await _mockProvider.DidNotReceive().ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenContentTypeWithCharsetParameter_WhenAsserting_ThenMatchesOnMediaType()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse
            {
                StatusCode = 200,
                Headers = new Dictionary<string, string> { ["Content-Type"] = "application/fhir+json; charset=utf-8" }.ToImmutableDictionary()
            });

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "ContentTypeCharset" },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "Check",
                    Actions =
                    [
                        new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
                        new AssertExpression { Criteria = new ContentTypeCriteria("application/fhir+json") }
                    ]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.TestResults[0].Actions[1].Outcome.ShouldBe(TestScriptOutcome.Pass);
    }

    [Fact]
    public async Task GivenResourceTypeAssertWithParseError_WhenBodyFailedToParse_ThenMessageReportsParseError()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 200, Body = null, BodyParseError = "Unexpected token at position 0" });

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "ResourceTypeParseError" },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "Check",
                    Actions =
                    [
                        new OperationExpression { Type = "read", Resource = "Patient" },
                        new AssertExpression { Criteria = new ResourceTypeCriteria("Patient") }
                    ]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        var assertAction = report.TestResults[0].Actions[1];
        assertAction.Outcome.ShouldBe(TestScriptOutcome.Fail);
        assertAction.Message.ShouldNotBeNull();
        assertAction.Message!.ShouldContain("Unexpected token at position 0");
    }

    [Fact]
    public async Task GivenOperationWithEncodeRequestUrlFalse_WhenExecuting_ThenRecordsWarning()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 200 });

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "EncodeRequestUrlFalse" },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "NoEncode",
                    Actions =
                    [
                        new OperationExpression { Type = "read", Resource = "Patient", Params = "/1", EncodeRequestUrl = false }
                    ]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.TestResults[0].Actions.ShouldContain(a =>
            a.Outcome == TestScriptOutcome.Warning &&
            a.Message != null &&
            a.Message.Contains("encodeRequestUrl=false"));
    }

    [Fact]
    public async Task GivenFhirPathValueGreaterThan_WhenComparingNumbers_ThenComparesNumerically()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse
            {
                StatusCode = 200,
                Headers = new Dictionary<string, string> { ["X-Count"] = "9" }.ToImmutableDictionary()
            });

        var definition = new TestScriptDefinition
        {
            Metadata = new TestScriptMetadata { Name = "NumericCompare" },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "Compare",
                    Actions =
                    [
                        new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" },
                        new AssertExpression
                        {
                            Criteria = new HeaderCriteria("X-Count", "10", AssertOperator.LessThan)
                        }
                    ]
                }
            ]
        };

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.TestResults[0].Actions[1].Outcome.ShouldBe(TestScriptOutcome.Pass);
    }
}
