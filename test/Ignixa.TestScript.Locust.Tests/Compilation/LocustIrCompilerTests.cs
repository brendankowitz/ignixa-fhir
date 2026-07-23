using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Specification.Extensions;
using Ignixa.TestScript.Expressions;
using Ignixa.TestScript.Locust.Compilation;
using Ignixa.TestScript.Locust.Diagnostics;
using Ignixa.TestScript.Locust.Ir;
using Ignixa.TestScript.Model;

namespace Ignixa.TestScript.Locust.Tests.Compilation;

public class LocustIrCompilerTests
{
    private static readonly LocustIrCompiler s_compiler = new();

    private static LocustCompilerOptions Options(string source = "read.json") => new(
        source,
        "4.0",
        FhirVersion.R4.GetSchemaProvider(),
        0);

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

    private static TestScriptDefinition SingleSetupOperation(OperationExpression operation) => new()
    {
        Metadata = new TestScriptMetadata { Name = "Suite" },
        Setup = [operation]
    };

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
