using System.Xml.Linq;
using Ignixa.DataLayer.SqlServer.Tests.Fixtures;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.SqlServer.Dac;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

/// <summary>
/// Real-database coverage for the schema-version-4 pre-deploy step. The tenant under test is "v3-shaped":
/// the current schema deployed fresh, then IX_Resource_ResourceTypeId_ResourceSurrgateId put back to its
/// version-3 definition (no INCLUDE) and the stamp lowered to 3. That is exactly a version-3 database --
/// version 4 changes nothing else a deployed database can observe (Tables/Resource.sql is the only
/// Database-project file it touches, and its other edit, the CH_Resource_RawResource_Length literal, is
/// stored identically either way) -- without a committed binary fixture.
/// </summary>
public class ResourceSurrogateIdIndexOnlineMigrationTests
{
    private const int TenantId = 1;
    private const int CurrentRowCount = 40;
    private const string IndexName = ResourceSurrogateIdIndexOnlineMigration.IndexName;

    [SkippableFact]
    public async Task GivenAV3ShapedTenant_WhenTheIndexIsConvertedBeforeTheDeploy_ThenTheDeployReportNoLongerTouchesDboResourceAndIsAutoSafe()
    {
        await using var tenant = await V3ShapedTenant.CreateAsync();

        // Control: without the pre-step DacFx plans the drop/create this whole change exists to avoid.
        var reportBefore = tenant.GenerateDeployReport();
        ItemValues(reportBefore).ShouldContain($"[dbo].[Resource].[{IndexName}]");

        var plan = await ResourceSurrogateIdIndexOnlineMigration.PlanAsync(tenant.ConnectionString, 3, CancellationToken.None);
        plan.Action.ShouldBe(
            ResourceSurrogateIdIndexMigrationAction.ConvertOnline,
            $"the test server must support online index operations for this test to mean anything ({plan.Reason})");
        await ResourceSurrogateIdIndexOnlineMigration.ApplyAsync(
            tenant.ConnectionString, plan, TenantId, NullLogger.Instance, CancellationToken.None);

        (await tenant.IndexIncludesResourceIdAsync()).ShouldBeTrue();

        // The report is generated under the deployer's own options, exactly as UpgradeIfNeededAsync does,
        // so this is the diff the automatic upgrade would then classify and apply: nothing on dbo.Resource,
        // its indexes or its constraints. A pre-step whose DDL drifted from Tables/Resource.sql in any
        // index option would show up here as the drop/create coming back.
        var reportAfter = tenant.GenerateDeployReport();
        ItemValues(reportAfter).ShouldNotContain(value => value.StartsWith("[dbo].[Resource]", StringComparison.Ordinal));
        ItemValues(reportAfter).ShouldNotContain("[dbo].[CH_Resource_RawResource_Length]");
        DeployReportClassifier.Classify(reportAfter).IsAutoSafe.ShouldBeTrue();
    }

    [SkippableFact]
    public async Task GivenAV3ShapedTenant_WhenUpgradeIfNeededAsyncCalled_ThenTheIndexIsConvertedOnlineBeforeTheDeployAndTheVersionIsStamped()
    {
        await using var tenant = await V3ShapedTenant.CreateAsync();
        var logger = new RecordingLogger<SchemaDeployer>();

        await tenant.CreateDeployer(logger).UpgradeIfNeededAsync(TenantId, CancellationToken.None);

        (await tenant.IndexIncludesResourceIdAsync()).ShouldBeTrue();
        (await tenant.ScalarAsync<int>("SELECT MAX(Version) FROM dbo.SchemaVersion")).ShouldBe(SchemaVersionConstants.CurrentVersion);
        (await tenant.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.Resource WITH (INDEX({IndexName})) WHERE IsHistory = 0 AND IsDeleted = 0"))
            .ShouldBe(CurrentRowCount);

        // The online conversion ran inside the upgrade, and nothing fell back to the offline path. Had it run
        // after the deploy (or not at all) the deploy would already have rebuilt the index offline and the
        // plan would have found nothing to convert, so this message would be missing.
        logger.Messages(LogLevel.Information).ShouldContain(message =>
            message.Contains($"converted {IndexName} on dbo.Resource ONLINE", StringComparison.Ordinal));
        logger.Messages(LogLevel.Warning).ShouldBeEmpty();
    }

    [SkippableFact]
    public async Task GivenAnAlreadyConvertedIndex_WhenTheConversionIsAppliedAgain_ThenItIsANoOp()
    {
        await using var tenant = await V3ShapedTenant.CreateAsync();
        var plan = await ResourceSurrogateIdIndexOnlineMigration.PlanAsync(tenant.ConnectionString, 3, CancellationToken.None);
        plan.Action.ShouldBe(ResourceSurrogateIdIndexMigrationAction.ConvertOnline, plan.Reason);
        await ResourceSurrogateIdIndexOnlineMigration.ApplyAsync(
            tenant.ConnectionString, plan, TenantId, NullLogger.Instance, CancellationToken.None);
        var partitionsAfterFirstRun = await tenant.IndexPartitionIdsAsync();

        // A re-plan sees nothing left to do ...
        (await ResourceSurrogateIdIndexOnlineMigration.PlanAsync(tenant.ConnectionString, 3, CancellationToken.None))
            .Action.ShouldBe(ResourceSurrogateIdIndexMigrationAction.None);

        // ... and even a stale ConvertOnline plan (another instance's, or one made before a failed deploy)
        // does not rebuild: a rebuild allocates new partitions, so unchanged partition ids prove none ran.
        await ResourceSurrogateIdIndexOnlineMigration.ApplyAsync(
            tenant.ConnectionString, plan, TenantId, NullLogger.Instance, CancellationToken.None);

        (await tenant.IndexPartitionIdsAsync()).ShouldBe(partitionsAfterFirstRun);
        (await tenant.IndexIncludesResourceIdAsync()).ShouldBeTrue();
    }

    [SkippableFact]
    public async Task GivenTwoInstancesUpgradingTheSameTenant_WhenBothConvertConcurrently_ThenTheSecondWaitsForTheFirstInsteadOfFailingWith1912()
    {
        await using var tenant = await V3ShapedTenant.CreateAsync();
        var plan = await ResourceSurrogateIdIndexOnlineMigration.PlanAsync(tenant.ConnectionString, 3, CancellationToken.None);
        plan.Action.ShouldBe(ResourceSurrogateIdIndexMigrationAction.ConvertOnline, plan.Reason);

        // On a 40-row table the build finishes in milliseconds, so the overlap is forced: a third session holds
        // an intent-shared lock on dbo.Resource, which an online build only conflicts with at its final Sch-M.
        // The first build therefore runs and then waits there, still in progress -- which is exactly when a
        // second online build of the same index fails at once with error 1912 unless it is serialised.
        await using var blocker = new SqlConnection(tenant.ConnectionString);
        await blocker.OpenAsync();
        await using var blockingTransaction = (SqlTransaction)await blocker.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        await using (var hold = new SqlCommand("SELECT TOP (1) ResourceSurrogateId FROM dbo.Resource WITH (ROWLOCK)", blocker, blockingTransaction))
        {
            await hold.ExecuteScalarAsync();
        }

        var firstLogger = new RecordingLogger<SchemaDeployer>();
        var secondLogger = new RecordingLogger<SchemaDeployer>();
        var first = Task.Run(() => ResourceSurrogateIdIndexOnlineMigration.ApplyAsync(
            tenant.ConnectionString, plan, TenantId, firstLogger, CancellationToken.None));
        await tenant.WaitUntilAsync(
            "the first build to be in progress, blocked on a lock on dbo.Resource",
            () => first.IsCompleted,
            "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE database_id = DB_ID() AND command = 'CREATE INDEX' AND wait_type LIKE 'LCK[_]M[_]%'");
        first.IsCompleted.ShouldBeFalse("the blocker must hold the first build in progress, or the test proves nothing");

        var second = Task.Run(() => ResourceSurrogateIdIndexOnlineMigration.ApplyAsync(
            tenant.ConnectionString, plan, TenantId, secondLogger, CancellationToken.None));
        await tenant.WaitUntilAsync(
            "the second instance to wait for the upgrade lock (or, unserialised, to fail)",
            () => second.IsCompleted,
            "SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_database_id = DB_ID() AND resource_type = 'APPLICATION' AND request_status = 'WAIT'");

        await blockingTransaction.CommitAsync();
        await Task.WhenAll(first, second);

        (await tenant.IndexIncludesResourceIdAsync()).ShouldBeTrue();
        firstLogger.Messages(LogLevel.Information).ShouldContain(message =>
            message.Contains($"converted {IndexName} on dbo.Resource ONLINE in", StringComparison.Ordinal));
        secondLogger.Messages(LogLevel.Information).ShouldContain(message =>
            message.Contains("was already converted", StringComparison.Ordinal));
        secondLogger.Messages(LogLevel.Information).ShouldContain(message =>
            message.Contains("for another instance's upgrade lock", StringComparison.Ordinal));
    }

    [SkippableFact]
    public async Task GivenAPopulatedResourceTableWithoutTheIndex_WhenPlannedAndApplied_ThenTheIndexIsCreatedOnlineAndTheDeployLeavesDboResourceAlone()
    {
        await using var tenant = await V3ShapedTenant.CreateAsync();
        await tenant.ExecuteAsync($"DROP INDEX {IndexName} ON dbo.Resource");

        var plan = await ResourceSurrogateIdIndexOnlineMigration.PlanAsync(tenant.ConnectionString, 3, CancellationToken.None);
        plan.Action.ShouldBe(ResourceSurrogateIdIndexMigrationAction.CreateOnline, plan.Reason);
        var logger = new RecordingLogger<SchemaDeployer>();
        await ResourceSurrogateIdIndexOnlineMigration.ApplyAsync(tenant.ConnectionString, plan, TenantId, logger, CancellationToken.None);

        (await tenant.IndexIncludesResourceIdAsync()).ShouldBeTrue();
        (await tenant.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.Resource WITH (INDEX({IndexName})) WHERE IsHistory = 0 AND IsDeleted = 0"))
            .ShouldBe(CurrentRowCount);
        logger.Messages(LogLevel.Information).ShouldContain(message =>
            message.Contains($"created {IndexName} on dbo.Resource ONLINE in", StringComparison.Ordinal));
        var reportAfter = tenant.GenerateDeployReport();
        ItemValues(reportAfter).ShouldNotContain(value => value.StartsWith("[dbo].[Resource]", StringComparison.Ordinal));
        DeployReportClassifier.Classify(reportAfter).IsAutoSafe.ShouldBeTrue();

        // A stale CreateOnline plan finds the index in place and does nothing.
        var partitions = await tenant.IndexPartitionIdsAsync();
        var staleLogger = new RecordingLogger<SchemaDeployer>();
        await ResourceSurrogateIdIndexOnlineMigration.ApplyAsync(tenant.ConnectionString, plan, TenantId, staleLogger, CancellationToken.None);
        (await tenant.IndexPartitionIdsAsync()).ShouldBe(partitions);
        staleLogger.Messages(LogLevel.Information).ShouldContain(message =>
            message.Contains("was already created", StringComparison.Ordinal));
    }

    private static List<string> ItemValues(string deployReportXml)
    {
        XNamespace ns = "http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02";
        return XDocument.Parse(deployReportXml).Descendants(ns + "Item")
            .Select(item => item.Attribute("Value")?.Value ?? string.Empty)
            .ToList();
    }

    private sealed class V3ShapedTenant : IAsyncDisposable
    {
        private readonly string _databaseName;

        private V3ShapedTenant(string databaseName, string connectionString)
        {
            _databaseName = databaseName;
            ConnectionString = connectionString;
        }

        public string ConnectionString { get; }

        public static async Task<V3ShapedTenant> CreateAsync()
        {
            var configured = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING");
            if (string.IsNullOrEmpty(configured))
            {
                throw new SkipException(
                    "TEST_SQL_CONNECTION_STRING is not set (see docker-compose.test.yml) -- skipping, not failing.");
            }

            var databaseName = $"ResourceIndexOnlineMigration_{Guid.NewGuid():N}";
            var tenant = new V3ShapedTenant(
                databaseName,
                new SqlConnectionStringBuilder(configured) { InitialCatalog = databaseName }.ConnectionString);
            try
            {
                await tenant.ExecuteOnMasterAsync($"CREATE DATABASE [{databaseName}]");
                await tenant.CreateDeployer(NullLogger<SchemaDeployer>.Instance).DeployIfEmptyAsync(TenantId, CancellationToken.None);
                await tenant.ExecuteAsync($"""
                    INSERT dbo.ResourceType (Name) VALUES ('Patient');
                    DECLARE @type smallint = SCOPE_IDENTITY();
                    INSERT dbo.Resource (ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RawResource)
                    SELECT @type, CONCAT('p', n), 1, 0, n, 0, 0x01
                    FROM (SELECT TOP ({CurrentRowCount}) ROW_NUMBER() OVER (ORDER BY (SELECT 1)) AS n FROM sys.all_objects) AS numbers;
                    -- History rows sit outside the filtered index; they make its filter observable.
                    INSERT dbo.Resource (ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RawResource)
                    SELECT @type, CONCAT('p', n), 0, 1, 100000 + n, 0, 0x01
                    FROM (SELECT TOP (5) ROW_NUMBER() OVER (ORDER BY (SELECT 1)) AS n FROM sys.all_objects) AS numbers;

                    -- The version-3 definition of the index (origin/main before schema version 4).
                    CREATE UNIQUE NONCLUSTERED INDEX {IndexName}
                        ON dbo.Resource (ResourceTypeId, ResourceSurrogateId)
                        WHERE IsHistory = 0 AND IsDeleted = 0
                        WITH (DROP_EXISTING = ON)
                        ON PartitionScheme_ResourceTypeId (ResourceTypeId);
                    UPDATE dbo.SchemaVersion SET Version = 3;
                    """);
                (await tenant.IndexIncludesResourceIdAsync()).ShouldBeFalse();
                return tenant;
            }
            catch
            {
                await tenant.DisposeAsync();
                throw;
            }
        }

        public SchemaDeployer CreateDeployer(ILogger<SchemaDeployer> logger)
        {
            var store = new SingleTenantStore(ConnectionString);
            return new SchemaDeployer(
                store,
                new ProductionHostEnvironment(),
                Options.Create(new SqlServerOptions { AutomaticSchemaDeploymentEnabled = true, AllowIncompatiblePlatform = true }),
                new SchemaVersionResolver(store, NullLogger<SchemaVersionResolver>.Instance),
                logger);
        }

        public string GenerateDeployReport()
        {
            using var dacpacStream = typeof(SchemaDeployer).Assembly.GetManifestResourceStream("Ignixa.DataLayer.SqlServer.Schema.dacpac")
                ?? throw new InvalidOperationException("Embedded schema dacpac not found.");
            using var package = DacPackage.Load(dacpacStream);
            return new DacServices(ConnectionString).GenerateDeployReport(
                package, _databaseName, CreateDeployer(NullLogger<SchemaDeployer>.Instance).CreateDeployOptions());
        }

        public Task<bool> IndexIncludesResourceIdAsync()
            => ScalarAsync<bool>($"""
                SELECT CONVERT(bit, CASE WHEN EXISTS (
                    SELECT 1
                    FROM sys.indexes i
                    JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                    JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                    WHERE i.object_id = OBJECT_ID(N'dbo.Resource') AND i.name = N'{IndexName}'
                      AND c.name = N'ResourceId' AND ic.is_included_column = 1) THEN 1 ELSE 0 END)
                """);

        public async Task<List<long>> IndexPartitionIdsAsync()
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT p.partition_id
                FROM sys.partitions p
                JOIN sys.indexes i ON i.object_id = p.object_id AND i.index_id = p.index_id
                WHERE i.object_id = OBJECT_ID(N'dbo.Resource') AND i.name = N'{IndexName}'
                ORDER BY p.partition_number
                """;
            await using var reader = await command.ExecuteReaderAsync();
            var ids = new List<long>();
            while (await reader.ReadAsync())
            {
                ids.Add(reader.GetInt64(0));
            }

            ids.ShouldNotBeEmpty();
            return ids;
        }

        public async Task<T> ScalarAsync<T>(string sql)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
#pragma warning disable CA2100 // Test-only SQL built from constants.
            await using var command = new SqlCommand(sql, connection);
#pragma warning restore CA2100
            return (T)(await command.ExecuteScalarAsync())!;
        }

        /// <summary>
        /// Polls until <paramref name="countQuery"/> returns a positive count or <paramref name="done"/> holds,
        /// and fails, naming <paramref name="what"/>, after 60 seconds rather than hanging the run.
        /// </summary>
        public async Task WaitUntilAsync(string what, Func<bool> done, string countQuery)
        {
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (!done() && await ScalarAsync<int>(countQuery) == 0)
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException($"Timed out waiting for {what}.");
                }

                await Task.Delay(20);
            }
        }

        public async ValueTask DisposeAsync()
            => await ExecuteOnMasterAsync($"""
                IF DB_ID('{_databaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{_databaseName}];
                END
                """);

        public Task ExecuteAsync(string sql) => ExecuteAsync(ConnectionString, sql);

        private Task ExecuteOnMasterAsync(string sql)
            => ExecuteAsync(new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" }.ConnectionString, sql);

        private static async Task ExecuteAsync(string connectionString, string sql)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
#pragma warning disable CA2100 // Test-only SQL built from constants and a generated database name.
            await using var command = new SqlCommand(sql, connection);
#pragma warning restore CA2100
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class SingleTenantStore(string connectionString) : ITenantConfigurationStore
    {
        public TenantMode Mode => TenantMode.Isolated;

        public ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(int tenantId, CancellationToken cancellationToken = default)
            => new(tenantId == TenantId
                ? new TenantConfiguration
                {
                    TenantId = tenantId,
                    DisplayName = "Index migration",
                    FhirVersion = "4.0",
                    Storage = new TenantStorageConfiguration { Type = "SqlServer", ConnectionString = connectionString },
                }
                : null);

        public ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(CancellationToken cancellationToken = default)
            => new(Array.Empty<TenantConfiguration>());

        public ValueTask<TenantConfiguration?> ResolveByHostAsync(string host, CancellationToken cancellationToken = default)
            => new((TenantConfiguration?)null);
    }

    private sealed class ProductionHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Ignixa.DataLayer.SqlServer.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
