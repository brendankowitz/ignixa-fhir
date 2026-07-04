using Ignixa.FhirFakes;
using Ignixa.FhirFakes.Cli.Commands;
using Shouldly;

namespace Ignixa.FhirFakes.Cli.Tests;

public class ResourceCommandThemeParseTests
{
    [Fact]
    public void GivenKebabCaseTheme_WhenParsing_ThenResolvesToDomain()
    {
        var parsed = ResourceCommand.TryParseTheme("orthopedic-surgery", out var theme);

        parsed.ShouldBeTrue();
        theme.ShouldBe(ClinicalDomain.OrthopedicSurgery);
    }

    [Fact]
    public void GivenNone_WhenParsing_ThenResolvesToUnspecified()
    {
        var parsed = ResourceCommand.TryParseTheme("none", out var theme);

        parsed.ShouldBeTrue();
        theme.ShouldBe(ClinicalDomain.Unspecified);
    }

    [Fact]
    public void GivenEmptyValue_WhenParsing_ThenResolvesToNullRandomTheme()
    {
        var parsed = ResourceCommand.TryParseTheme(null, out var theme);

        parsed.ShouldBeTrue();
        theme.ShouldBeNull();
    }

    [Fact]
    public void GivenInvalidValue_WhenParsing_ThenReturnsFalse()
    {
        var parsed = ResourceCommand.TryParseTheme("notadomain", out var theme);

        parsed.ShouldBeFalse();
        theme.ShouldBeNull();
    }
}
