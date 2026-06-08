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
        test.Parameters.Count.ShouldBe(1);
        test.Parameters[0].VariableName.ShouldBe("searchDate");
        test.Parameters[0].Values.ShouldBe(["2028", "2028-06", "2028-06-15", "2028-06-15T12:00:00Z"]);
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
        result.Value!.Tests[0].Parameters.ShouldBeEmpty();
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
}
