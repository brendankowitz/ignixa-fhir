using Ignixa.TestScript.Expressions;
using Ignixa.TestScript.Model;
using Ignixa.TestScript.Parsing;

namespace Ignixa.TestScript.Tests.Parsing;

public class TestScriptParserTests
{
    private static string GetTestDataPath(string filename) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", filename);

    [Fact]
    public void GivenSimpleReadTestScript_WhenParsing_ThenReturnsValidDefinition()
    {
        var json = File.ReadAllText(GetTestDataPath("simple-read.json"));

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBeNull();
        result.Value.Metadata.Name.ShouldBe("SimpleReadTest");
        result.Value.Metadata.Status.ShouldBe("active");
    }

    [Fact]
    public void GivenSimpleReadTestScript_WhenParsing_ThenParsesVariables()
    {
        var json = File.ReadAllText(GetTestDataPath("simple-read.json"));

        var result = TestScriptParser.Parse(json);

        result.Value!.Variables.Count.ShouldBe(1);
        result.Value.Variables[0].Name.ShouldBe("patientId");
        result.Value.Variables[0].DefaultValue.ShouldBe("example");
    }

    [Fact]
    public void GivenSimpleReadTestScript_WhenParsing_ThenParsesTestActions()
    {
        var json = File.ReadAllText(GetTestDataPath("simple-read.json"));

        var result = TestScriptParser.Parse(json);

        result.Value!.Tests.Count.ShouldBe(1);
        result.Value.Tests[0].Name.ShouldBe("ReadPatient");
        result.Value.Tests[0].Actions.Count.ShouldBe(3);

        var operation = result.Value.Tests[0].Actions[0].ShouldBeOfType<OperationExpression>();
        operation.Type.ShouldBe("read");
        operation.Resource.ShouldBe("Patient");
        operation.ResponseId.ShouldBe("read-response");

        var assert1 = result.Value.Tests[0].Actions[1].ShouldBeOfType<AssertExpression>();
        assert1.Criteria.ShouldBeOfType<ResponseStatusCriteria>().Status.ShouldBe("okay");

        var assert2 = result.Value.Tests[0].Actions[2].ShouldBeOfType<AssertExpression>();
        assert2.Criteria.ShouldBeOfType<ResourceTypeCriteria>().ResourceType.ShouldBe("Patient");
    }

    [Fact]
    public void GivenInvalidJson_WhenParsing_ThenReturnsError()
    {
        var json = "not valid json";

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void GivenMissingName_WhenParsing_ThenReturnsError()
    {
        var json = """{"resourceType": "TestScript", "status": "active"}""";

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Message.Contains("name"));
    }

    [Fact]
    public void GivenParseFile_WhenFileNotFound_ThenReturnsFailure()
    {
        var nonExistentPath = Path.Combine(AppContext.BaseDirectory, "does-not-exist.json");

        var result = TestScriptParser.ParseFile(nonExistentPath);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldNotBeEmpty();
        result.Errors[0].Severity.ShouldBe(ParseSeverity.Error);
    }

    [Fact]
    public void GivenScriptWithMissingStatus_WhenParsing_ThenIsSuccessButHasWarnings()
    {
        var json = """{"resourceType":"TestScript","name":"NoStatus"}""";

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        result.HasWarnings.ShouldBeTrue();
        result.Errors.ShouldContain(e => e.Severity == ParseSeverity.Warning && e.Message.Contains("status"));
    }

    [Fact]
    public void GivenAssertWithExpression_WhenParsing_ThenCreatesFhirPathCriteria()
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"Expr",
              "status":"active",
              "test":[{"name":"t","action":[{"assert":{"expression":"Patient.id.exists()"}}]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        var assertion = result.Value!.Tests[0].Actions[0].ShouldBeOfType<AssertExpression>();
        assertion.Criteria.ShouldBeOfType<FhirPathCriteria>().Expression.ShouldBe("Patient.id.exists()");
    }

    [Fact]
    public void GivenAssertWithExpressionAndValue_WhenParsing_ThenCreatesFhirPathValueCriteria()
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"ExprVal",
              "status":"active",
              "test":[{"name":"t","action":[{"assert":{"expression":"Patient.id","value":"abc","operator":"equals"}}]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        var assertion = result.Value!.Tests[0].Actions[0].ShouldBeOfType<AssertExpression>();
        var criteria = assertion.Criteria.ShouldBeOfType<FhirPathValueCriteria>();
        criteria.Expression.ShouldBe("Patient.id");
        criteria.Value.ShouldBe("abc");
        criteria.Operator.ShouldBe(AssertOperator.Equals);
    }

    [Fact]
    public void GivenAssertWithExpressionOnly_WhenParsing_ThenCreatesFhirPathCriteria()
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"ExprOnly",
              "status":"active",
              "test":[{"name":"t","action":[{"assert":{"expression":"Patient.id.exists()"}}]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        var assertion = result.Value!.Tests[0].Actions[0].ShouldBeOfType<AssertExpression>();
        assertion.Criteria.ShouldBeOfType<FhirPathCriteria>().Expression.ShouldBe("Patient.id.exists()");
    }

    [Fact]
    public void GivenOperationWithCustomType_WhenParsing_ThenPreservesTypeCode()
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"CustomOp",
              "status":"active",
              "test":[{"name":"t","action":[{"operation":{"type":{"code":"validate"},"url":"Patient/$validate"}}]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        var operation = result.Value!.Tests[0].Actions[0].ShouldBeOfType<OperationExpression>();
        operation.Type.ShouldBe("validate");
        operation.Url.ShouldBe("Patient/$validate");
    }

    [Fact]
    public void GivenAssertWithHeaderField_WhenParsing_ThenCreatesHeaderCriteria()
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"Hdr",
              "status":"active",
              "test":[{"name":"t","action":[{"assert":{"headerField":"Content-Type","value":"application/fhir+json","operator":"equals"}}]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        var assertion = result.Value!.Tests[0].Actions[0].ShouldBeOfType<AssertExpression>();
        var header = assertion.Criteria.ShouldBeOfType<HeaderCriteria>();
        header.Field.ShouldBe("Content-Type");
        header.Value.ShouldBe("application/fhir+json");
        header.Operator.ShouldBe(AssertOperator.Equals);
    }

    [Fact]
    public void GivenVariableWithHeaderField_WhenParsing_ThenCreatesHeaderExtraction()
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"VarHdr",
              "status":"active",
              "variable":[{"name":"loc","headerField":"Location"}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        var variable = result.Value!.Variables[0];
        variable.Name.ShouldBe("loc");
        variable.Extraction.ShouldBeOfType<HeaderExtraction>().Field.ShouldBe("Location");
    }

    [Fact]
    public void GivenTestWithParametrizeExtension_WhenParsing_ThenPopulatesParameters()
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"Parametrized",
              "status":"active",
              "test":[{
                "name":"date prefix ge",
                "extension":[{
                  "url":"http://ignixa.io/testscript/parametrize",
                  "extension":[
                    {"url":"variable","valueString":"searchDate"},
                    {"url":"values","valueString":"2028,2028-06,2028-06-15,2028-06-15T12:00:00Z"}
                  ]
                }],
                "action":[{"operation":{"type":{"code":"search"},"resource":"Observation","params":"?date=ge${searchDate}"}}]
              }]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        var test = result.Value!.Tests[0];
        test.Parameters.ShouldNotBeNull();
        test.Parameters!.VariableName.ShouldBe("searchDate");
        test.Parameters!.Values.ShouldBe(["2028", "2028-06", "2028-06-15", "2028-06-15T12:00:00Z"]);
    }

    [Fact]
    public void GivenTestWithoutParametrizeExtension_WhenParsing_ThenParametersIsEmpty()
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"Plain",
              "status":"active",
              "test":[{"name":"t","action":[{"assert":{"expression":"Patient.id.exists()"}}]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        result.Value!.Tests[0].Parameters.ShouldBeNull();
    }

    [Fact]
    public void GivenTestWithFhirVersionsExtension_WhenParsed_ThenVersionsPopulated()
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"Versioned",
              "status":"active",
              "test":[{
                "name":"of-type r4 only",
                "extension":[{"url":"http://ignixa.io/testscript/fhirVersions","valueString":"4.0,4.3"}],
                "action":[{"operation":{"type":{"code":"search"},"resource":"Patient","params":"?identifier:of-type=x"}}]
              }]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        result.Value!.Tests[0].FhirVersions.ShouldBe(["4.0", "4.3"]);
    }

    [Fact]
    public void GivenTestWithoutFhirVersionsExtension_WhenParsed_ThenVersionsEmpty()
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"Unversioned",
              "status":"active",
              "test":[{"name":"t","action":[{"assert":{"expression":"Patient.id.exists()"}}]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        result.Value!.Tests[0].FhirVersions.ShouldBeEmpty();
    }

    [Fact]
    public void GivenTestWithRequiresCapabilityExtension_WhenParsed_ThenExpressionPopulated()
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"CapabilityGated",
              "status":"active",
              "test":[{
                "name":"everything op",
                "extension":[{"url":"http://ignixa.io/testscript/requiresCapability","valueString":"rest.resource.where(type='Patient').operation.where(name='everything').exists()"}],
                "action":[{"operation":{"type":{"code":"read"},"resource":"Patient","params":"/123/$everything"}}]
              }]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        result.Value!.Tests[0].RequiresCapability
            .ShouldBe("rest.resource.where(type='Patient').operation.where(name='everything').exists()");
    }

    [Fact]
    public void GivenTestWithoutRequiresCapabilityExtension_WhenParsed_ThenExpressionNull()
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"Ungated",
              "status":"active",
              "test":[{"name":"t","action":[{"assert":{"expression":"Patient.id.exists()"}}]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        result.Value!.Tests[0].RequiresCapability.ShouldBeNull();
    }

    [Fact]
    public void GivenTestScriptWithRequiresCapabilityOnMetadata_WhenParsed_ThenExpressionPopulated()
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"SuiteGated",
              "status":"active",
              "extension":[{"url":"http://ignixa.io/testscript/requiresCapability","valueString":"rest.operation.where(name='reindex').exists()"}],
              "test":[{"name":"t","action":[{"assert":{"expression":"Patient.id.exists()"}}]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        result.Value!.Metadata.RequiresCapability.ShouldBe("rest.operation.where(name='reindex').exists()");
    }

    [Fact]
    public void GivenRequiresCapabilityExtensionWithWrongValueType_WhenParsed_ThenExpressionNull()
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"WrongValueType",
              "status":"active",
              "test":[{
                "name":"t",
                "extension":[{"url":"http://ignixa.io/testscript/requiresCapability","valueBoolean":true}],
                "action":[{"assert":{"expression":"Patient.id.exists()"}}]
              }]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        result.Value!.Tests[0].RequiresCapability.ShouldBeNull();
    }

    [Fact]
    public void GivenTestWithParametrizeFhirVersionsAndRequiresCapabilityTogether_WhenParsed_ThenAllThreePopulate()
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"AllExtensionsTogether",
              "status":"active",
              "test":[{
                "name":"combined",
                "extension":[
                  {"url":"http://ignixa.io/testscript/fhirVersions","valueString":"4.0,4.3"},
                  {"url":"http://ignixa.io/testscript/requiresCapability","valueString":"Patient.exists()"},
                  {"url":"http://ignixa.io/testscript/parametrize","extension":[
                    {"url":"variable","valueString":"id"},
                    {"url":"values","valueString":"1,2"}
                  ]}
                ],
                "action":[{"operation":{"type":{"code":"read"},"resource":"Patient","params":"/${id}"}}]
              }]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        var test = result.Value!.Tests[0];
        test.FhirVersions.ShouldBe(["4.0", "4.3"]);
        test.RequiresCapability.ShouldBe("Patient.exists()");
        test.Parameters.ShouldNotBeNull();
        test.Parameters!.VariableName.ShouldBe("id");
        test.Parameters.Values.ShouldBe(["1", "2"]);
    }

    [Fact]
    public void GivenVariableWithExpression_WhenParsing_ThenCreatesExpressionExtraction()
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"VarExpr",
              "status":"active",
              "variable":[{"name":"id","expression":"Patient.id"}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        var variable = result.Value!.Variables[0];
        variable.Extraction.ShouldBeOfType<ExpressionExtraction>().Expression.ShouldBe("Patient.id");
    }

    [Fact]
    public void GivenAssertWithNoSupportedCriteriaField_WhenParsing_ThenReturnsError()
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"BadAssert",
              "status":"active",
              "test":[{"name":"t","action":[{"assert":{"validateProfileId":"my-profile"}}]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldContain(e =>
            e.Severity == ParseSeverity.Error
            && e.Message.Contains("no supported criteria field")
            && e.Message.Contains("validateProfileId"));
    }

    [Fact]
    public void GivenTestWithMultipleParametrizeExtensions_WhenParsing_ThenUsesFirstAndWarns()
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"MultiParam",
              "status":"active",
              "test":[{
                "name":"multi",
                "extension":[
                  {
                    "url":"http://ignixa.io/testscript/parametrize",
                    "extension":[
                      {"url":"variable","valueString":"x"},
                      {"url":"values","valueString":"a,b"}
                    ]
                  },
                  {
                    "url":"http://ignixa.io/testscript/parametrize",
                    "extension":[
                      {"url":"variable","valueString":"y"},
                      {"url":"values","valueString":"1,2"}
                    ]
                  }
                ],
                "action":[{"operation":{"type":{"code":"read"},"resource":"Patient"}}]
              }]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        result.HasWarnings.ShouldBeTrue();
        result.Errors.ShouldContain(e =>
            e.Severity == ParseSeverity.Warning && e.Message.Contains("parametrize"));
        result.Value!.Tests[0].Parameters.ShouldNotBeNull();
        result.Value!.Tests[0].Parameters!.VariableName.ShouldBe("x");
    }

    [Fact]
    public void GivenTestScriptWithWrongFieldType_WhenParsing_ThenSucceedsWithNullDestination()
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"BadType",
              "status":"active",
              "test":[{"name":"t","action":[{
                "operation":{"type":{"code":"read"},"resource":"Patient","destination":"server1"}
              }]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        var op = result.Value!.Tests[0].Actions[0].ShouldBeOfType<OperationExpression>();
        op.Destination.ShouldBeNull();
    }

    [Fact]
    public void GivenOperationWithInvalidHttpMethod_WhenParsing_ThenReturnsWarning()
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"BadMethod",
              "status":"active",
              "test":[{"name":"t","action":[{
                "operation":{"type":{"code":"read"},"resource":"Patient","method":"GEET"}
              }]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        result.HasWarnings.ShouldBeTrue();
        result.Errors.ShouldContain(e =>
            e.Severity == ParseSeverity.Warning && e.Message.Contains("GEET"));
    }

    [Theory]
    [InlineData("\"name\": 123", "name")]
    [InlineData("\"name\": \"ok\", \"status\": true", "status")]
    public void GivenWrongTypedScalarField_WhenParsing_ThenReturnsErrorWithoutThrow(string field, string fieldName)
    {
        var json = """
            {
              "resourceType":"TestScript",
              __FIELD__
            }
            """.Replace("__FIELD__", field, StringComparison.Ordinal);

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldContain(e =>
            e.Severity == ParseSeverity.Error && e.Path != null && e.Path.Contains(fieldName));
    }

    [Fact]
    public void GivenWrongTypedAutocreateField_WhenParsing_ThenReturnsErrorWithoutThrow()
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"BadAutocreate",
              "status":"active",
              "fixture":[{"id":"f1","autocreate":"true"}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldContain(e =>
            e.Severity == ParseSeverity.Error
            && e.Message.Contains("autocreate")
            && e.Message.Contains("boolean"));
    }

    [Fact]
    public void GivenWrongTypedAssertValueField_WhenParsing_ThenReturnsErrorWithoutThrow()
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"BadAssertValue",
              "status":"active",
              "test":[{"name":"t","action":[{"assert":{"expression":"Patient.id","value":123}}]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldContain(e =>
            e.Severity == ParseSeverity.Error && e.Message.Contains("value"));
    }

    [Fact]
    public void GivenMisspelledActionKey_WhenParsing_ThenReturnsErrorMentioningActionIndex()
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"Typo",
              "status":"active",
              "test":[{"name":"t","action":[
                {"operation":{"type":{"code":"read"},"resource":"Patient"}},
                {"asert":{"response":"okay"}}
              ]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldContain(e =>
            e.Severity == ParseSeverity.Error
            && e.Path != null
            && e.Path.Contains("action[1]")
            && e.Message.Contains("asert"));
    }

    [Fact]
    public void GivenActionThatIsNotAnObject_WhenParsing_ThenReturnsError()
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"NotObject",
              "status":"active",
              "test":[{"name":"t","action":["i am a string"]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldContain(e =>
            e.Severity == ParseSeverity.Error && e.Path != null && e.Path.Contains("action[0]"));
    }

    [Theory]
    [InlineData("notEqual")]
    [InlineData("lessThanOrEquals")]
    public void GivenUnknownAssertOperator_WhenParsing_ThenReturnsError(string op)
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"BadOp",
              "status":"active",
              "test":[{"name":"t","action":[{"assert":{"expression":"Patient.id","value":"x","operator":"__OP__"}}]}]
            }
            """.Replace("__OP__", op, StringComparison.Ordinal);

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldContain(e =>
            e.Severity == ParseSeverity.Error && e.Message.Contains(op));
    }

    [Fact]
    public void GivenEvalAssertOperator_WhenParsing_ThenReturnsNotSupportedError()
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"EvalOp",
              "status":"active",
              "test":[{"name":"t","action":[{"assert":{"expression":"Patient.id","value":"x","operator":"eval"}}]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldContain(e =>
            e.Severity == ParseSeverity.Error
            && e.Message.Contains("eval")
            && e.Message.Contains("not supported"));
    }

    [Fact]
    public void GivenSetupAssert_WhenParsing_ThenParsesAsAssertExpression()
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"SetupAssert",
              "status":"active",
              "setup":{"action":[
                {"operation":{"type":{"code":"create"},"resource":"Patient"}},
                {"assert":{"response":"created"}}
              ]}
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        result.Value!.Setup.Count.ShouldBe(2);
        result.Value.Setup[0].ShouldBeOfType<OperationExpression>();
        var assertion = result.Value.Setup[1].ShouldBeOfType<AssertExpression>();
        assertion.Criteria.ShouldBeOfType<ResponseStatusCriteria>().Status.ShouldBe("created");
    }

    [Fact]
    public void GivenTeardownAssert_WhenParsing_ThenReturnsError()
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"TeardownAssert",
              "status":"active",
              "teardown":{"action":[
                {"assert":{"response":"okay"}}
              ]}
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldContain(e =>
            e.Severity == ParseSeverity.Error
            && e.Path != null
            && e.Path.Contains("teardown.action[0]")
            && e.Message.Contains("only contain operations"));
    }

    [Fact]
    public void GivenFixtureWithMissingId_WhenParsing_ThenReturnsError()
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"NoFixtureId",
              "status":"active",
              "fixture":[{"autocreate":true}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldContain(e =>
            e.Severity == ParseSeverity.Error
            && e.Path != null
            && e.Path.Contains("fixture[0]")
            && e.Message.Contains("id"));
    }

    [Fact]
    public void GivenVariableWithMissingName_WhenParsing_ThenReturnsError()
    {
        var json = """
            {
              "resourceType":"TestScript",
              "name":"NoVarName",
              "status":"active",
              "variable":[{"defaultValue":"x"}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldContain(e =>
            e.Severity == ParseSeverity.Error
            && e.Path != null
            && e.Path.Contains("variable[0]")
            && e.Message.Contains("name"));
    }

    [Fact]
    public void GivenTwoAssertsWithSameAnyOfGroup_WhenParsing_ThenBothCarryTheGroupId()
    {
        var json = """
            {
              "resourceType":"TestScript","name":"OrGroup","status":"active",
              "test":[{"name":"t","action":[
                {"assert":{"extension":[{"url":"http://ignixa.io/testscript/assertionAnyOfGroup","valueString":"g1"}],
                  "response":"gone","warningOnly":true}},
                {"assert":{"extension":[{"url":"http://ignixa.io/testscript/assertionAnyOfGroup","valueString":"g1"}],
                  "response":"notFound","warningOnly":true}}
              ]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        var actions = result.Value!.Tests[0].Actions;
        actions[0].ShouldBeOfType<AssertExpression>().AnyOfGroupId.ShouldBe("g1");
        actions[1].ShouldBeOfType<AssertExpression>().AnyOfGroupId.ShouldBe("g1");
    }

    [Fact]
    public void GivenAnyOfGroupWithOnlyOneMember_WhenParsing_ThenReturnsParseError()
    {
        var json = """
            {
              "resourceType":"TestScript","name":"OrGroup","status":"active",
              "test":[{"name":"t","action":[
                {"assert":{"extension":[{"url":"http://ignixa.io/testscript/assertionAnyOfGroup","valueString":"g1"}],
                  "response":"gone"}}
              ]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Message.Contains("at least 2"));
    }

    [Fact]
    public void GivenAnyOfGroupMembersWithDifferentSourceId_WhenParsing_ThenReturnsParseError()
    {
        var json = """
            {
              "resourceType":"TestScript","name":"OrGroup","status":"active",
              "test":[{"name":"t","action":[
                {"assert":{"sourceId":"r1","extension":[{"url":"http://ignixa.io/testscript/assertionAnyOfGroup","valueString":"g1"}],
                  "response":"gone"}},
                {"assert":{"sourceId":"r2","extension":[{"url":"http://ignixa.io/testscript/assertionAnyOfGroup","valueString":"g1"}],
                  "response":"notFound"}}
              ]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Message.Contains("different sourceId"));
    }

    [Fact]
    public void GivenAnyOfGroupMembersWithDifferentDirection_WhenParsing_ThenReturnsParseError()
    {
        var json = """
            {
              "resourceType":"TestScript","name":"OrGroup","status":"active",
              "test":[{"name":"t","action":[
                {"assert":{"direction":"request","extension":[{"url":"http://ignixa.io/testscript/assertionAnyOfGroup","valueString":"g1"}],
                  "requestMethod":"GET"}},
                {"assert":{"extension":[{"url":"http://ignixa.io/testscript/assertionAnyOfGroup","valueString":"g1"}],
                  "response":"notFound"}}
              ]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Message.Contains("direction"));
    }

    [Fact]
    public void GivenAnyOfGroupInSetup_WhenParsing_ThenReturnsParseError()
    {
        var json = """
            {
              "resourceType":"TestScript","name":"OrGroup","status":"active",
              "setup":{"action":[
                {"assert":{"extension":[{"url":"http://ignixa.io/testscript/assertionAnyOfGroup","valueString":"g1"}],
                  "response":"gone"}},
                {"assert":{"extension":[{"url":"http://ignixa.io/testscript/assertionAnyOfGroup","valueString":"g1"}],
                  "response":"notFound"}}
              ]},
              "test":[{"name":"t","action":[{"assert":{"response":"okay"}}]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Message.Contains("only supported within test actions"));
    }

    [Fact]
    public void GivenAssertionWhenResponseStatusExtension_WhenParsing_ThenPopulatesCondition()
    {
        var json = """
            {
              "resourceType":"TestScript","name":"Conditional","status":"active",
              "test":[{"name":"t","action":[
                {"assert":{
                  "sourceId":"readback-response",
                  "extension":[
                    {"url":"http://ignixa.io/testscript/assertionAnyOfGroup","valueString":"g1"},
                    {"url":"http://ignixa.io/testscript/assertionWhenResponseStatus","extension":[
                      {"url":"sourceId","valueString":"delete-response"},
                      {"url":"status","valueInteger":202}
                    ]}
                  ],
                  "responseCode":"200"}},
                {"assert":{"sourceId":"readback-response",
                  "extension":[{"url":"http://ignixa.io/testscript/assertionAnyOfGroup","valueString":"g1"}],
                  "response":"notFound"}}
              ]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        var conditional = result.Value!.Tests[0].Actions[0].ShouldBeOfType<AssertExpression>();
        conditional.WhenResponseStatus.ShouldNotBeNull();
        conditional.WhenResponseStatus!.SourceId.ShouldBe("delete-response");
        conditional.WhenResponseStatus.Statuses.ShouldBe([202]);
    }

    [Fact]
    public void GivenAssertionWhenResponseStatusWithRepeatedStatusChildren_WhenParsing_ThenAllStatusesRecorded()
    {
        var json = """
            {
              "resourceType":"TestScript","name":"Conditional","status":"active",
              "test":[{"name":"t","action":[
                {"assert":{
                  "extension":[
                    {"url":"http://ignixa.io/testscript/assertionAnyOfGroup","valueString":"g1"},
                    {"url":"http://ignixa.io/testscript/assertionWhenResponseStatus","extension":[
                      {"url":"sourceId","valueString":"delete-response"},
                      {"url":"status","valueInteger":200},
                      {"url":"status","valueInteger":202},
                      {"url":"status","valueInteger":204}
                    ]}
                  ],
                  "response":"okay"}},
                {"assert":{"extension":[{"url":"http://ignixa.io/testscript/assertionAnyOfGroup","valueString":"g1"}],
                  "response":"gone"}}
              ]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        result.Value!.Tests[0].Actions[0].ShouldBeOfType<AssertExpression>()
            .WhenResponseStatus!.Statuses.ShouldBe([200, 202, 204]);
    }

    [Fact]
    public void GivenAssertionWhenResponseStatusWithoutSourceId_WhenParsing_ThenReturnsParseError()
    {
        var json = """
            {
              "resourceType":"TestScript","name":"Conditional","status":"active",
              "test":[{"name":"t","action":[
                {"assert":{
                  "extension":[
                    {"url":"http://ignixa.io/testscript/assertionAnyOfGroup","valueString":"g1"},
                    {"url":"http://ignixa.io/testscript/assertionWhenResponseStatus","extension":[
                      {"url":"status","valueInteger":202}
                    ]}
                  ],
                  "response":"okay"}},
                {"assert":{"extension":[{"url":"http://ignixa.io/testscript/assertionAnyOfGroup","valueString":"g1"}],
                  "response":"gone"}}
              ]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Message.Contains("sourceId"));
    }

    [Fact]
    public void GivenAssertionWhenResponseStatusWithOutOfRangeStatus_WhenParsing_ThenReturnsParseError()
    {
        var json = """
            {
              "resourceType":"TestScript","name":"Conditional","status":"active",
              "test":[{"name":"t","action":[
                {"assert":{
                  "extension":[
                    {"url":"http://ignixa.io/testscript/assertionAnyOfGroup","valueString":"g1"},
                    {"url":"http://ignixa.io/testscript/assertionWhenResponseStatus","extension":[
                      {"url":"sourceId","valueString":"delete-response"},
                      {"url":"status","valueInteger":999}
                    ]}
                  ],
                  "response":"okay"}},
                {"assert":{"extension":[{"url":"http://ignixa.io/testscript/assertionAnyOfGroup","valueString":"g1"}],
                  "response":"gone"}}
              ]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Message.Contains("100") && e.Message.Contains("599"));
    }

    [Fact]
    public void GivenWaitForExtensionWithNoChildren_WhenParsing_ThenDefaultsApply()
    {
        var json = """
            {
              "resourceType":"TestScript","name":"WaitForDefaults","status":"active",
              "test":[{"name":"t","action":[
                {"operation":{"type":{"code":"read"},"resource":"Patient","params":"/1",
                  "extension":[{"url":"http://ignixa.io/testscript/waitFor"}]}}
              ]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        var op = result.Value!.Tests[0].Actions[0].ShouldBeOfType<OperationExpression>();
        op.WaitFor.ShouldNotBeNull();
        op.WaitFor!.PollingStatusCode.ShouldBe(202);
        op.WaitFor.MaxAttempts.ShouldBe(60);
        op.WaitFor.IntervalMs.ShouldBe(1000);
    }

    [Fact]
    public void GivenWaitForExtensionWithExplicitChildren_WhenParsing_ThenValuesOverrideDefaults()
    {
        var json = """
            {
              "resourceType":"TestScript","name":"WaitForExplicit","status":"active",
              "test":[{"name":"t","action":[
                {"operation":{"type":{"code":"read"},"resource":"Patient","params":"/1",
                  "extension":[{"url":"http://ignixa.io/testscript/waitFor","extension":[
                    {"url":"pollingStatusCode","valueInteger":404},
                    {"url":"maxAttempts","valueInteger":5},
                    {"url":"intervalMs","valueInteger":250}
                  ]}]}}
              ]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        var op = result.Value!.Tests[0].Actions[0].ShouldBeOfType<OperationExpression>();
        op.WaitFor!.PollingStatusCode.ShouldBe(404);
        op.WaitFor.MaxAttempts.ShouldBe(5);
        op.WaitFor.IntervalMs.ShouldBe(250);
    }

    [Fact]
    public void GivenWaitForPollingStatusCodeOutOfRange_WhenParsing_ThenReturnsParseError()
    {
        var json = """
            {
              "resourceType":"TestScript","name":"WaitForInvalidStatus","status":"active",
              "test":[{"name":"t","action":[
                {"operation":{"type":{"code":"read"},"resource":"Patient","params":"/1",
                  "extension":[{"url":"http://ignixa.io/testscript/waitFor","extension":[
                    {"url":"pollingStatusCode","valueInteger":999}
                  ]}]}}
              ]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Message.Contains("100") && e.Message.Contains("599"));
    }

    [Fact]
    public void GivenWaitForMaxAttemptsLessThanOne_WhenParsing_ThenReturnsParseError()
    {
        var json = """
            {
              "resourceType":"TestScript","name":"WaitForInvalidMaxAttempts","status":"active",
              "test":[{"name":"t","action":[
                {"operation":{"type":{"code":"read"},"resource":"Patient","params":"/1",
                  "extension":[{"url":"http://ignixa.io/testscript/waitFor","extension":[
                    {"url":"maxAttempts","valueInteger":0}
                  ]}]}}
              ]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Message.Contains("at least 1"));
    }

    [Fact]
    public void GivenWaitForIntervalMsNegative_WhenParsing_ThenReturnsParseError()
    {
        var json = """
            {
              "resourceType":"TestScript","name":"WaitForInvalidInterval","status":"active",
              "test":[{"name":"t","action":[
                {"operation":{"type":{"code":"read"},"resource":"Patient","params":"/1",
                  "extension":[{"url":"http://ignixa.io/testscript/waitFor","extension":[
                    {"url":"intervalMs","valueInteger":-1}
                  ]}]}}
              ]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Message.Contains("non-negative"));
    }

    [Fact]
    public void GivenOperationWithNoWaitForExtension_WhenParsing_ThenWaitForIsNull()
    {
        var json = """
            {
              "resourceType":"TestScript","name":"NoWaitFor","status":"active",
              "test":[{"name":"t","action":[
                {"operation":{"type":{"code":"read"},"resource":"Patient","params":"/1"}}
              ]}]
            }
            """;

        var result = TestScriptParser.Parse(json);

        result.IsSuccess.ShouldBeTrue();
        var op = result.Value!.Tests[0].Actions[0].ShouldBeOfType<OperationExpression>();
        op.WaitFor.ShouldBeNull();
    }
}
