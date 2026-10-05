using Ignixa.Application.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Infrastructure;

/// <summary>
/// Binds the actual shipped src/Application/Ignixa.Web/appsettings.json (copied into the test output as
/// TestData/Ignixa.Web.appsettings.json -- see the csproj) rather than an in-memory replica. A hand-built
/// replica would not have caught the regression this guards: InheritConnectionStringFromTenant was shipped
/// as a JSON boolean where TenantStorageConfiguration declares it as an int, which
/// Microsoft.Extensions.Configuration's List&lt;T&gt; binder silently drops -- tenant 0 (the system
/// partition) simply vanished from the bound list with no error.
/// </summary>
public class TenantConfigurationShippedSettingsTests
{
    [Fact]
    public async Task GivenShippedAppSettings_WhenTenantsLoaded_ThenSystemPartitionIsPresent()
    {
        // Arrange
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "TestData", "Ignixa.Web.appsettings.json");
        File.Exists(settingsPath).ShouldBeTrue($"expected the shipped appsettings.json to be copied to '{settingsPath}'");

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(settingsPath, optional: false)
            .Build();
        var store = new AppSettingsTenantConfigurationStore(configuration, NullLogger<AppSettingsTenantConfigurationStore>.Instance);

        // Act
        var systemPartition = await store.GetTenantConfigurationAsync(0);

        // Assert
        systemPartition.ShouldNotBeNull("tenant 0 must not be silently dropped by configuration binding");
        systemPartition.TenantId.ShouldBe(0);
        systemPartition.IsSystemPartition.ShouldBeTrue();
    }
}
