using System.Globalization;
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

    private static DiscoveredScenario GetMetabolicSyndromeProgressionScenario()
    {
        var scenario = ScenarioCatalog.Find("MetabolicSyndromeProgression");
        scenario.ShouldNotBeNull();
        return scenario!;
    }

    [Fact]
    public void GivenValidParamValues_WhenParsingOverrides_ThenReturnsConvertedValues()
    {
        var scenario = GetDiabeticPatientScenario();
        var paramValues = new[] { "age=70", "severity=4", "gender=female" };

        var success = ScenarioCommand.TryParseParameterOverrides(scenario.Id, scenario.Parameters, paramValues, out var overrides, out var error);

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

        var success = ScenarioCommand.TryParseParameterOverrides(scenario.Id, scenario.Parameters, paramValues, out _, out var error);

        success.ShouldBeFalse();
        error.ShouldNotBeNull();
        error.ShouldContain("notAParameter");
    }

    [Fact]
    public void GivenNonNumericValueForIntParameter_WhenParsingOverrides_ThenReturnsFalseWithError()
    {
        var scenario = GetDiabeticPatientScenario();
        var paramValues = new[] { "age=notanumber" };

        var success = ScenarioCommand.TryParseParameterOverrides(scenario.Id, scenario.Parameters, paramValues, out _, out var error);

        success.ShouldBeFalse();
        error.ShouldNotBeNull();
        error.ShouldContain("age");
    }

    [Fact]
    public void GivenOutOfRangeValueForIntParameter_WhenParsingOverrides_ThenErrorNamesTheRangeNotAConversionFailure()
    {
        var scenario = GetDiabeticPatientScenario();
        var paramValues = new[] { "age=5" };

        var success = ScenarioCommand.TryParseParameterOverrides(scenario.Id, scenario.Parameters, paramValues, out _, out var error);

        success.ShouldBeFalse();
        error.ShouldNotBeNull();
        // 5 converts to int just fine — the error must name the actual problem (the declared
        // range), not claim a type-conversion failure that didn't happen.
        error.ShouldNotContain("Cannot convert");
        error.ShouldContain("5");
        error.ShouldContain("18");
        error.ShouldContain("90");
    }

    [Fact]
    public void GivenMalformedParamValue_WhenParsingOverrides_ThenReturnsFalseWithError()
    {
        var scenario = GetDiabeticPatientScenario();
        var paramValues = new[] { "age" };

        var success = ScenarioCommand.TryParseParameterOverrides(scenario.Id, scenario.Parameters, paramValues, out _, out var error);

        success.ShouldBeFalse();
        error.ShouldNotBeNull();
        error.ShouldContain("Invalid --param value");
    }

    [Fact]
    public void GivenDecimalParamValue_WhenParsingOverridesUnderNonInvariantCulture_ThenParsesAsInvariantDecimal()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            // de-DE treats '.' as a thousands separator and ',' as the decimal point.
            // Parsing must stay culture-invariant so "35.5" always means 35.5, not 355.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var scenario = GetMetabolicSyndromeProgressionScenario();
            var paramValues = new[] { "startingBMI=35.5" };

            var success = ScenarioCommand.TryParseParameterOverrides(scenario.Id, scenario.Parameters, paramValues, out var overrides, out var error);

            success.ShouldBeTrue();
            error.ShouldBeNull();
            overrides["startingBMI"].ShouldBe(35.5m);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void GivenEnumParamValue_WhenParsingOverrides_ThenConvertsToEnumCaseInsensitively()
    {
        IReadOnlyList<DiscoveredScenarioParameter> parameters =
            [new() { Name = "severity", Type = typeof(SeverityLevel) }];
        var paramValues = new[] { "severity=high" };

        var success = ScenarioCommand.TryParseParameterOverrides("SyntheticEnumScenario", parameters, paramValues, out var overrides, out var error);

        success.ShouldBeTrue();
        error.ShouldBeNull();
        overrides["severity"].ShouldBe(SeverityLevel.High);
    }

    [Fact]
    public void GivenInvalidEnumParamValue_WhenParsingOverrides_ThenReturnsFalseWithError()
    {
        IReadOnlyList<DiscoveredScenarioParameter> parameters =
            [new() { Name = "severity", Type = typeof(SeverityLevel) }];
        var paramValues = new[] { "severity=notavalue" };

        var success = ScenarioCommand.TryParseParameterOverrides("SyntheticEnumScenario", parameters, paramValues, out _, out var error);

        success.ShouldBeFalse();
        error.ShouldNotBeNull();
        error.ShouldContain("severity");
    }

    private enum SeverityLevel
    {
        Low = 0,
        High = 1,
    }
}
