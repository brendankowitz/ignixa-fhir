using Ignixa.TestScript.Expressions;
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
        assert1.Response.ShouldBe("okay");

        var assert2 = result.Value.Tests[0].Actions[2].ShouldBeOfType<AssertExpression>();
        assert2.Resource.ShouldBe("Patient");
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
}
