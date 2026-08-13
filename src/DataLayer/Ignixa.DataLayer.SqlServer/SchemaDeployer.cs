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
    private readonly ISchemaVersionResolver _schemaVersionResolver;
    private readonly ILogger<SchemaDeployer> _logger;

    public SchemaDeployer(
        ITenantConfigurationStore tenantConfigurationStore,
        IHostEnvironment environment,
        IOptions<SqlServerOptions> options,
        ISchemaVersionResolver schemaVersionResolver,
        ILogger<SchemaDeployer> logger)
    {
        ArgumentNullException.ThrowIfNull(tenantConfigurationStore);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(schemaVersionResolver);
        ArgumentNullException.ThrowIfNull(logger);
        _tenantConfigurationStore = tenantConfigurationStore;
        _environment = environment;
        _options = options;
        _schemaVersionResolver = schemaVersionResolver;
        _logger = logger;
    }

    public async Task DeployIfEmptyAsync(int tenantId, CancellationToken cancellationToken)
    {
        var connectionString = await TenantConnectionStringResolver.ResolveAsync(
            _tenantConfigurationStore, tenantId, cancellationToken);

        if (_environment.IsDevelopment() && !await CanConnectAsync(connectionString, cancellationToken))
        {
            await CreateEmptyDatabaseAsync(connectionString, cancellationToken);

            // SQL Server occasionally isn't immediately connectable via a brand-new connection right
            // after CREATE DATABASE commits (observed as a transient "login failed" / error 4060 --
            // the same error code SqlExecutionService.IsTransient already treats as retryable), even
            // though the creating login is db_owner. A short bounded retry avoids a spurious startup
            // failure on a database this method itself just created.
            await WaitUntilConnectableAsync(connectionString, cancellationToken);
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
        await StampSchemaVersionAsync(connectionString, SchemaVersionConstants.CurrentVersion, cancellationToken);
    }

    public async Task UpgradeIfNeededAsync(int tenantId, CancellationToken cancellationToken)
    {
        var connectionString = await TenantConnectionStringResolver.ResolveAsync(
            _tenantConfigurationStore, tenantId, cancellationToken);

        if (await IsDatabaseEmptyAsync(connectionString, cancellationToken))
        {
            // Nothing to upgrade -- an empty database is DeployIfEmptyAsync's job, not this one.
            return;
        }

        var currentVersion = await _schemaVersionResolver.GetCurrentVersionAsync(tenantId, cancellationToken);
        if (currentVersion >= SchemaVersionConstants.CurrentVersion)
        {
            _logger.LogDebug("Tenant {TenantId} is already at schema version {Version}.", tenantId, currentVersion);
            return;
        }

        // Cheap config gate before the expensive DacFx work below. A deployment that has explicitly
        // opted out of automatic schema changes shouldn't pay for full deploy-report generation on
        // every uncached factory creation, nor be taken down by a report-shape problem it has no
        // interest in -- it needs to be told to use the CLI, and nothing more.
        if (!_options.Value.AutomaticSchemaDeploymentEnabled)
        {
            throw new InvalidOperationException(
                $"Tenant {tenantId}'s database is behind schema version {SchemaVersionConstants.CurrentVersion} " +
                $"and {SqlServerOptions.SectionName}:{nameof(SqlServerOptions.AutomaticSchemaDeploymentEnabled)} is false. " +
                "Apply the upgrade manually using the schema-upgrade CLI tool, or enable automatic deployment.");
        }

        using var dacpacStream = typeof(SchemaDeployer).Assembly.GetManifestResourceStream(DacpacResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{DacpacResourceName}' not found in {typeof(SchemaDeployer).Assembly.FullName}.");
        using var package = DacPackage.Load(dacpacStream);
        var databaseName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        var dacServices = new DacServices(connectionString);

        var deployReportXml = dacServices.GenerateDeployReport(package, databaseName, cancellationToken: cancellationToken);

        var classification = DeployReportClassifier.Classify(deployReportXml);
        if (!classification.IsAutoSafe)
        {
            throw new InvalidOperationException(
                $"Tenant {tenantId}'s database is at schema version {currentVersion}, behind the current " +
                $"version {SchemaVersionConstants.CurrentVersion}, and the pending diff is classified as " +
                $"{classification.Outcome} rather than auto-safe ({classification.ReasonSummary}). Review the diff " +
                "and apply it explicitly using the schema-upgrade CLI tool (tools/Ignixa.SchemaUpgrade.Cli).");
        }

        dacServices.Deploy(package, databaseName, upgradeExisting: true, cancellationToken: cancellationToken);
        await StampSchemaVersionAsync(connectionString, SchemaVersionConstants.CurrentVersion, cancellationToken);
        _logger.LogInformation(
            "Upgraded tenant {TenantId}'s database from schema version {OldVersion} to {NewVersion}.",
            tenantId, currentVersion, SchemaVersionConstants.CurrentVersion);
    }

    private static async Task<bool> CanConnectAsync(string connectionString, CancellationToken cancellationToken)
    {
        try
        {
            // Pooling disabled: this is a one-off "does it exist yet" probe, not a connection meant
            // to be reused. Without this, a probe against a not-yet-created database trips
            // Microsoft.Data.SqlClient's connection-pool blocking period for this exact connection
            // string -- poisoning the SAME pool that IsDatabaseEmptyAsync/DacServices.Deploy/the
            // app's own runtime EF Core queries reuse afterward, causing spurious "login failed"
            // errors for several seconds even after the database is confirmed ONLINE and reachable
            // via a fresh, unpooled connection.
            await using var connection = new SqlConnection(BuildNonPooledConnectionString(connectionString));
            await connection.OpenAsync(cancellationToken);
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
    }

    private static string BuildNonPooledConnectionString(string connectionString)
        => new SqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;

    private const int ConnectableRetryAttempts = 10;
    private static readonly TimeSpan ConnectableRetryDelay = TimeSpan.FromMilliseconds(300);

    private static async Task WaitUntilConnectableAsync(string connectionString, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= ConnectableRetryAttempts; attempt++)
        {
            if (await CanConnectAsync(connectionString, cancellationToken))
            {
                return;
            }

            await Task.Delay(ConnectableRetryDelay, cancellationToken);
        }

        // Final attempt: let the real exception surface if the database is still unreachable --
        // don't swallow a genuine failure behind a generic timeout message. Still unpooled, for the
        // same reason CanConnectAsync is.
        await using var connection = new SqlConnection(BuildNonPooledConnectionString(connectionString));
        await connection.OpenAsync(cancellationToken);
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

    /// <summary>
    /// Records that a tenant's database has been stamped at <paramref name="version"/>. Public so
    /// that Ignixa.SchemaUpgrade.Cli -- the operator-run escape hatch for diffs
    /// <see cref="DeployReportClassifier"/> refuses to auto-apply -- can record the same history
    /// this class's own automatic paths do after applying its manual deploy. Without this, a
    /// CLI-applied upgrade would leave <see cref="ISchemaVersionResolver"/> reporting a stale
    /// version until some later, unrelated call happened to no-op-redeploy and self-heal it.
    /// Idempotent and safe under concurrency: dbo.SchemaVersion has PRIMARY KEY (Version), so a
    /// bare INSERT throws (SQL error 2627) whenever an already-stamped version is re-stamped --
    /// which is the norm, not the exception, on the CLI's re-run path. A plain IF NOT EXISTS guard
    /// would fix only the sequential case; two connections can both pass it before either inserts,
    /// so the check takes a range lock (UPDLOCK, HOLDLOCK) inside an explicit transaction to also
    /// cover two app instances cold-starting the same tenant concurrently.
    /// </summary>
    public static async Task StampSchemaVersionAsync(string connectionString, int version, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersion WITH (UPDLOCK, HOLDLOCK) WHERE Version = @version)
                INSERT dbo.SchemaVersion (Version) VALUES (@version);
            COMMIT TRANSACTION;
            """;
        command.Parameters.AddWithValue("@version", version);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
