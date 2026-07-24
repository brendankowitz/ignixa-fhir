using Ignixa.Application.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Infrastructure;

public class TenantHostIndexTests
{
    private static AppSettingsTenantConfigurationStore Store(Dictionary<string, string?> values) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            NullLogger<AppSettingsTenantConfigurationStore>.Instance);

    [Fact]
    public async Task GivenAKnownHost_WhenResolvingByHost_ThenReturnsTheOwningTenant()
    {
        var store = Store(new()
        {
            ["Tenants:Configurations:0:TenantId"] = "1",
            ["Tenants:Configurations:0:DisplayName"] = "Acme",
            ["Tenants:Configurations:0:FhirVersion"] = "4.0",
            ["Tenants:Configurations:0:Hostnames:0"] = "fhir1.example.org",
        });

        var tenant = await store.ResolveByHostAsync("FHIR1.EXAMPLE.ORG");

        tenant.ShouldNotBeNull();
        tenant.TenantId.ShouldBe(1);
    }

    [Fact]
    public async Task GivenAnUnknownHost_WhenResolvingByHost_ThenReturnsNull()
    {
        var store = Store(new()
        {
            ["Tenants:Configurations:0:TenantId"] = "1",
            ["Tenants:Configurations:0:DisplayName"] = "Acme",
            ["Tenants:Configurations:0:FhirVersion"] = "4.0",
            ["Tenants:Configurations:0:Hostnames:0"] = "fhir1.example.org",
        });

        (await store.ResolveByHostAsync("evil.attacker.test")).ShouldBeNull();
    }

    [Fact]
    public async Task GivenTheSameHostOnTwoTenants_WhenResolvingByHost_ThenThrowsAtLoad()
    {
        var store = Store(new()
        {
            ["Tenants:Configurations:0:TenantId"] = "1",
            ["Tenants:Configurations:0:DisplayName"] = "Acme",
            ["Tenants:Configurations:0:FhirVersion"] = "4.0",
            ["Tenants:Configurations:0:Hostnames:0"] = "shared.example.org",
            ["Tenants:Configurations:1:TenantId"] = "2",
            ["Tenants:Configurations:1:DisplayName"] = "Beta",
            ["Tenants:Configurations:1:FhirVersion"] = "4.0",
            ["Tenants:Configurations:1:Hostnames:0"] = "shared.example.org",
        });

        // A host that maps to two tenants is a cross-tenant confusion hazard; fail loudly, not silently.
        await Should.ThrowAsync<InvalidOperationException>(async () => await store.ResolveByHostAsync("shared.example.org"));
    }
}
