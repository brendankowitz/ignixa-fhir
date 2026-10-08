using System.Text.Json;
using Ignixa.DataLayer.SqlServer;
using Ignixa.SchemaUpgrade.Cli;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;
using Shouldly;

namespace Ignixa.SchemaUpgrade.Cli.Tests;

// Proves Finding 1 is fixed: without --allow-data-loss, dacServices.Deploy(...) had no
// DacDeployOptions at all, so DacDeployOptions.BlockOnPossibleDataLoss defaulted to true (DacFx's
// own default) and DacFx would terminate a genuinely data-lossy deploy -- the CLI's entire reason
// to exist -- even after the operator confirmed via [y/N]/--confirm. Follows
// SchemaDeployerUpgradeTests.cs's real-database method: deploy the current dacpac fresh, diverge
// the live database with an undeclared column via raw SQL, then insert a row so the pending
// column-drop is genuinely data-lossy (not just schema-lossy against an empty table).
public class RunAsyncDataLossTests
{
    private static readonly string[] LegacyReindexObjectNames =
    [
        "AcquireReindexJobs",
        "CheckActiveReindexJobs",
        "CreateReindexJob",
        "GetReindexJobById",
        "ReindexJob",
        "UpdateReindexJob",
    ];

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

    private static async Task<bool> ColumnExistsAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1 FROM sys.columns
            WHERE object_id = OBJECT_ID('dbo.BackgroundJobs') AND name = 'ExtraTestColumn'
            """;
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<List<string>> GetLegacyReindexObjectNamesAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name
            FROM sys.objects
            WHERE schema_id = SCHEMA_ID('dbo')
              AND name IN
                  ('AcquireReindexJobs', 'CheckActiveReindexJobs', 'CreateReindexJob',
                   'GetReindexJobById', 'ReindexJob', 'UpdateReindexJob')
            ORDER BY name
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var names = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static async Task WriteAppSettingsAsync(string configPath, string connectionString)
    {
        await File.WriteAllTextAsync(configPath, $$"""
            {
              "Tenants": {
                "Mode": "Isolated",
                "Configurations": [
                  {
                    "TenantId": 1,
                    "DisplayName": "Test Tenant",
                    "FhirVersion": "4.0",
                    "Storage": {
                      "Type": "SqlServer",
                      "ConnectionString": {{JsonSerializer.Serialize(connectionString)}}
                    }
                  }
                ]
              }
            }
            """);
    }

    [SkippableFact]
    public async Task GivenAGenuinelyDestructiveDiffWithARowPresent_WhenRunAsyncCalled_ThenAllowDataLossIsRequiredToApplyAndDropTheColumn()
    {
        var databaseName = $"SchemaUpgradeCliDataLossTest_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionStringForDatabase(databaseName);
        await CreateEmptyDatabaseAsync(databaseName, CancellationToken.None);

        var tempDir = Directory.CreateTempSubdirectory("schema-upgrade-cli-data-loss-test-");
        try
        {
            // Deploy the current, embedded dacpac fresh -- a real, current-schema database to
            // diverge from (mirrors SchemaDeployerUpgradeTests.cs's third test, minus the
            // tenant-store/version-stamping machinery this test doesn't need).
            using (var dacpacStream = typeof(SchemaDeployer).Assembly.GetManifestResourceStream("Ignixa.DataLayer.SqlServer.Schema.dacpac")
                ?? throw new InvalidOperationException("Embedded schema dacpac not found."))
            using (var package = DacPackage.Load(dacpacStream))
            {
                var dacServices = new DacServices(connectionString);
                // The schema targets Azure SQL Database; this baseline deploy goes to the box SQL
                // Server container the CLI tests run against, which is the incompatible side.
                dacServices.Deploy(
                    package,
                    databaseName,
                    upgradeExisting: true,
                    options: new DacDeployOptions { AllowIncompatiblePlatform = true },
                    cancellationToken: CancellationToken.None);
            }

            // Diverge: add a column the dacpac's model does not know about, comparing the embedded
            // dacpac against this database makes DacFx propose dropping ExtraTestColumn to reconcile
            // the live database back to the dacpac's declared shape.
            await using (var alterConnection = new SqlConnection(connectionString))
            {
                await alterConnection.OpenAsync(CancellationToken.None);
                await using var alterCommand = alterConnection.CreateCommand();
                alterCommand.CommandText = "ALTER TABLE dbo.BackgroundJobs ADD ExtraTestColumn INT NULL";
                await alterCommand.ExecuteNonQueryAsync(CancellationToken.None);
            }

            // Insert a row with a value in the divergent column, so the pending drop is genuinely
            // data-lossy -- not just schema-lossy against an empty table.
            await using (var insertConnection = new SqlConnection(connectionString))
            {
                await insertConnection.OpenAsync(CancellationToken.None);
                await using var insertCommand = insertConnection.CreateCommand();
                insertCommand.CommandText = """
                    INSERT dbo.BackgroundJobs
                        (TenantId, JobId, JobType, Status, Definition, CreateDate, HeartbeatDate, CancelRequested, ExtraTestColumn)
                    VALUES
                        (1, @jobId, 1, 'Pending', '{}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 0, 42)
                    """;
                insertCommand.Parameters.AddWithValue("@jobId", Guid.NewGuid().ToString());
                await insertCommand.ExecuteNonQueryAsync(CancellationToken.None);
            }

            var configPath = Path.Combine(tempDir.FullName, "appsettings.json");
            await WriteAppSettingsAsync(configPath, connectionString);

            // Act & Assert -- WITHOUT --allow-data-loss, DacFx blocks the deploy: the column, and the
            // row's value in it, must still exist afterward.
            using (var input = new StringReader(string.Empty))
            using (var output = new StringWriter())
            {
                var blockedOptions = new CliUpgradeOptions(TenantId: 1, AutoConfirm: true, AllowDataLoss: false, AllowIncompatiblePlatform: true, ConfigPath: configPath);
                await Should.ThrowAsync<Exception>(() =>
                    Program.RunAsync(blockedOptions, input, output, CancellationToken.None));
            }

            (await ColumnExistsAsync(connectionString, CancellationToken.None)).ShouldBeTrue();

            // Act -- WITH --allow-data-loss, the identical deploy succeeds and the column is
            // actually dropped -- proving the flag is load-bearing, not just present.
            int exitCode;
            using (var input = new StringReader(string.Empty))
            using (var output = new StringWriter())
            {
                var allowedOptions = new CliUpgradeOptions(TenantId: 1, AutoConfirm: true, AllowDataLoss: true, AllowIncompatiblePlatform: true, ConfigPath: configPath);
                exitCode = await Program.RunAsync(allowedOptions, input, output, CancellationToken.None);
            }

            exitCode.ShouldBe(0);
            (await ColumnExistsAsync(connectionString, CancellationToken.None)).ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(tempDir.FullName, recursive: true);
            await DropDatabaseAsync(databaseName, CancellationToken.None);
        }
    }

    [SkippableFact]
    public async Task GivenAVersionFiveTenantWithLegacyReindexObjects_WhenSchemaUpgradeCliAllowsDataLoss_ThenItRemovesThoseObjects()
    {
        var databaseName = $"SchemaUpgradeCliReindexRetirementTest_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionStringForDatabase(databaseName);
        var configDirectory = Path.Combine(AppContext.BaseDirectory, $"schema-upgrade-cli-reindex-retirement-{Guid.NewGuid():N}");
        await CreateEmptyDatabaseAsync(databaseName, CancellationToken.None);
        Directory.CreateDirectory(configDirectory);

        try
        {
            var legacyDacpacPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "schema-v5-before-reindex-retirement.dacpac");
            using (var legacyDacpacStream = File.OpenRead(legacyDacpacPath))
            using (var legacyPackage = DacPackage.Load(legacyDacpacStream))
            {
                var legacyDacServices = new DacServices(connectionString);
                legacyDacServices.Deploy(
                    legacyPackage,
                    databaseName,
                    upgradeExisting: true,
                    options: new DacDeployOptions { AllowIncompatiblePlatform = true },
                    cancellationToken: CancellationToken.None);
            }

            await SchemaDeployer.StampSchemaVersionAsync(connectionString, 5, CancellationToken.None);
            (await GetLegacyReindexObjectNamesAsync(connectionString, CancellationToken.None))
                .ShouldBe(LegacyReindexObjectNames, ignoreOrder: true);

            await using (var legacyConnection = new SqlConnection(connectionString))
            {
                await legacyConnection.OpenAsync(CancellationToken.None);
                await using var insertLegacyJob = legacyConnection.CreateCommand();
                insertLegacyJob.CommandText = """
                    INSERT dbo.ReindexJob (Id, Status, RawJobRecord)
                    VALUES ('obsolete-reindex-job', 'Running', '{}')
                    """;
                await insertLegacyJob.ExecuteNonQueryAsync(CancellationToken.None);
            }

            var configPath = Path.Combine(configDirectory, "appsettings.json");
            await WriteAppSettingsAsync(configPath, connectionString);

            using (var input = new StringReader(string.Empty))
            using (var output = new StringWriter())
            {
                var blockedOptions = new CliUpgradeOptions(TenantId: 1, AutoConfirm: true, AllowDataLoss: false, AllowIncompatiblePlatform: true, ConfigPath: configPath);
                await Should.ThrowAsync<Exception>(() =>
                    Program.RunAsync(blockedOptions, input, output, CancellationToken.None));
            }

            (await GetLegacyReindexObjectNamesAsync(connectionString, CancellationToken.None))
                .ShouldBe(LegacyReindexObjectNames, ignoreOrder: true);

            int exitCode;
            using (var input = new StringReader(string.Empty))
            using (var output = new StringWriter())
            {
                var allowedOptions = new CliUpgradeOptions(TenantId: 1, AutoConfirm: true, AllowDataLoss: true, AllowIncompatiblePlatform: true, ConfigPath: configPath);
                exitCode = await Program.RunAsync(allowedOptions, input, output, CancellationToken.None);
            }

            exitCode.ShouldBe(0);
            (await GetLegacyReindexObjectNamesAsync(connectionString, CancellationToken.None)).ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(configDirectory, recursive: true);
            await DropDatabaseAsync(databaseName, CancellationToken.None);
        }
    }
}
