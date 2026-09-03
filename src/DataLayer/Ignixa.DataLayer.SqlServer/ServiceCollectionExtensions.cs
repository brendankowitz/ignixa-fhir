using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIgnixaSqlServerSchemaDeployment(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<SqlServerOptions>(configuration.GetSection(SqlServerOptions.SectionName));
        services.AddSingleton<ISchemaDeployer, SchemaDeployer>();
        services.AddSingleton<ISchemaVersionResolver, SchemaVersionResolver>();
        // Constructed explicitly rather than by type registration so the credential guard it now depends on
        // is a visible part of this registration. ManagedIdentityConnectionStringValidator is registered by
        // the host (DataLayerRegistration.RegisterRepositoryFactories), because only the host knows its own
        // environment name -- reading ASPNETCORE_ENVIRONMENT off the process is the bug that made the guard
        // a no-op in containers.
        services.AddSingleton<ISqlExecutionService>(serviceProvider => new SqlExecutionService(
            serviceProvider.GetRequiredService<ITenantConfigurationStore>(),
            serviceProvider.GetRequiredService<ManagedIdentityConnectionStringValidator>(),
            serviceProvider.GetRequiredService<ILogger<SqlExecutionService>>()));
        return services;
    }
}
