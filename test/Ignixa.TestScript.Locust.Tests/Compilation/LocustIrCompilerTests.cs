using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Ignixa.TestScript.Expressions;
using Ignixa.TestScript.Locust.Compatibility;
using Ignixa.TestScript.Locust.Compilation;
using Ignixa.TestScript.Locust.Diagnostics;
using Ignixa.TestScript.Locust.Ir;
using Ignixa.TestScript.Model;

namespace Ignixa.TestScript.Locust.Tests.Compilation;

public class LocustIrCompilerTests
{
    private static readonly LocustIrCompiler s_compiler = new();

    private const string FhirFakesExtensionUrl = "http://ignixa.io/testscript/fhirfakes";

    private static LocustCompilerOptions Options(
        string source = "read.json", string? fhirVersion = "4.0", int fixtureVariants = 0) => new(
        source,
        fhirVersion,
        FhirVersion.R4.GetSchemaProvider(),
        fixtureVariants);

    private static FixtureDefinition FhirFakesPatientFixture(
        string id = "fakes-fixture", bool autocreate = false, bool autodelete = false) => new()
    {
        Id = id,
        Autocreate = autocreate,
        Autodelete = autodelete,
        Resource = ResourceJsonNode.Parse($$"""
            {
                "resourceType": "Basic",
                "extension": [
                    { "url": "{{FhirFakesExtensionUrl}}", "valueCode": "Patient" }
                ]
            }
            """)
    };

    private static FixtureDefinition LiteralPatientFixture(
        string id = "literal-fixture", bool autocreate = false, bool autodelete = false) => new()
    {
        Id = id,
        Autocreate = autocreate,
        Autodelete = autodelete,
        Resource = ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{id}}-1"}""")
    };

    [Fact]
    public async Task GivenRepresentativeSupportedDefinition_WhenCompiled_ThenNoErrorsAndDocumentMatchesShape()
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Read patient" },
            Variables =
            [
                new VariableDefinition
                {
                    Name = "patientId",
                    SourceId = "created",
                    Extraction = new ExpressionExtraction("Patient.id")
                }
            ],
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "read",
                    Actions =
                    [
                        new OperationExpression
                        {
                            Type = "read",
                            Resource = "Patient",
                            Params = "/${patientId}",
                            ResponseId = "read-response"
                        },
                        new AssertExpression
                        {
                            Criteria = new FhirPathCriteria("Patient.id.exists()"),
                            SourceId = "read-response"
                        }
                    ]
                }
            ]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options(), CancellationToken.None);

        result.HasErrors.ShouldBeFalse();
        result.Document.ShouldNotBeNull();

        LocustIrVariable variable = result.Document.Variables.ShouldHaveSingleItem();
        variable.Name.ShouldBe("patientId");
        variable.SourceId.ShouldBe("created");
        variable.ExtractionKind.ShouldBe(LocustIrVariableExtractionKind.FhirPath);
        variable.Selector.ShouldBe("Patient.id");

        LocustIrTest test = result.Document.Tests.ShouldHaveSingleItem();
        test.Id.ShouldBe("test.0");
        test.Name.ShouldBe("read");

        LocustIrOperation operation = test.Actions[0].ShouldBeOfType<LocustIrOperation>();
        operation.Id.ShouldBe("test.0.action.0");
        operation.Method.ShouldBe("GET");
        operation.Params.ShouldBe("/${patientId}");
        operation.ResponseId.ShouldBe("read-response");

        LocustIrAssertion assertion = test.Actions[1].ShouldBeOfType<LocustIrAssertion>();
        assertion.Id.ShouldBe("test.0.action.1");
        assertion.Criteria.Kind.ShouldBe(LocustIrAssertionKind.FhirPath);
        assertion.Criteria.Expression.ShouldBe("Patient.id.exists()");
        assertion.SourceId.ShouldBe("read-response");
    }

    [Fact]
    public async Task GivenSetupTestsAndTeardown_WhenCompiled_ThenActionIdsAreStable()
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Setup = [new OperationExpression { Type = "create", Resource = "Patient" }],
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "case-one",
                    Actions =
                    [
                        new OperationExpression { Type = "read", Resource = "Patient" },
                        new AssertExpression { Criteria = new ResponseStatusCriteria("200") }
                    ]
                },
                new TestPhaseDefinition
                {
                    Name = "case-two",
                    Actions = [new OperationExpression { Type = "delete", Resource = "Patient" }]
                }
            ],
            Teardown = [new OperationExpression { Type = "delete", Resource = "Patient" }]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options(), CancellationToken.None);

        result.HasErrors.ShouldBeFalse();
        LocustIrDocument document = result.Document.ShouldNotBeNull();

        document.Setup[0].Id.ShouldBe("setup.0");
        document.Tests[0].Id.ShouldBe("test.0");
        document.Tests[0].Actions[0].Id.ShouldBe("test.0.action.0");
        document.Tests[0].Actions[1].Id.ShouldBe("test.0.action.1");
        document.Tests[1].Id.ShouldBe("test.1");
        document.Tests[1].Actions[0].Id.ShouldBe("test.1.action.0");
        document.Teardown[0].Id.ShouldBe("teardown.0");
    }

    [Fact]
    public async Task GivenSetupTestAndTeardownActions_WhenCompiled_ThenMetricInfoDiagnosticsCoverLifecycleAndOnlyFirstGroupMember()
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Setup = [new OperationExpression { Type = "create", Resource = "Patient" }],
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "case",
                    Actions =
                    [
                        new OperationExpression { Type = "read", Resource = "Patient" },
                        new AssertExpression { Criteria = new ResponseStatusCriteria("200"), AnyOfGroupId = "g1" },
                        new AssertExpression { Criteria = new ResponseStatusCriteria("201"), AnyOfGroupId = "g1" },
                        new AssertExpression { Criteria = new ResponseCodeCriteria("ok") }
                    ]
                }
            ],
            Teardown = [new OperationExpression { Type = "delete", Resource = "Patient" }]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options("metrics.json"), CancellationToken.None);

        result.HasErrors.ShouldBeFalse();

        List<LocustDiagnostic> metrics = [.. result.Diagnostics.Where(d => d.Code == "LOCUST_METRIC")];

        metrics.Count.ShouldBe(5);

        metrics[0].Source.ShouldBe("metrics.json:setup:action:0");
        metrics[0].Message.ShouldBe("Metric 'metrics.json::setup.0'");
        metrics[0].Severity.ShouldBe(LocustDiagnosticSeverity.Info);

        metrics[1].Source.ShouldBe("metrics.json:test:case:action:0");
        metrics[1].Message.ShouldBe("Metric 'metrics.json::test.0.action.0'");
        metrics[1].Severity.ShouldBe(LocustDiagnosticSeverity.Info);

        metrics[2].Source.ShouldBe("metrics.json:test:case:action:1");
        metrics[2].Message.ShouldBe("Metric 'metrics.json::test.0.action.1'");

        metrics[3].Source.ShouldBe("metrics.json:test:case:action:3");
        metrics[3].Message.ShouldBe("Metric 'metrics.json::test.0.action.3'");

        metrics[4].Source.ShouldBe("metrics.json:teardown:action:0");
        metrics[4].Message.ShouldBe("Metric 'metrics.json::teardown.0'");
        metrics[4].Severity.ShouldBe(LocustDiagnosticSeverity.Info);
    }

    [Theory]
    [InlineData("create", "POST")]
    [InlineData("read", "GET")]
    [InlineData("vread", "GET")]
    [InlineData("search", "GET")]
    [InlineData("history", "GET")]
    [InlineData("capabilities", "GET")]
    [InlineData("conforms", "GET")]
    [InlineData("update", "PUT")]
    [InlineData("updateCreate", "PUT")]
    [InlineData("patch", "PATCH")]
    [InlineData("delete", "DELETE")]
    [InlineData("$custom-operation", "POST")]
    public async Task GivenOperationType_WhenCompiled_ThenMethodMatchesEvaluatorDerivation(string type, string expectedMethod)
    {
        TestScriptDefinition definition = SingleSetupOperation(new OperationExpression { Type = type, Resource = "Patient" });

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options(), CancellationToken.None);

        result.HasErrors.ShouldBeFalse();
        LocustIrOperation operation = result.Document.ShouldNotBeNull().Setup[0].ShouldBeOfType<LocustIrOperation>();
        operation.Method.ShouldBe(expectedMethod);
    }

    [Fact]
    public async Task GivenExplicitMethodOverride_WhenCompiled_ThenExplicitMethodWins()
    {
        TestScriptDefinition definition = SingleSetupOperation(new OperationExpression
        {
            Type = "create",
            Resource = "Patient",
            Method = HttpMethod.Get
        });

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options(), CancellationToken.None);

        result.HasErrors.ShouldBeFalse();
        LocustIrOperation operation = result.Document.ShouldNotBeNull().Setup[0].ShouldBeOfType<LocustIrOperation>();
        operation.Method.ShouldBe("GET");
    }

    [Fact]
    public async Task GivenResponseStatusCriteria_WhenCompiled_ThenMapsToResponseStatusKind()
    {
        LocustIrAssertionCriteria criteria = await CompileSingleAssertCriteria(new ResponseStatusCriteria("200"));

        criteria.Kind.ShouldBe(LocustIrAssertionKind.ResponseStatus);
        criteria.Value.ShouldBe("200");
    }

    [Fact]
    public async Task GivenResponseCodeCriteria_WhenCompiled_ThenMapsToResponseCodeKind()
    {
        LocustIrAssertionCriteria criteria = await CompileSingleAssertCriteria(new ResponseCodeCriteria("ok"));

        criteria.Kind.ShouldBe(LocustIrAssertionKind.ResponseCode);
        criteria.Value.ShouldBe("ok");
    }

    [Fact]
    public async Task GivenContentTypeCriteria_WhenCompiled_ThenMapsToContentTypeKind()
    {
        LocustIrAssertionCriteria criteria = await CompileSingleAssertCriteria(new ContentTypeCriteria("application/fhir+json"));

        criteria.Kind.ShouldBe(LocustIrAssertionKind.ContentType);
        criteria.Value.ShouldBe("application/fhir+json");
    }

    [Fact]
    public async Task GivenResourceTypeCriteria_WhenCompiled_ThenMapsToResourceTypeKind()
    {
        LocustIrAssertionCriteria criteria = await CompileSingleAssertCriteria(new ResourceTypeCriteria("Patient"));

        criteria.Kind.ShouldBe(LocustIrAssertionKind.ResourceType);
        criteria.Value.ShouldBe("Patient");
    }

    [Fact]
    public async Task GivenHeaderCriteriaWithOperator_WhenCompiled_ThenMapsFieldValueAndOperator()
    {
        LocustIrAssertionCriteria criteria = await CompileSingleAssertCriteria(
            new HeaderCriteria("ETag", "W/\"1\"", AssertOperator.Equals));

        criteria.Kind.ShouldBe(LocustIrAssertionKind.Header);
        criteria.Field.ShouldBe("ETag");
        criteria.Value.ShouldBe("W/\"1\"");
        criteria.Operator.ShouldBe("Equals");
    }

    [Fact]
    public async Task GivenHeaderCriteriaWithoutOperator_WhenCompiled_ThenOperatorIsNull()
    {
        LocustIrAssertionCriteria criteria = await CompileSingleAssertCriteria(new HeaderCriteria("ETag"));

        criteria.Kind.ShouldBe(LocustIrAssertionKind.Header);
        criteria.Field.ShouldBe("ETag");
        criteria.Operator.ShouldBeNull();
    }

    [Fact]
    public async Task GivenFhirPathCriteria_WhenCompiled_ThenMapsToFhirPathKind()
    {
        LocustIrAssertionCriteria criteria = await CompileSingleAssertCriteria(new FhirPathCriteria("Patient.id.exists()"));

        criteria.Kind.ShouldBe(LocustIrAssertionKind.FhirPath);
        criteria.Expression.ShouldBe("Patient.id.exists()");
    }

    [Fact]
    public async Task GivenFhirPathValueCriteria_WhenCompiled_ThenMapsExpressionValueAndOperator()
    {
        LocustIrAssertionCriteria criteria = await CompileSingleAssertCriteria(
            new FhirPathValueCriteria("Patient.id", "abc", AssertOperator.Equals));

        criteria.Kind.ShouldBe(LocustIrAssertionKind.FhirPathValue);
        criteria.Expression.ShouldBe("Patient.id");
        criteria.Value.ShouldBe("abc");
        criteria.Operator.ShouldBe("Equals");
    }

    [Fact]
    public async Task GivenRequestMethodCriteria_WhenCompiled_ThenMapsToRequestMethodKind()
    {
        LocustIrAssertionCriteria criteria = await CompileSingleAssertCriteria(new RequestMethodCriteria("GET"));

        criteria.Kind.ShouldBe(LocustIrAssertionKind.RequestMethod);
        criteria.Value.ShouldBe("GET");
    }

    [Fact]
    public async Task GivenRequestUrlCriteriaWithOperator_WhenCompiled_ThenMapsValueAndOperator()
    {
        LocustIrAssertionCriteria criteria = await CompileSingleAssertCriteria(
            new RequestUrlCriteria("Patient", AssertOperator.Contains));

        criteria.Kind.ShouldBe(LocustIrAssertionKind.RequestUrl);
        criteria.Value.ShouldBe("Patient");
        criteria.Operator.ShouldBe("Contains");
    }

    [Fact]
    public async Task GivenRequestUrlCriteriaWithoutOperator_WhenCompiled_ThenOperatorIsNull()
    {
        LocustIrAssertionCriteria criteria = await CompileSingleAssertCriteria(new RequestUrlCriteria("Patient"));

        criteria.Kind.ShouldBe(LocustIrAssertionKind.RequestUrl);
        criteria.Operator.ShouldBeNull();
    }

    [Fact]
    public async Task GivenExpressionExtractionVariable_WhenCompiled_ThenMapsToFhirPathKind()
    {
        LocustIrVariable variable = await CompileSingleVariable(new ExpressionExtraction("Patient.id"));

        variable.ExtractionKind.ShouldBe(LocustIrVariableExtractionKind.FhirPath);
        variable.Selector.ShouldBe("Patient.id");
    }

    [Fact]
    public async Task GivenPathExtractionVariable_WhenCompiled_ThenMapsToPathKind()
    {
        LocustIrVariable variable = await CompileSingleVariable(new PathExtraction("$.id"));

        variable.ExtractionKind.ShouldBe(LocustIrVariableExtractionKind.Path);
        variable.Selector.ShouldBe("$.id");
    }

    [Fact]
    public async Task GivenHeaderExtractionVariable_WhenCompiled_ThenMapsToHeaderKind()
    {
        LocustIrVariable variable = await CompileSingleVariable(new HeaderExtraction("ETag"));

        variable.ExtractionKind.ShouldBe(LocustIrVariableExtractionKind.Header);
        variable.Selector.ShouldBe("ETag");
    }

    [Fact]
    public async Task GivenNullExtractionVariable_WhenCompiled_ThenMapsToNoneKindWithNullSelector()
    {
        LocustIrVariable variable = await CompileSingleVariable(null);

        variable.ExtractionKind.ShouldBe(LocustIrVariableExtractionKind.None);
        variable.Selector.ShouldBeNull();
    }

    [Fact]
    public async Task GivenOperationWithHeadersIdsContentAndWaitFor_WhenCompiled_ThenAllFieldsSurviveLowering()
    {
        TestScriptDefinition definition = SingleSetupOperation(new OperationExpression
        {
            Type = "update",
            Resource = "Patient",
            Url = "http://example.test/Patient/1",
            Params = "?_id=1",
            Accept = "json",
            ContentType = "json",
            SourceId = "fixture-1",
            ResponseId = "response-1",
            RequestId = "request-1",
            EncodeRequestUrl = false,
            Headers = [new HeaderExpression { Field = "If-Match", Value = "W/\"1\"" }],
            WaitFor = new WaitForCondition(202, 5, 250)
        });

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options(), CancellationToken.None);

        result.HasErrors.ShouldBeFalse();
        LocustIrOperation operation = result.Document.ShouldNotBeNull().Setup[0].ShouldBeOfType<LocustIrOperation>();

        operation.Url.ShouldBe("http://example.test/Patient/1");
        operation.Params.ShouldBe("?_id=1");
        operation.Accept.ShouldBe("json");
        operation.ContentType.ShouldBe("json");
        operation.SourceId.ShouldBe("fixture-1");
        operation.ResponseId.ShouldBe("response-1");
        operation.RequestId.ShouldBe("request-1");
        operation.EncodeRequestUrl.ShouldBeFalse();

        LocustIrHeader header = operation.Headers.ShouldHaveSingleItem();
        header.Field.ShouldBe("If-Match");
        header.Value.ShouldBe("W/\"1\"");

        operation.WaitFor.ShouldNotBeNull();
        operation.WaitFor.PollingStatusCode.ShouldBe(202);
        operation.WaitFor.MaxAttempts.ShouldBe(5);
        operation.WaitFor.IntervalMs.ShouldBe(250);
    }

    [Fact]
    public async Task GivenDefinitionWithSupportWarnings_WhenCompiled_ThenWarningsSurviveOnSuccessfulCompilation()
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Profiles = [new ProfileReference { Id = "profile", Canonical = "http://example.test/StructureDefinition/patient" }],
            Setup = [new OperationExpression { Type = "read", Resource = "Patient", EncodeRequestUrl = false }]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options(), CancellationToken.None);

        result.HasErrors.ShouldBeFalse();
        result.Document.ShouldNotBeNull();
        result.Diagnostics.Select(d => d.Code).ShouldContain("LOCUST004");
        result.Diagnostics.Select(d => d.Code).ShouldContain("LOCUST005");
    }

    [Fact]
    public async Task GivenAnalyzerErrors_WhenCompiled_ThenDocumentIsNullAndNoMetricDiagnosticsAreEmitted()
    {
        TestScriptDefinition definition = SingleSetupOperation(new OperationExpression
        {
            Type = "read",
            Resource = "Patient",
            Destination = 2
        });

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options(), CancellationToken.None);

        result.HasErrors.ShouldBeTrue();
        result.Document.ShouldBeNull();
        result.Diagnostics.ShouldNotContain(d => d.Code == "LOCUST_METRIC");
    }

    [Fact]
    public async Task GivenAssertionWithWhenResponseStatusAndRequestDirection_WhenCompiled_ThenMapsExactly()
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Setup =
            [
                new AssertExpression
                {
                    Criteria = new RequestMethodCriteria("GET"),
                    Direction = AssertDirection.Request,
                    WarningOnly = true,
                    WhenResponseStatus = new ResponseStatusCondition("earlier-response", [200, 201])
                }
            ]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options(), CancellationToken.None);

        result.HasErrors.ShouldBeFalse();
        LocustIrAssertion assertion = result.Document.ShouldNotBeNull().Setup[0].ShouldBeOfType<LocustIrAssertion>();

        assertion.Direction.ShouldBe("request");
        assertion.WarningOnly.ShouldBeTrue();
        assertion.WhenResponseSourceId.ShouldBe("earlier-response");
        assertion.WhenResponseStatuses.ShouldBe([200, 201]);
    }

    [Fact]
    public async Task GivenNullDefinition_WhenCompiled_ThenThrowsArgumentNullException()
    {
        await Should.ThrowAsync<ArgumentNullException>(
            () => s_compiler.CompileAsync(null!, Options(), CancellationToken.None));
    }

    [Fact]
    public async Task GivenNullOptions_WhenCompiled_ThenThrowsArgumentNullException()
    {
        TestScriptDefinition definition = SingleSetupOperation(new OperationExpression { Type = "read", Resource = "Patient" });

        await Should.ThrowAsync<ArgumentNullException>(
            () => s_compiler.CompileAsync(definition, null!, CancellationToken.None));
    }

    [Fact]
    public async Task GivenNullSchema_WhenCompiled_ThenThrowsArgumentNullException()
    {
        TestScriptDefinition definition = SingleSetupOperation(new OperationExpression { Type = "read", Resource = "Patient" });
        LocustCompilerOptions options = new("read.json", "4.0", null!, 0);

        await Should.ThrowAsync<ArgumentNullException>(
            () => s_compiler.CompileAsync(definition, options, CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GivenBlankSource_WhenCompiled_ThenThrowsArgumentException(string source)
    {
        TestScriptDefinition definition = SingleSetupOperation(new OperationExpression { Type = "read", Resource = "Patient" });
        LocustCompilerOptions options = new(source, "4.0", FhirVersion.R4.GetSchemaProvider(), 0);

        await Should.ThrowAsync<ArgumentException>(
            () => s_compiler.CompileAsync(definition, options, CancellationToken.None));
    }

    [Fact]
    public async Task GivenPreCancelledToken_WhenCompiled_ThenThrowsOperationCanceledException()
    {
        TestScriptDefinition definition = SingleSetupOperation(new OperationExpression { Type = "read", Resource = "Patient" });
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => s_compiler.CompileAsync(definition, Options(), cts.Token));
    }

    [Fact]
    public async Task GivenTestWithNonMatchingFhirVersion_WhenCompiled_ThenTestIsExcludedFromDocument()
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Tests =
            [
                new TestPhaseDefinition { Name = "r5-only", FhirVersions = ["5.0"] },
                new TestPhaseDefinition { Name = "r4-only", FhirVersions = ["4.0"] }
            ]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options(fhirVersion: "4.0"), CancellationToken.None);

        result.HasErrors.ShouldBeFalse();
        LocustIrDocument document = result.Document.ShouldNotBeNull();
        LocustIrTest test = document.Tests.ShouldHaveSingleItem();
        test.Id.ShouldBe("test.1");
        test.Name.ShouldBe("r4-only");
    }

    [Theory]
    [InlineData("4.0", "4.0", true)]
    [InlineData("4.0", "4.0.1", true)]
    [InlineData("4.0.1", "4.0.1", true)]
    [InlineData("4.0.1", "4.0.2", false)]
    [InlineData("4.*", "4.3", true)]
    [InlineData("5.0", "4.3", false)]
    [InlineData("4.3", "4.0", false)]
    public async Task GivenVersionSpecs_WhenCompiled_ThenSharedCompatibilityHelperGatesTestInclusion(
        string spec, string actualVersion, bool expectIncluded)
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Tests = [new TestPhaseDefinition { Name = "gated", FhirVersions = [spec] }]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options(fhirVersion: actualVersion), CancellationToken.None);

        result.HasErrors.ShouldBeFalse();
        LocustIrDocument document = result.Document.ShouldNotBeNull();
        document.Tests.Count.ShouldBe(expectIncluded ? 1 : 0);
    }

    [Fact]
    public async Task GivenTestWithNullActualVersionOrEmptyDeclaredVersions_WhenCompiled_ThenTestRemainsCompatible()
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Tests =
            [
                new TestPhaseDefinition { Name = "declared", FhirVersions = ["4.0"] },
                new TestPhaseDefinition { Name = "undeclared" }
            ]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options(fhirVersion: null), CancellationToken.None);

        result.HasErrors.ShouldBeFalse();
        LocustIrDocument document = result.Document.ShouldNotBeNull();
        document.Tests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GivenParametrizedTest_WhenCompiled_ThenEmitsOneTestPerValueWithUniqueIdsAndBoundVariables()
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "case",
                    Description = "desc",
                    Parameters = new ParametrizeDefinition("code", ["a", "b", "a"]),
                    Actions = [new OperationExpression { Type = "read", Resource = "Patient" }]
                }
            ]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options(), CancellationToken.None);

        result.HasErrors.ShouldBeFalse();
        LocustIrDocument document = result.Document.ShouldNotBeNull();
        document.Tests.Count.ShouldBe(3);

        document.Tests[0].Id.ShouldBe("test.0.param.0");
        document.Tests[0].Name.ShouldBe("case [a]");
        document.Tests[0].Description.ShouldBe("desc");
        document.Tests[0].DiscardContextAfterExecution.ShouldBeTrue();
        document.Tests[0].InitialVariables.ShouldContainKeyAndValue("code", "a");
        document.Tests[0].Actions[0].Id.ShouldBe("test.0.param.0.action.0");

        document.Tests[1].Id.ShouldBe("test.0.param.1");
        document.Tests[1].Name.ShouldBe("case [b]");
        document.Tests[1].InitialVariables.ShouldContainKeyAndValue("code", "b");
        document.Tests[1].Actions[0].Id.ShouldBe("test.0.param.1.action.0");

        document.Tests[2].Id.ShouldBe("test.0.param.2");
        document.Tests[2].Name.ShouldBe("case [a]");
        document.Tests[2].InitialVariables.ShouldContainKeyAndValue("code", "a");
        document.Tests[2].Actions[0].Id.ShouldBe("test.0.param.2.action.0");
    }

    [Fact]
    public async Task GivenNonParametrizedTest_WhenCompiled_ThenRetainsOriginalIndexAndDoesNotDiscardContext()
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "case",
                    Actions = [new OperationExpression { Type = "read", Resource = "Patient" }]
                }
            ]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options(), CancellationToken.None);

        result.HasErrors.ShouldBeFalse();
        LocustIrTest test = result.Document.ShouldNotBeNull().Tests.ShouldHaveSingleItem();
        test.Id.ShouldBe("test.0");
        test.Name.ShouldBe("case");
        test.DiscardContextAfterExecution.ShouldBeFalse();
        test.InitialVariables.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenFilteredTest_WhenLaterTestsCompiled_ThenOriginalIndexGapIsPreservedNotRenumbered()
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Tests =
            [
                new TestPhaseDefinition { Name = "excluded", FhirVersions = ["5.0"] },
                new TestPhaseDefinition { Name = "included-one" },
                new TestPhaseDefinition { Name = "included-two" }
            ]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options(fhirVersion: "4.0"), CancellationToken.None);

        result.HasErrors.ShouldBeFalse();
        LocustIrDocument document = result.Document.ShouldNotBeNull();
        document.Tests.Count.ShouldBe(2);
        document.Tests[0].Id.ShouldBe("test.1");
        document.Tests[1].Id.ShouldBe("test.2");
    }

    [Fact]
    public async Task GivenSuiteAndTestRequiresCapability_WhenCompiled_ThenExpressionsAreCopiedVerbatimNotEvaluated()
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata
            {
                Name = "Suite",
                RequiresCapability = "rest.resource.where(type='Patient').exists()"
            },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "case",
                    RequiresCapability = "rest.resource.where(type='Patient').operation.where(name='everything').exists()"
                }
            ]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options(), CancellationToken.None);

        result.HasErrors.ShouldBeFalse();
        LocustIrDocument document = result.Document.ShouldNotBeNull();
        document.RequiresCapability.ShouldBe("rest.resource.where(type='Patient').exists()");
        document.Tests.ShouldHaveSingleItem().RequiresCapability
            .ShouldBe("rest.resource.where(type='Patient').operation.where(name='everything').exists()");
    }

    [Fact]
    public async Task GivenLiteralFixture_WhenCompiled_ThenEmitsExactlyOneClonedVariantPreservingFlagsEvenWithZeroVariants()
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Fixtures = [LiteralPatientFixture(id: "literal-fixture", autocreate: true, autodelete: true)]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options(fixtureVariants: 0), CancellationToken.None);

        result.HasErrors.ShouldBeFalse();
        LocustIrFixture fixture = result.Document.ShouldNotBeNull().Fixtures.ShouldHaveSingleItem();
        fixture.Id.ShouldBe("literal-fixture");
        fixture.Autocreate.ShouldBeTrue();
        fixture.Autodelete.ShouldBeTrue();
        fixture.Variants.ShouldHaveSingleItem()["resourceType"]!.GetValue<string>().ShouldBe("Patient");
    }

    [Fact]
    public async Task GivenFhirFakesFixture_WhenFixtureVariantsIsZero_ThenReturnsLocust007ErrorAndNullDocument()
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Fixtures = [FhirFakesPatientFixture()]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options(fixtureVariants: 0), CancellationToken.None);

        result.HasErrors.ShouldBeTrue();
        result.Document.ShouldBeNull();
        result.Diagnostics.ShouldContain(d => d.Code == "LOCUST007" && d.Severity == LocustDiagnosticSeverity.Error);
        result.Diagnostics.ShouldNotContain(d => d.Code == "LOCUST_METRIC");
    }

    [Fact]
    public async Task GivenFhirFakesFixture_WhenFixtureVariantsIsThree_ThenEmitsThreeSchemaValidPatientVariants()
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Fixtures = [FhirFakesPatientFixture()]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options(fixtureVariants: 3), CancellationToken.None);

        result.HasErrors.ShouldBeFalse();
        LocustIrFixture fixture = result.Document.ShouldNotBeNull().Fixtures.ShouldHaveSingleItem();
        fixture.Variants.Count.ShouldBe(3);
        fixture.Variants.ShouldAllBe(resource => resource["resourceType"]!.GetValue<string>() == "Patient");
    }

    [Fact]
    public async Task GivenUnmaterializableFixture_WhenCompiled_ThenReturnsLocust008ErrorAndNullDocumentWithNoMetrics()
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Fixtures = [new FixtureDefinition { Id = "empty-fixture", Resource = null }]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options(fixtureVariants: 1), CancellationToken.None);

        result.HasErrors.ShouldBeTrue();
        result.Document.ShouldBeNull();
        result.Diagnostics.ShouldContain(d => d.Code == "LOCUST008" && d.Severity == LocustDiagnosticSeverity.Error);
        result.Diagnostics.ShouldNotContain(d => d.Code == "LOCUST_METRIC");
    }

    [Fact]
    public async Task GivenSupportWarningAndUnmaterializableFixture_WhenCompiled_ThenBothDiagnosticSetsSurviveWithNullDocumentAndNoMetrics()
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Fixtures = [new FixtureDefinition { Id = "empty-fixture", Resource = null }],
            Setup = [new OperationExpression { Type = "read", Resource = "Patient", EncodeRequestUrl = false }]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options(fixtureVariants: 1), CancellationToken.None);

        result.HasErrors.ShouldBeTrue();
        result.Document.ShouldBeNull();
        result.Diagnostics.ShouldContain(d => d.Code == "LOCUST004" && d.Severity == LocustDiagnosticSeverity.Warning);
        result.Diagnostics.ShouldContain(d => d.Code == "LOCUST008" && d.Severity == LocustDiagnosticSeverity.Error);
        result.Diagnostics.ShouldNotContain(d => d.Code == "LOCUST_METRIC");
    }

    [Fact]
    public async Task GivenAnalyzerErrors_WhenDefinitionAlsoHasFixtures_ThenFixturesAreNeverCompiled()
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Fixtures = [new FixtureDefinition { Id = "empty-fixture", Resource = null }],
            Setup = [new OperationExpression { Type = "read", Resource = "Patient", Destination = 2 }]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options(fixtureVariants: 1), CancellationToken.None);

        result.HasErrors.ShouldBeTrue();
        result.Document.ShouldBeNull();
        result.Diagnostics.ShouldNotContain(d => d.Code == "LOCUST008");
        result.Diagnostics.ShouldContain(d => d.Code == "LOCUST001");
    }

    [Fact]
    public async Task GivenFixtureWithAutocreateAndAutodelete_WhenCompiled_ThenEmitsBothLifecycleMetricMappings()
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Fixtures = [LiteralPatientFixture(id: "patient-fixture", autocreate: true, autodelete: true)]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options("lifecycle.json"), CancellationToken.None);

        result.HasErrors.ShouldBeFalse();
        List<LocustDiagnostic> metrics = [.. result.Diagnostics.Where(d => d.Code == "LOCUST_METRIC")];

        metrics.ShouldContain(d =>
            d.Message == "Metric 'lifecycle.json::fixture.patient-fixture.autocreate'"
            && d.Severity == LocustDiagnosticSeverity.Info);
        metrics.ShouldContain(d =>
            d.Message == "Metric 'lifecycle.json::fixture.patient-fixture.autodelete'"
            && d.Severity == LocustDiagnosticSeverity.Info);
    }

    [Fact]
    public async Task GivenFixtureWithOnlyAutocreate_WhenCompiled_ThenEmitsOnlyAutocreateLifecycleMapping()
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Fixtures = [LiteralPatientFixture(id: "patient-fixture", autocreate: true, autodelete: false)]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options("lifecycle.json"), CancellationToken.None);

        result.HasErrors.ShouldBeFalse();
        List<LocustDiagnostic> metrics = [.. result.Diagnostics.Where(d => d.Code == "LOCUST_METRIC")];

        metrics.ShouldContain(d => d.Message == "Metric 'lifecycle.json::fixture.patient-fixture.autocreate'");
        metrics.ShouldNotContain(d => d.Message == "Metric 'lifecycle.json::fixture.patient-fixture.autodelete'");
    }

    [Fact]
    public async Task GivenFixtureWithOnlyAutodelete_WhenCompiled_ThenEmitsOnlyAutodeleteLifecycleMapping()
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Fixtures = [LiteralPatientFixture(id: "patient-fixture", autocreate: false, autodelete: true)]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options("lifecycle.json"), CancellationToken.None);

        result.HasErrors.ShouldBeFalse();
        List<LocustDiagnostic> metrics = [.. result.Diagnostics.Where(d => d.Code == "LOCUST_METRIC")];

        metrics.ShouldNotContain(d => d.Message == "Metric 'lifecycle.json::fixture.patient-fixture.autocreate'");
        metrics.ShouldContain(d => d.Message == "Metric 'lifecycle.json::fixture.patient-fixture.autodelete'");
    }

    [Fact]
    public async Task GivenFixtureWithNeitherAutocreateNorAutodelete_WhenCompiled_ThenEmitsNoLifecycleMappings()
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Fixtures = [LiteralPatientFixture(id: "patient-fixture")]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options("lifecycle.json"), CancellationToken.None);

        result.HasErrors.ShouldBeFalse();
        result.Diagnostics.Where(d => d.Code == "LOCUST_METRIC")
            .ShouldNotContain(d => d.Message.Contains("fixture.patient-fixture", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GivenFilteredTest_WhenCompiled_ThenNoMetricMappingIsEmittedForIt()
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "excluded",
                    FhirVersions = ["5.0"],
                    Actions = [new OperationExpression { Type = "read", Resource = "Patient" }]
                }
            ]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options("filtered.json", fhirVersion: "4.0"), CancellationToken.None);

        result.HasErrors.ShouldBeFalse();
        result.Diagnostics.ShouldNotContain(d => d.Code == "LOCUST_METRIC");
    }

    [Fact]
    public async Task GivenParametrizedTest_WhenCompiled_ThenMetricMappingsUseFullExpandedActionIds()
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "case",
                    Parameters = new ParametrizeDefinition("code", ["a", "b"]),
                    Actions = [new OperationExpression { Type = "read", Resource = "Patient" }]
                }
            ]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options("params.json"), CancellationToken.None);

        result.HasErrors.ShouldBeFalse();
        List<LocustDiagnostic> metrics = [.. result.Diagnostics.Where(d => d.Code == "LOCUST_METRIC")];

        LocustDiagnostic first = metrics.Single(d => d.Message == "Metric 'params.json::test.0.param.0.action.0'");
        LocustDiagnostic second = metrics.Single(d => d.Message == "Metric 'params.json::test.0.param.1.action.0'");

        // Distinct expansions of the same TestPhaseDefinition share a Name, so the diagnostic Source
        // must be disambiguated per value index rather than colliding on an identical location.
        first.Source.ShouldNotBe(second.Source);
    }

    private static TestScriptDefinition SingleSetupOperation(OperationExpression operation) => new()
    {
        Metadata = new TestScriptMetadata { Name = "Suite" },
        Setup = [operation]
    };

    // ----------------------------------------------------------------------
    // Task 9 (RED): FHIRPath compatibility gating diagnostics.
    //
    // These tests express the planned INTERNAL Task 9 compiler surface exactly:
    //   internal enum FhirPathUsage { Boolean, Scalar }
    //   internal sealed record FhirPathIncompatibility(string Expression, FhirPathUsage Usage, string Reason)
    //   internal sealed class FhirPathCompatibilityManifest
    //       internal ctor(IEnumerable<FhirPathIncompatibility> entries)
    //       static FhirPathCompatibilityManifest LoadEmbedded()
    //       string? FindReason(string expression, FhirPathUsage usage)
    //   LocustIrCompiler(): parameterless ctor (loads embedded manifest)
    //   internal LocustIrCompiler(FhirPathCompatibilityManifest manifest)
    //
    // None of these production types exist yet, so this file will not compile
    // until Task 9 is implemented. That compile failure IS the intended RED.
    // ----------------------------------------------------------------------

    // Clearly malformed FHIRPath (unbalanced parenthesis) used to exercise the
    // planned LOCUST010 "expression fails to parse" diagnostic in every location.
    private const string MalformedFhirPathExpression = "Patient.where(";

    [Fact]
    public void GivenManifest_WhenQueried_ThenFindReasonAndLoadEmbeddedExposeExpectedApi()
    {
        FhirPathCompatibilityManifest manifest = new(
        [
            new FhirPathIncompatibility("Patient.name", FhirPathUsage.Scalar, "multi-value coercion differs")
        ]);

        // FindReason matches on (expression, usage); returns the recorded reason or null.
        manifest.FindReason("Patient.name", FhirPathUsage.Scalar).ShouldBe("multi-value coercion differs");
        manifest.FindReason("Patient.name", FhirPathUsage.Boolean).ShouldBeNull();
        manifest.FindReason("Patient.id", FhirPathUsage.Scalar).ShouldBeNull();

        // The embedded manifest is the source the parameterless compiler ctor uses.
        FhirPathCompatibilityManifest embedded = FhirPathCompatibilityManifest.LoadEmbedded();
        embedded.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenIncompatibleScalarExpressionInVariableExtraction_WhenCompiledWithManifest_ThenReturnsSourceQualifiedLocust009ErrorAndNullDocument()
    {
        FhirPathCompatibilityManifest manifest = new(
        [
            new FhirPathIncompatibility("Patient.name", FhirPathUsage.Scalar, "multi-value coercion differs")
        ]);
        LocustIrCompiler compiler = new(manifest);

        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Variables =
            [
                new VariableDefinition
                {
                    Name = "value",
                    Extraction = new ExpressionExtraction("Patient.name")
                }
            ]
        };

        LocustCompilationResult result = await compiler.CompileAsync(
            definition, Options(source: "incompat.json"), CancellationToken.None);

        result.HasErrors.ShouldBeTrue();
        result.Document.ShouldBeNull();

        LocustDiagnostic diagnostic = result.Diagnostics.Where(d => d.Code == "LOCUST009").ShouldHaveSingleItem();
        diagnostic.Severity.ShouldBe(LocustDiagnosticSeverity.Error);
        diagnostic.Source.ShouldNotBeNull();
        diagnostic.Source.ShouldStartWith("incompat.json");
        diagnostic.Source.ShouldContain(":");
        diagnostic.Message.ShouldContain("multi-value coercion differs");
        result.Diagnostics.ShouldNotContain(d => d.Code == "LOCUST_METRIC");
    }

    [Fact]
    public async Task GivenMalformedSuiteRequiresCapability_WhenCompiled_ThenReturnsSingleSourceQualifiedLocust010ErrorAndNullDocument()
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata
            {
                Name = "Suite",
                RequiresCapability = MalformedFhirPathExpression
            }
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(
            definition, Options(source: "malformed-suite.json"), CancellationToken.None);

        AssertSingleSourceQualifiedError(result, "LOCUST010", "malformed-suite.json");
    }

    [Fact]
    public async Task GivenMalformedTestRequiresCapability_WhenCompiled_ThenReturnsSingleSourceQualifiedLocust010ErrorAndNullDocument()
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Tests =
            [
                new TestPhaseDefinition
                {
                    Name = "case",
                    RequiresCapability = MalformedFhirPathExpression
                }
            ]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(
            definition, Options(source: "malformed-test.json"), CancellationToken.None);

        AssertSingleSourceQualifiedError(result, "LOCUST010", "malformed-test.json");
    }

    [Fact]
    public async Task GivenMalformedAssertionCriteria_WhenCompiled_ThenReturnsSingleSourceQualifiedLocust010ErrorAndNullDocument()
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Setup = [new AssertExpression { Criteria = new FhirPathCriteria(MalformedFhirPathExpression) }]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(
            definition, Options(source: "malformed-assert.json"), CancellationToken.None);

        AssertSingleSourceQualifiedError(result, "LOCUST010", "malformed-assert.json");
    }

    [Fact]
    public async Task GivenMalformedVariableExtraction_WhenCompiled_ThenReturnsSingleSourceQualifiedLocust010ErrorAndNullDocument()
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Variables =
            [
                new VariableDefinition
                {
                    Name = "value",
                    Extraction = new ExpressionExtraction(MalformedFhirPathExpression)
                }
            ]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(
            definition, Options(source: "malformed-variable.json"), CancellationToken.None);

        AssertSingleSourceQualifiedError(result, "LOCUST010", "malformed-variable.json");
    }

    [Fact]
    public async Task GivenOversizedNumericLiteralInScannedExpression_WhenCompiled_ThenReturnsSingleSourceQualifiedLocust010ErrorAndNullDocument()
    {
        // An out-of-range integer literal makes the Ignixa parser throw OverflowException (not
        // ArgumentException/FormatException). The compiler must surface this as a deterministic
        // LOCUST010 rather than letting the exception escape the compile.
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Setup = [new AssertExpression { Criteria = new FhirPathCriteria("Patient.id = 9999999999") }]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(
            definition, Options(source: "overflow-assert.json"), CancellationToken.None);

        AssertSingleSourceQualifiedError(result, "LOCUST010", "overflow-assert.json");
    }

    private static void AssertSingleSourceQualifiedError(LocustCompilationResult result, string code, string source)
    {
        result.HasErrors.ShouldBeTrue();
        result.Document.ShouldBeNull();

        LocustDiagnostic diagnostic = result.Diagnostics.Where(d => d.Code == code).ShouldHaveSingleItem();
        diagnostic.Severity.ShouldBe(LocustDiagnosticSeverity.Error);
        diagnostic.Source.ShouldNotBeNull();
        diagnostic.Source.ShouldStartWith(source);
        diagnostic.Source.ShouldContain(":");
        result.Diagnostics.ShouldNotContain(d => d.Code == "LOCUST_METRIC");
    }

    private static async Task<LocustIrAssertionCriteria> CompileSingleAssertCriteria(AssertCriteria criteria)
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Setup = [new AssertExpression { Criteria = criteria }]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options(), CancellationToken.None);

        result.HasErrors.ShouldBeFalse();
        LocustIrAssertion assertion = result.Document.ShouldNotBeNull().Setup[0].ShouldBeOfType<LocustIrAssertion>();
        return assertion.Criteria;
    }

    private static async Task<LocustIrVariable> CompileSingleVariable(VariableExtraction? extraction)
    {
        TestScriptDefinition definition = new()
        {
            Metadata = new TestScriptMetadata { Name = "Suite" },
            Variables = [new VariableDefinition { Name = "value", Extraction = extraction }]
        };

        LocustCompilationResult result = await s_compiler.CompileAsync(definition, Options(), CancellationToken.None);

        result.HasErrors.ShouldBeFalse();
        return result.Document.ShouldNotBeNull().Variables.ShouldHaveSingleItem();
    }
}
