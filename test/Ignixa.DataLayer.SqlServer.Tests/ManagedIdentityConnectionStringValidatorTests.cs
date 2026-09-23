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

    /// <summary>
    /// THE FIX. A literal <c>"Password="</c> substring scan misses this: ADO.NET connection strings allow
    /// whitespace around <c>=</c>, so <c>SqlConnectionStringBuilder</c> is the only thing that reliably
    /// recognizes every legal spelling of the keyword.
    /// </summary>
    [Fact]
    public void GivenProduction_WhenValidatingAConnectionStringWithWhitespaceAroundTheEquals_ThenItStillThrows()
    {
        // Arrange
        var validator = CreateValidator("Production");
        const string connectionString = "Server=tcp:fhir.database.windows.net,1433;Database=Fhir;User ID = sa;Password = hunter2;";

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => validator.Validate(connectionString, 1));
    }

    /// <summary>
    /// <c>pwd</c> is a documented alias for the same keyword, so it must be caught exactly like
    /// <c>Password</c> rather than only being covered by coincidence.
    /// </summary>
    [Fact]
    public void GivenProduction_WhenValidatingAConnectionStringUsingThePwdAlias_ThenItThrows()
    {
        // Arrange
        var validator = CreateValidator("Production");
        const string connectionString = "Server=tcp:fhir.database.windows.net,1433;Database=Fhir;User ID=sa;pwd=hunter2;";

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => validator.Validate(connectionString, 1));
    }

    /// <summary>
    /// A connection string too malformed to parse cannot be evaluated for credentials, so it is rethrown as
    /// an <see cref="InvalidOperationException"/> naming the tenant rather than left as a bare
    /// <see cref="ArgumentException"/> that gives no indication of which tenant's configuration is bad.
    /// </summary>
    [Fact]
    public void GivenProduction_WhenValidatingAMalformedConnectionString_ThenItThrowsNamingTheTenant()
    {
        // Arrange
        var validator = CreateValidator("Production");
        const string connectionString = "this is not a connection string==;;";

        // Act
        var ex = Should.Throw<InvalidOperationException>(() => validator.Validate(connectionString, 7));

        // Assert
        ex.Message.ShouldContain("7");
    }

    /// <summary>
    /// An explicit but empty password ("Password=;") is a known, accepted gap: <see cref="SqlConnectionStringBuilder"/>
    /// itself drops an empty value from its keyword collection when parsing, so there is no parsed
    /// representation left to distinguish "explicitly empty" from "absent". This pins the current,
    /// intentional behavior rather than asserting a guarantee the validator cannot make.
    /// </summary>
    [Fact]
    public void GivenProduction_WhenValidatingAConnectionStringWithAnExplicitEmptyPassword_ThenItDoesNotThrow()
    {
        // Arrange
        var validator = CreateValidator("Production");
        const string connectionString = "Server=tcp:fhir.database.windows.net,1433;Database=Fhir;User ID=sa;Password=;";

        // Act & Assert
        Should.NotThrow(() => validator.Validate(connectionString, 1));
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
