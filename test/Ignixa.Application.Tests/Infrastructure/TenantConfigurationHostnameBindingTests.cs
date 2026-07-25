using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Infrastructure;

public class TenantConfigurationHostnameBindingTests
{
    [Fact]
    public async Task GivenHostnamesInConfig_WhenTenantLoaded_ThenHostnamesAreBound()
    {
        // Arrange
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenants:Configurations:0:TenantId"] = "1",
            ["Tenants:Configurations:0:DisplayName"] = "Acme",
            ["Tenants:Configurations:0:FhirVersion"] = "4.0",
            ["Tenants:Configurations:0:Hostnames:0"] = "fhir1.example.org",
            ["Tenants:Configurations:0:Hostnames:1"] = "acme.example.org",
        }).Build();
        var store = new AppSettingsTenantConfigurationStore(config, NullLogger<AppSettingsTenantConfigurationStore>.Instance);

        // Act
        var tenant = await store.GetTenantConfigurationAsync(1);

        // Assert
        tenant.ShouldNotBeNull();
        tenant.Hostnames.ShouldBe(["fhir1.example.org", "acme.example.org"]);
    }

    [Fact]
    public async Task GivenNoHostnamesInConfig_WhenTenantLoaded_ThenHostnamesIsEmpty()
    {
        // Arrange
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenants:Configurations:0:TenantId"] = "1",
            ["Tenants:Configurations:0:DisplayName"] = "Acme",
            ["Tenants:Configurations:0:FhirVersion"] = "4.0",
        }).Build();
        var store = new AppSettingsTenantConfigurationStore(config, NullLogger<AppSettingsTenantConfigurationStore>.Instance);

        // Act
        var tenant = await store.GetTenantConfigurationAsync(1);

        // Assert
        tenant.ShouldNotBeNull();
        tenant.Hostnames.ShouldBeEmpty();
    }
}
