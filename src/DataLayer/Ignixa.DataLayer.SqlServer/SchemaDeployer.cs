using System.Reflection;
using Ignixa.Domain.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SqlServer.Dac;

namespace Ignixa.DataLayer.SqlServer;

public sealed class SchemaDeployer : ISchemaDeployer
{
    private const string DacpacResourceName = "Ignixa.DataLayer.SqlServer.Schema.dacpac";

    private readonly ITenantConfigurationStore _tenantConfigurationStore;
    private readonly IHostEnvironment _environment;
    private readonly IOptions<SqlServerOptions> _options;
    private readonly ILogger<SchemaDeployer> _logger;

    public SchemaDeployer(
        ITenantConfigurationStore tenantConfigurationStore,
        IHostEnvironment environment,
        IOptions<SqlServerOptions> options,
        ILogger<SchemaDeployer> logger)
    {
        ArgumentNullException.ThrowIfNull(tenantConfigurationStore);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _tenantConfigurationStore = tenantConfigurationStore;
        _environment = environment;
        _options = options;
        _logger = logger;
    }

    public async Task DeployIfEmptyAsync(int tenantId, CancellationToken cancellationToken)
    {
        var connectionString = await SqlExecutionService.ResolveConnectionStringAsync(
            _tenantConfigurationStore, tenantId, cancellationToken);

        if (_environment.IsDevelopment() && !await CanConnectAsync(connectionString, cancellationToken))
        {
            await CreateEmptyDatabaseAsync(connectionString, cancellationToken);
        }

        if (!await IsDatabaseEmptyAsync(connectionString, cancellationToken))
        {
            _logger.LogDebug("Tenant {TenantId}'s database already has schema; skipping deploy.", tenantId);
            return;
        }

        if (!_options.Value.AutomaticSchemaDeploymentEnabled)
        {
            throw new InvalidOperationException(
                $"Tenant {tenantId}'s database is not initialized and " +
                $"{SqlServerOptions.SectionName}:{nameof(SqlServerOptions.AutomaticSchemaDeploymentEnabled)} is false. " +
                "Deploy the schema manually (sqlpackage /Action:Publish against the " +
                "Ignixa.DataLayer.SqlServer.Database dacpac) before starting the app, or enable automatic deployment.");
        }

        using var dacpacStream = typeof(SchemaDeployer).Assembly.GetManifestResourceStream(DacpacResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{DacpacResourceName}' not found in {typeof(SchemaDeployer).Assembly.FullName}.");
        using var package = DacPackage.Load(dacpacStream);

        var databaseName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        var dacServices = new DacServices(connectionString);
        // upgradeExisting must be true here: DacFx's flag only distinguishes "database
        // exists on the server" from "database doesn't exist" -- not "empty" from
        // "non-empty". By this point the target database always already exists (created
        // either by this method's own CreateEmptyDatabaseAsync above in dev mode, or
        // pre-provisioned by ops), so upgradeExisting: false would throw
        // DacServicesException unconditionally, even against an empty target. The actual
        // safety gate is the IsDatabaseEmptyAsync check above, which returns before this
        // line runs if the database already has schema -- matching the old
        // DatabaseInitializer's historical safety model (a single emptiness check
        // immediately before acting, with no deeper DacFx-level backstop).
        dacServices.Deploy(package, databaseName, upgradeExisting: true, cancellationToken: cancellationToken);
        _logger.LogInformation("Deployed schema to tenant {TenantId}'s new database '{DatabaseName}'.", tenantId, databaseName);
    }

    private static async Task<bool> CanConnectAsync(string connectionString, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
    }

    private static async Task CreateEmptyDatabaseAsync(string connectionString, CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var databaseName = builder.InitialCatalog;
        builder.InitialCatalog = "master";

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // CA2100 suppressed: databaseName is the InitialCatalog from the tenant's configured
        // connection string (app config), never user input -- SQL Server does not support
        // parameterizing database names, so interpolation is the only option here. Mirrors the
        // same suppression in the old DatabaseInitializer.CreateEmptyDatabaseAsync.
#pragma warning disable CA2100
        command.CommandText = $"CREATE DATABASE [{databaseName}]";
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> IsDatabaseEmptyAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Resource') THEN 0 ELSE 1 END";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return (int)result! == 1;
    }
}
