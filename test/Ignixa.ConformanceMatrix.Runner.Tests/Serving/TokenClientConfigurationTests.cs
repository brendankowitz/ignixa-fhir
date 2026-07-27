using Ignixa.ConformanceMatrix.Runner.Serving;
using Shouldly;

namespace Ignixa.ConformanceMatrix.Runner.Tests.Serving;

public class TokenClientConfigurationTests
{
    [Fact]
    public void GivenCompleteConfiguration_WhenValidating_ThenReturnsNull()
    {
        // Arrange
        var config = new TokenClientConfiguration("https://login.test/token", "client-id", "client-secret", "scope-a scope-b");

        // Act & Assert
        config.Validate().ShouldBeNull();
    }

    [Fact]
    public void GivenBlankClientId_WhenValidating_ThenNamesTheMissingVariable()
    {
        // Arrange
        var config = new TokenClientConfiguration("https://login.test/token", "", "client-secret", null);

        // Act
        var error = config.Validate();

        // Assert
        error.ShouldNotBeNull();
        error.ShouldContain("FHIR_CLIENT_ID");
    }

    [Fact]
    public void GivenBlankClientSecret_WhenValidating_ThenNamesTheMissingVariable()
    {
        // Arrange
        var config = new TokenClientConfiguration("https://login.test/token", "client-id", "", null);

        // Act
        var error = config.Validate();

        // Assert
        error.ShouldNotBeNull();
        error.ShouldContain("FHIR_CLIENT_SECRET");
    }

    [Fact]
    public void GivenRelativeTokenUrl_WhenValidating_ThenReportsInvalidUrl()
    {
        // Arrange
        var config = new TokenClientConfiguration("not-a-url", "client-id", "client-secret", null);

        // Act
        var error = config.Validate();

        // Assert
        error.ShouldNotBeNull();
        error.ShouldContain("FHIR_TOKEN_URL");
    }
}
