using Ignixa.Validation.Cli.Commands;
using Shouldly;

namespace Ignixa.Validation.Cli.Tests;

public class SplitPackageSpecTests
{
    [Theory]
    [InlineData("hl7.fhir.us.core@6.1.0", "hl7.fhir.us.core", "6.1.0")]
    [InlineData("hl7.fhir.r4.core@4.0.1", "hl7.fhir.r4.core", "4.0.1")]
    public void GivenValidSpec_WhenSplit_ThenReturnsIdAndVersion(string spec, string expectedId, string expectedVersion)
    {
        var (id, version) = ValidateCommand.SplitPackageSpec(spec);
        id.ShouldBe(expectedId);
        version.ShouldBe(expectedVersion);
    }

    [Theory]
    [InlineData("novat")]
    [InlineData("@1.0.0")]
    [InlineData("hl7.fhir.r4.core@")]
    public void GivenInvalidSpec_WhenSplit_ThenReturnsNulls(string spec)
    {
        var (id, version) = ValidateCommand.SplitPackageSpec(spec);
        id.ShouldBeNull();
        version.ShouldBeNull();
    }

    [Fact]
    public void GivenSpecWithMultipleAtSigns_WhenSplit_ThenUsesLastAt()
    {
        var (id, version) = ValidateCommand.SplitPackageSpec("hl7@fhir@1.0.0");
        id.ShouldBe("hl7@fhir");
        version.ShouldBe("1.0.0");
    }
}
