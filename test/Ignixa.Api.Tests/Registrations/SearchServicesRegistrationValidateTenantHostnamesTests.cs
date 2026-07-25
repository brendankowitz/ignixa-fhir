using Ignixa.Api.Registrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.Api.Tests.Registrations;

public class SearchServicesRegistrationValidateTenantHostnamesTests
{
    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void GivenADuplicateHostnameConfiguration_WhenValidatingEagerly_ThenThrows()
    {
        var configuration = Configuration(new()
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

        // A duplicate hostname is an ambiguous config -- it must not boot, and RegisterBuildCallback runs this
        // eagerly at container Build() so the failure surfaces at startup, not on the first request.
        Should.Throw<InvalidOperationException>(() =>
            SearchServicesRegistration.ValidateTenantHostnames(configuration, NullLoggerFactory.Instance));
    }

    [Fact]
    public void GivenOnlyAFormatProblem_WhenValidatingEagerly_ThenDoesNotThrow()
    {
        var configuration = Configuration(new()
        {
            ["Tenants:Configurations:0:TenantId"] = "1",
            ["Tenants:Configurations:0:DisplayName"] = "Acme",
            ["Tenants:Configurations:0:FhirVersion"] = "4.0",
            ["Tenants:Configurations:0:Hostnames:0"] = "fhir1.example.org:8080",
        });

        // A malformed hostname is logged eagerly (see TenantHostnameValidationTests) but is not fatal: one
        // operator typo must not take every tenant down. AppSettingsTenantConfigurationStore excludes the
        // malformed host from the routing index, so it simply never routes.
        Should.NotThrow(() =>
            SearchServicesRegistration.ValidateTenantHostnames(configuration, NullLoggerFactory.Instance));
    }

    [Fact]
    public void GivenValidUniqueHostnames_WhenValidatingEagerly_ThenDoesNotThrow()
    {
        var configuration = Configuration(new()
        {
            ["Tenants:Configurations:0:TenantId"] = "1",
            ["Tenants:Configurations:0:DisplayName"] = "Acme",
            ["Tenants:Configurations:0:FhirVersion"] = "4.0",
            ["Tenants:Configurations:0:Hostnames:0"] = "fhir1.example.org",
        });

        Should.NotThrow(() =>
            SearchServicesRegistration.ValidateTenantHostnames(configuration, NullLoggerFactory.Instance));
    }
}
