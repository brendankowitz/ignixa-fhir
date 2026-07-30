using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.Tests;

/// <summary>
/// The guard this covers had no tests at all while it lived on
/// <c>SqlEntityFrameworkRepositoryFactory</c>, which is how it went unnoticed that it ignored its own
/// injected environment and read <c>ASPNETCORE_ENVIRONMENT</c> off the process instead.
/// </summary>
public class ManagedIdentityConnectionStringValidatorTests
{
    private const string PasswordConnectionString =
        "Server=tcp:fhir.database.windows.net,1433;Database=Fhir;User ID=sa;Password=hunter2;";

    private const string IntegratedSecurityConnectionString =
        "Server=localhost;Integrated Security=true;TrustServerCertificate=true;";

    private static ManagedIdentityConnectionStringValidator CreateValidator(string environmentName)
        => new(environmentName, NullLogger<ManagedIdentityConnectionStringValidator>.Instance);

    [Fact]
    public void GivenProduction_WhenValidatingAPasswordBearingConnectionString_ThenItThrows()
    {
        // Arrange
        var validator = CreateValidator("Production");

        // Act
        var ex = Should.Throw<InvalidOperationException>(() => validator.Validate(PasswordConnectionString, 1));

        // Assert
        ex.Message.ShouldContain("Managed Identity");
        ex.Message.ShouldContain("1");
    }

    [Fact]
    public void GivenDevelopment_WhenValidatingAPasswordBearingConnectionString_ThenItDoesNotThrow()
    {
        // Arrange
        var validator = CreateValidator("Development");

        // Act & Assert
        Should.NotThrow(() => validator.Validate(PasswordConnectionString, 1));
    }

    /// <summary>
    /// The shape every test fixture and local run uses. It must stay legal in Production too, otherwise
    /// pinning a fixture to "Development" would be the only thing keeping it alive.
    /// </summary>
    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    [InlineData("Test")]
    public void GivenAnIntegratedSecurityConnectionString_WhenValidatingInAnyEnvironment_ThenItDoesNotThrow(string environmentName)
    {
        // Arrange
        var validator = CreateValidator(environmentName);

        // Act & Assert
        Should.NotThrow(() => validator.Validate(IntegratedSecurityConnectionString, 1));
    }

    /// <summary>
    /// The regression itself: the injected environment decides, and the process variable is not consulted.
    /// Before the fix, an unset (or non-Production) <c>ASPNETCORE_ENVIRONMENT</c> disabled the guard no
    /// matter what the host passed in.
    /// </summary>
    [Fact]
    public void GivenProductionInjectedWhileTheEnvironmentVariableSaysDevelopment_WhenValidating_ThenTheInjectedValueDecides()
    {
        // Arrange
        var original = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        try
        {
            var validator = CreateValidator("Production");

            // Act & Assert
            Should.Throw<InvalidOperationException>(() => validator.Validate(PasswordConnectionString, 1));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", original);
        }
    }
}
