using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.SqlServer.Dac;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class SchemaDeployerUpgradeTests
{
    private sealed class SingleTenantStore : ITenantConfigurationStore
    {
        private readonly TenantConfiguration _tenant;

        public SingleTenantStore(string connectionString)
        {
            _tenant = new TenantConfiguration
            {
                TenantId = 1,
                DisplayName = "Test Tenant",
                FhirVersion = "4.0",
                Storage = new TenantStorageConfiguration { Type = "SqlServer", ConnectionString = connectionString },
            };
        }

        public TenantMode Mode => TenantMode.Isolated;

        public ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(int tenantId, CancellationToken ct = default)
            => new(tenantId == 1 ? _tenant : null);

        public ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(CancellationToken ct = default)
            => new((IReadOnlyList<TenantConfiguration>)new List<TenantConfiguration> { _tenant });

        public ValueTask<TenantConfiguration?> ResolveByHostAsync(string host, CancellationToken cancellationToken = default)
            => new((TenantConfiguration?)null);
    }

    // IHostEnvironment.EnvironmentName is settable but the concrete HostingEnvironment
    // implementation lives in the Microsoft.Extensions.Hosting package (not .Abstractions), in the
    // Microsoft.Extensions.Hosting.Internal namespace, and is documented as "not intended to be used
    // directly from your code". A minimal local fake avoids pulling in that extra package.
    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Ignixa.DataLayer.SqlServer.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static string GetBaseConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING");
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new SkipException(
                "TEST_SQL_CONNECTION_STRING is not set (see docker-compose.test.yml) -- skipping, not failing.");
        }

        return connectionString;
    }

    private static string BuildConnectionStringForDatabase(string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(GetBaseConnectionString())
        {
            InitialCatalog = databaseName,
        };
        return builder.ConnectionString;
    }

    private static async Task CreateEmptyDatabaseAsync(string databaseName, CancellationToken cancellationToken)
    {
        var masterConnectionString = BuildConnectionStringForDatabase("master");
        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100
        command.CommandText = $"CREATE DATABASE [{databaseName}]";
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DropDatabaseAsync(string databaseName, CancellationToken cancellationToken)
    {
        var masterConnectionString = BuildConnectionStringForDatabase("master");
        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100
        command.CommandText = $"""
            IF DB_ID('{databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{databaseName}];
            END
            """;
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<List<string>> GetTableNamesAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sys.tables";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var tableNames = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            tableNames.Add(reader.GetString(0));
        }

        return tableNames;
    }

    private static async Task<int> GetSchemaVersionRowCountAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM dbo.SchemaVersion";
        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static SchemaDeployer CreateDeployer(string connectionString, bool automaticSchemaDeploymentEnabled = true)
        => new(
            new SingleTenantStore(connectionString),
            new FakeHostEnvironment { EnvironmentName = "Production" },
            Options.Create(new SqlServerOptions { AutomaticSchemaDeploymentEnabled = automaticSchemaDeploymentEnabled }),
            new SchemaVersionResolver(new SingleTenantStore(connectionString), NullLogger<SchemaVersionResolver>.Instance),
            NullLogger<SchemaDeployer>.Instance);

    [SkippableFact]
    public async Task GivenATenantAlreadyAtCurrentVersion_WhenUpgradeIfNeededAsyncCalled_ThenDoesNothing()
    {
        // Arrange -- deploy fresh via DeployIfEmptyAsync (stamps CurrentVersion per Task 1).
        var databaseName = $"SchemaDeployerUpgradeTest_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionStringForDatabase(databaseName);
        await CreateEmptyDatabaseAsync(databaseName, CancellationToken.None);

        try
        {
            var deployer = CreateDeployer(connectionString);
            await deployer.DeployIfEmptyAsync(1, CancellationToken.None);

            var tableNamesBefore = await GetTableNamesAsync(connectionString, CancellationToken.None);
            var schemaVersionRowCountBefore = await GetSchemaVersionRowCountAsync(connectionString, CancellationToken.None);
            schemaVersionRowCountBefore.ShouldBe(1);

            // Act -- calling UpgradeIfNeededAsync against an already-current tenant must be a
            // true no-op: it returns without throwing and without touching sys.tables or
            // SchemaVersion.
            await deployer.UpgradeIfNeededAsync(1, CancellationToken.None);

            // Assert
            var tableNamesAfter = await GetTableNamesAsync(connectionString, CancellationToken.None);
            tableNamesAfter.ShouldBe(tableNamesBefore, ignoreOrder: true);

            var schemaVersionRowCountAfter = await GetSchemaVersionRowCountAsync(connectionString, CancellationToken.None);
            schemaVersionRowCountAfter.ShouldBe(schemaVersionRowCountBefore);
        }
        finally
        {
            await DropDatabaseAsync(databaseName, CancellationToken.None);
        }
    }

    // A Phase-B-era build of this project's .sqlproj, committed as a binary fixture so the test is
    // runnable without git archaeology or a scratch worktree still being present. It is structurally
    // missing the terminology tables (TermCodeSystem/TermConcept/etc) and the SchemaVersion table
    // itself -- a real schema gap, not a synthetic fixture, used to prove the upgrade *mechanism*
    // works, not a genuine version-1-to-version-2 transition (none exists yet).
    //
    // To regenerate: delete the terminology .sql files and Tables/SchemaVersion.sql from a scratch
    // copy of Ignixa.DataLayer.SqlServer.Database, build it --configuration Release, and copy the
    // resulting .dacpac here. (Deliberately described by content rather than by a commit hash: this
    // branch has been rebased, so any hash cited here would not survive.)
    private const string OldDacpacFixtureFileName = "phase-b-pre-task9-schema.dacpac";

    [SkippableFact]
    public async Task GivenATenantOnAnOlderRealSchema_WhenUpgradeIfNeededAsyncCalled_ThenUpgradesToCurrentAndStampsTheVersion()
    {
        // Arrange -- a real, empty, freshly-created database.
        var databaseName = $"SchemaDeployerUpgradeTest_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionStringForDatabase(databaseName);
        await CreateEmptyDatabaseAsync(databaseName, CancellationToken.None);

        try
        {
            // Deploy the OLD dacpac directly via DacServices -- SchemaDeployer only ever knows
            // about the CURRENT embedded dacpac, so an older schema has to be put in place out of
            // band, exactly like a real pre-Phase-C tenant's database would already look before
            // this app version ever touches it.
            var oldDacpacPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", OldDacpacFixtureFileName);
            using (var oldDacpacStream = File.OpenRead(oldDacpacPath))
            using (var oldPackage = DacPackage.Load(oldDacpacStream))
            {
                var oldDacServices = new DacServices(connectionString);
                oldDacServices.Deploy(oldPackage, databaseName, upgradeExisting: true, cancellationToken: CancellationToken.None);
            }

            var tableNamesAfterOldDeploy = await GetTableNamesAsync(connectionString, CancellationToken.None);
            tableNamesAfterOldDeploy.ShouldContain("Resource");
            tableNamesAfterOldDeploy.ShouldNotContain("TermCodeSystem");
            tableNamesAfterOldDeploy.ShouldNotContain("SchemaVersion");

            // Confirm SchemaVersionResolver correctly reports version 0 for this un-versioned
            // pre-Phase-C schema (no SchemaVersion table exists at all yet).
            var resolver = new SchemaVersionResolver(new SingleTenantStore(connectionString), NullLogger<SchemaVersionResolver>.Instance);
            var versionBeforeUpgrade = await resolver.GetCurrentVersionAsync(1, CancellationToken.None);
            versionBeforeUpgrade.ShouldBe(0);

            var deployer = CreateDeployer(connectionString);

            // Act -- the pending diff is pure net-new tables/columns (TermCodeSystem etc.), no
            // drops, so it must classify as auto-safe and apply without throwing.
            await deployer.UpgradeIfNeededAsync(1, CancellationToken.None);

            // Assert
            var tableNamesAfterUpgrade = await GetTableNamesAsync(connectionString, CancellationToken.None);
            tableNamesAfterUpgrade.ShouldContain("TermCodeSystem");
            tableNamesAfterUpgrade.ShouldContain("SchemaVersion");

            var schemaVersionRowCount = await GetSchemaVersionRowCountAsync(connectionString, CancellationToken.None);
            schemaVersionRowCount.ShouldBe(1);

            var versionAfterUpgrade = await resolver.GetCurrentVersionAsync(1, CancellationToken.None);
            versionAfterUpgrade.ShouldBe(SchemaVersionConstants.CurrentVersion);
        }
        finally
        {
            await DropDatabaseAsync(databaseName, CancellationToken.None);
        }
    }

    [SkippableFact]
    public async Task GivenATenantOnAnOlderRealSchemaAndAutomaticDeploymentDisabled_WhenUpgradeIfNeededAsyncCalled_ThenThrowsAndDoesNotModifySchema()
    {
        // Arrange -- a real, empty, freshly-created database on the OLD (pre-Phase-C, version 0)
        // schema, exactly like GivenATenantOnAnOlderRealSchema_.../ThenUpgradesToCurrentAndStampsTheVersion,
        // but with automatic deployment disabled. Bootstrapping the SchemaVersion table is not exempt
        // from AutomaticSchemaDeploymentEnabled -- an operator who opted out must be told to use the
        // CLI even for a tenant that predates schema versioning.
        var databaseName = $"SchemaDeployerUpgradeTest_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionStringForDatabase(databaseName);
        await CreateEmptyDatabaseAsync(databaseName, CancellationToken.None);

        try
        {
            var oldDacpacPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", OldDacpacFixtureFileName);
            using (var oldDacpacStream = File.OpenRead(oldDacpacPath))
            using (var oldPackage = DacPackage.Load(oldDacpacStream))
            {
                var oldDacServices = new DacServices(connectionString);
                oldDacServices.Deploy(oldPackage, databaseName, upgradeExisting: true, cancellationToken: CancellationToken.None);
            }

            var deployer = CreateDeployer(connectionString, automaticSchemaDeploymentEnabled: false);

            // Act / Assert
            await Should.ThrowAsync<InvalidOperationException>(
                () => deployer.UpgradeIfNeededAsync(1, CancellationToken.None));

            var tableNamesAfterAttempt = await GetTableNamesAsync(connectionString, CancellationToken.None);
            tableNamesAfterAttempt.ShouldNotContain("SchemaVersion");
            tableNamesAfterAttempt.ShouldNotContain("TermCodeSystem");
        }
        finally
        {
            await DropDatabaseAsync(databaseName, CancellationToken.None);
        }
    }

    [SkippableFact]
    public async Task GivenATenantWithAGenuinelyDestructiveDiffPending_WhenUpgradeIfNeededAsyncCalled_ThenThrowsAndDoesNotModifySchema()
    {
        // Arrange -- a real, empty, freshly-created database, deployed to the current dacpac's
        // real schema via DeployIfEmptyAsync (stamps SchemaVersion at CurrentVersion).
        var databaseName = $"SchemaDeployerUpgradeTest_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionStringForDatabase(databaseName);
        await CreateEmptyDatabaseAsync(databaseName, CancellationToken.None);

        try
        {
            var deployer = CreateDeployer(connectionString);
            await deployer.DeployIfEmptyAsync(1, CancellationToken.None);

            // Diverge the live database from what the (current, embedded) dacpac declares: add a
            // column the dacpac's model does not know about. Comparing the embedded dacpac against
            // this database makes DacFx propose dropping ExtraTestColumn to reconcile the live
            // database back to the dacpac's declared shape -- a genuine, unambiguous destructive
            // operation, with no second dacpac needed anywhere.
            await using (var alterConnection = new SqlConnection(connectionString))
            {
                await alterConnection.OpenAsync(CancellationToken.None);
                await using var alterCommand = alterConnection.CreateCommand();
                alterCommand.CommandText = "ALTER TABLE dbo.BackgroundJobs ADD ExtraTestColumn INT NULL";
                await alterCommand.ExecuteNonQueryAsync(CancellationToken.None);
            }

            // Manually roll SchemaVersion back below CurrentVersion so UpgradeIfNeededAsync
            // actually attempts the diff instead of no-op'ing on an already-current tenant.
            await using (var stampConnection = new SqlConnection(connectionString))
            {
                await stampConnection.OpenAsync(CancellationToken.None);
                await using var updateCommand = stampConnection.CreateCommand();
                updateCommand.CommandText = "UPDATE dbo.SchemaVersion SET Version = 0";
                await updateCommand.ExecuteNonQueryAsync(CancellationToken.None);
            }

            // Act & Assert -- throws, mentioning the schema-upgrade CLI tool.
            var ex = await Should.ThrowAsync<InvalidOperationException>(
                () => deployer.UpgradeIfNeededAsync(1, CancellationToken.None));

            ex.Message.ShouldContain("tools/Ignixa.SchemaUpgrade.Cli");

            // Confirm the refused diff was never actually applied -- ExtraTestColumn still exists,
            // proving the method threw before calling DacServices.Deploy, not after a partial apply.
            await using var verifyConnection = new SqlConnection(connectionString);
            await verifyConnection.OpenAsync(CancellationToken.None);
            await using var verifyCommand = verifyConnection.CreateCommand();
            verifyCommand.CommandText = """
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID('dbo.BackgroundJobs') AND name = 'ExtraTestColumn'
                """;
            var columnStillExists = await verifyCommand.ExecuteScalarAsync(CancellationToken.None);
            columnStillExists.ShouldNotBeNull();
        }
        finally
        {
            await DropDatabaseAsync(databaseName, CancellationToken.None);
        }
    }
}
