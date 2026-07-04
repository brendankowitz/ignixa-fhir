using Shouldly;
using Ignixa.FhirFakes.Cli.Commands;

namespace Ignixa.FhirFakes.Cli.Tests;

public class ResourceCommandFindCityTests
{
    [Fact]
    public void GivenValidCityName_WhenFindingCity_ThenReturnsCity()
    {
        var city = ResourceCommand.FindCity("Seattle");

        city.ShouldNotBeNull();
        city!.Name.ShouldBe("Seattle");
    }

    [Fact]
    public void GivenInvalidCityName_WhenFindingCity_ThenReturnsNull()
    {
        var city = ResourceCommand.FindCity("NonExistentCity");

        city.ShouldBeNull();
    }
}
