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

    [Fact]
    public async Task GivenAMalformedHostname_WhenBuildingTheHostIndex_ThenItIsNotIndexed()
    {
        var store = Store(new()
        {
            ["Tenants:Configurations:0:TenantId"] = "1",
            ["Tenants:Configurations:0:DisplayName"] = "Acme",
            ["Tenants:Configurations:0:FhirVersion"] = "4.0",
            ["Tenants:Configurations:0:Hostnames:0"] = "fhir1.example.org:8080",
            ["Tenants:Configurations:0:Hostnames:1"] = "fhir1.example.org",
        });

        // The malformed (ported) hostname must never resolve a tenant: FhirServiceBaseUriResolver would
        // never recognize it as an outbound self-reference either, so indexing it inbound would silently
        // dead-route rather than fail closed.
        (await store.ResolveByHostAsync("fhir1.example.org:8080")).ShouldBeNull();

        var tenant = await store.ResolveByHostAsync("fhir1.example.org");
        tenant.ShouldNotBeNull();
        tenant.TenantId.ShouldBe(1);
    }

    [Fact]
    public async Task GivenAHostnameOnTheSystemPartition_WhenResolvingByHost_ThenReturnsNull()
    {
        var store = Store(new()
        {
            ["Tenants:Configurations:0:TenantId"] = "0",
            ["Tenants:Configurations:0:DisplayName"] = "System Partition",
            ["Tenants:Configurations:0:FhirVersion"] = "4.0",
            ["Tenants:Configurations:0:IsSystemPartition"] = "true",
            ["Tenants:Configurations:0:Hostnames:0"] = "system.example.org",
        });

        // Tenant 0 is the reserved system partition and must never be reachable over a hostname route.
        (await store.ResolveByHostAsync("system.example.org")).ShouldBeNull();
    }

    [Fact]
    public async Task GivenAHostnameOnAnInactiveTenant_WhenResolvingByHost_ThenReturnsNull()
    {
        var store = Store(new()
        {
            ["Tenants:Configurations:0:TenantId"] = "1",
            ["Tenants:Configurations:0:DisplayName"] = "Acme",
            ["Tenants:Configurations:0:FhirVersion"] = "4.0",
            ["Tenants:Configurations:0:IsActive"] = "false",
            ["Tenants:Configurations:0:Hostnames:0"] = "inactive.example.org",
        });

        (await store.ResolveByHostAsync("inactive.example.org")).ShouldBeNull();
    }
}
