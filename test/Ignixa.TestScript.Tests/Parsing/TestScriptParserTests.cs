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
