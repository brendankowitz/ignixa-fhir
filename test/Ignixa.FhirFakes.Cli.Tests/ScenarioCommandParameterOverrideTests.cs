using Shouldly;
using Ignixa.FhirFakes.Cli.Commands;
using Ignixa.FhirFakes.Scenarios;

namespace Ignixa.FhirFakes.Cli.Tests;

public class ScenarioCommandParameterOverrideTests
{
    private static DiscoveredScenario GetDiabeticPatientScenario()
    {
        var scenario = ScenarioCatalog.Find("DiabeticPatient");
        scenario.ShouldNotBeNull();
        return scenario!;
    }

    [Fact]
    public void GivenValidParamValues_WhenParsingOverrides_ThenReturnsConvertedValues()
    {
        var scenario = GetDiabeticPatientScenario();
        var paramValues = new[] { "age=70", "severity=4", "gender=female" };

        var success = ScenarioCommand.TryParseParameterOverrides(scenario, paramValues, out var overrides, out var error);

        success.ShouldBeTrue();
        error.ShouldBeNull();
        overrides["age"].ShouldBe(70);
        overrides["severity"].ShouldBe(4);
        overrides["gender"].ShouldBe("female");
    }

    [Fact]
    public void GivenUnknownParameterName_WhenParsingOverrides_ThenReturnsFalseWithError()
    {
        var scenario = GetDiabeticPatientScenario();
        var paramValues = new[] { "notAParameter=123" };

        var success = ScenarioCommand.TryParseParameterOverrides(scenario, paramValues, out _, out var error);

        success.ShouldBeFalse();
        error.ShouldNotBeNull();
        error.ShouldContain("notAParameter");
    }

    [Fact]
    public void GivenNonNumericValueForIntParameter_WhenParsingOverrides_ThenReturnsFalseWithError()
    {
        var scenario = GetDiabeticPatientScenario();
        var paramValues = new[] { "age=notanumber" };

        var success = ScenarioCommand.TryParseParameterOverrides(scenario, paramValues, out _, out var error);

        success.ShouldBeFalse();
        error.ShouldNotBeNull();
        error.ShouldContain("age");
    }

    [Fact]
    public void GivenMalformedParamValue_WhenParsingOverrides_ThenReturnsFalseWithError()
    {
        var scenario = GetDiabeticPatientScenario();
        var paramValues = new[] { "age" };

        var success = ScenarioCommand.TryParseParameterOverrides(scenario, paramValues, out _, out var error);

        success.ShouldBeFalse();
        error.ShouldNotBeNull();
        error.ShouldContain("Invalid --param value");
    }
}
