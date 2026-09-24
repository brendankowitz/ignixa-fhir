using Autofac;
using Autofac.Extensions.DependencyInjection;
using Ignixa.Api.Registrations;
using Ignixa.DataLayer.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.Api.Tests.Registrations;

/// <summary>
/// <see cref="SqlExecutionService"/> now depends on <see cref="ManagedIdentityConnectionStringValidator"/>,
/// but the two are registered on opposite sides of the composition: the execution service through the
/// <see cref="IServiceCollection"/> that <c>AddIgnixaSqlServerSchemaDeployment</c> populates, the validator
/// through the Autofac <see cref="ContainerBuilder"/>, because only the host knows its own environment name.
/// Nothing in the suite built the real graph, so a broken seam between the two would have surfaced as a
/// startup failure in a deployed container rather than a red test.
/// </summary>
public sealed class DataLayerRegistrationSqlExecutionServiceTests
{
    private static IContainer BuildContainer()
    {
        var configuration = new ConfigurationBuilder().Build();

        var services = new ServiceCollection();
        services.AddIgnixaDataLayerServices(configuration);

        var builder = new ContainerBuilder();
        builder.Populate(services);
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();
        builder.RegisterInstance(NullLoggerFactory.Instance).As<ILoggerFactory>();
        // The host supplies both in production. RegisterDataLayerServices registers the real
        // AppSettingsTenantConfigurationStore over anything else, and that needs IConfiguration.
        builder.RegisterInstance(configuration).As<IConfiguration>();
        builder.RegisterDataLayerServices(configuration, environmentName: "Production");

        return builder.Build();
    }

    [Fact]
    public void GivenTheProductionRegistrations_WhenResolvingTheSqlExecutionService_ThenItsCredentialGuardIsSatisfiedFromTheContainer()
    {
        using var container = BuildContainer();

        var executionService = container.Resolve<ISqlExecutionService>();

        executionService.ShouldBeOfType<SqlExecutionService>();
    }

    /// <summary>
    /// The dependency the test above exists to protect. Resolving it independently distinguishes "the seam
    /// works" from "the execution service happened to be registered with a different constructor".
    /// </summary>
    [Fact]
    public void GivenTheProductionRegistrations_WhenResolvingTheCredentialGuard_ThenItIsRegistered()
    {
        using var container = BuildContainer();

        container.Resolve<ManagedIdentityConnectionStringValidator>().ShouldNotBeNull();
    }
}
