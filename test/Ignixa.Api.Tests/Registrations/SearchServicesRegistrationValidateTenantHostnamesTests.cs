using Autofac;
using Ignixa.Api.Registrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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

    /// <summary>
    /// Pins that <c>RegisterBuildCallback</c> actually fires the validation at <c>ContainerBuilder.Build()</c>
    /// -- eagerly, before any request is served -- rather than only pinning that
    /// <see cref="SearchServicesRegistration.ValidateTenantHostnames"/> throws when called directly (the gap
    /// the earlier tests in this file leave open). This registers the identical
    /// <c>RegisterBuildCallback(...)</c> delegate used in <c>RegisterSearchServices</c> over a minimal
    /// container -- just <see cref="IConfiguration"/> and <see cref="ILoggerFactory"/> -- rather than standing
    /// up the full search-services registration graph, which pulls in many unrelated dependencies
    /// (schema providers, package repositories, etc.) that add noise without changing what this test proves.
    /// </summary>
    [Fact]
    public void GivenADuplicateHostnameConfiguration_WhenTheContainerIsBuilt_ThenBuildThrows()
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

        var builder = new ContainerBuilder();
        builder.RegisterInstance(configuration).As<IConfiguration>();
        builder.RegisterInstance(NullLoggerFactory.Instance).As<ILoggerFactory>();
        builder.RegisterBuildCallback(container =>
            SearchServicesRegistration.ValidateTenantHostnames(
                container.Resolve<IConfiguration>(),
                container.Resolve<ILoggerFactory>()));

        Should.Throw<InvalidOperationException>(() => builder.Build());
    }
}
