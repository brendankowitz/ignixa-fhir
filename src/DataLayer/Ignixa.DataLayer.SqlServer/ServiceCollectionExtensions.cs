using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        return services;
    }
}
