using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IO;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;

/// <summary>
/// Test fixture providing a real, uniquely-named scratch tenant database, restored from a schema deployed once
/// per test process (see <see cref="SchemaTemplate"/>) and backed by a real <see cref="SqlExecutionService"/>.
/// Reused by every SQL-backed integration test in the Phase D write-path plan. Follows the
/// fake-<see cref="ITenantConfigurationStore"/> pattern established in SchemaDeployerUpgradeTests.cs.
/// </summary>
public sealed class TestTenantDatabase
{
    public const int TestTenantId = 1;

    private readonly string _databaseName;
    private SqlServerSearchIndexReferenceDataCache? _cache;

    private TestTenantDatabase(string databaseName, int tenantId, ISqlExecutionService sqlExecutionService)
    {
        _databaseName = databaseName;
        TenantId = tenantId;
        SqlExecutionService = sqlExecutionService;
    }

    public int TenantId { get; }

    public ISqlExecutionService SqlExecutionService { get; }

    // Exposed for consumers that need to build their own tenant store over this database rather than use
    // the one above (e.g. TerminologyTestFixture, which must also serve the system partition), and so need
    // the raw connection string rather than the tenant-routed ISqlExecutionService abstraction.
    public string ConnectionString => BuildConnectionStringForDatabase(_databaseName);

    public static async Task<TestTenantDatabase> CreateEmptyAsync(CancellationToken cancellationToken = default)
    {
        var databaseName = $"IgnixaDataLayerSqlServerTest_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionStringForDatabase(databaseName);

        var template = await SchemaTemplate.Value.WaitAsync(cancellationToken);
        await RestoreTemplateAsync(template, databaseName, cancellationToken);

        var tenantConfigurationStore = new SingleTenantStore(connectionString);

        // dbo.ResourceType has no seed data of its own: the dacpac's post-deployment script only
        // seeds dbo.ResourceChangeType (see Script.PostDeployment.sql), and real deployments only
        // ever populate ResourceType on-demand via the write path's GetOrCreateResourceTypeIdAsync
        // (Task 6, not built yet). Seed the one row cache tests need for a "known resource type"
        // lookup so this fixture is usable before that on-demand-creation path exists.
        await SeedResourceTypeAsync(connectionString, "Patient", cancellationToken);

        var sqlExecutionService = new SqlExecutionService(
            tenantConfigurationStore,
            new ManagedIdentityConnectionStringValidator("Development", NullLogger<ManagedIdentityConnectionStringValidator>.Instance),
            NullLogger<SqlExecutionService>.Instance);

        return new TestTenantDatabase(databaseName, TestTenantId, sqlExecutionService);
    }

    private static async Task SeedResourceTypeAsync(string connectionString, string resourceTypeName, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO dbo.ResourceType (Name) VALUES (@Name)";
        command.Parameters.AddWithValue("@Name", resourceTypeName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // CA2100 suppressed: this is a test-only raw-SQL helper -- callers pass literal assertion
    // queries, never untrusted input, matching the same suppression rationale used throughout this
    // fixture and SchemaDeployerUpgradeTests.cs for test-controlled SQL text.
    public async Task<T> ExecuteScalarAsync<T>(string sql, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(BuildConnectionStringForDatabase(_databaseName));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100
        command.CommandText = sql;
#pragma warning restore CA2100
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return (T)Convert.ChangeType(result!, typeof(T));
    }

    /// <summary>
    /// Reads a single <c>VARBINARY</c> column value (e.g. <c>dbo.Resource.RawResource</c>). Separate
    /// from <see cref="ExecuteScalarAsync{T}"/> because that method routes through
    /// <see cref="Convert.ChangeType(object, Type)"/>, which throws on <c>byte[]</c>.
    /// </summary>
    // CA2100 suppressed: this is a test-only raw-SQL helper -- callers pass literal assertion
    // queries, never untrusted input, matching the same suppression rationale used throughout this
    // fixture and SchemaDeployerUpgradeTests.cs for test-controlled SQL text.
    public async Task<byte[]?> ExecuteScalarBytesAsync(string sql, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(BuildConnectionStringForDatabase(_databaseName));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100
        command.CommandText = sql;
#pragma warning restore CA2100
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as byte[];
    }

    // CA2100 suppressed: this is a test-only raw-SQL helper -- callers pass literal assertion
    // queries, never untrusted input, matching the same suppression rationale used throughout this
    // fixture and SchemaDeployerUpgradeTests.cs for test-controlled SQL text.
    public async Task ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(BuildConnectionStringForDatabase(_databaseName));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100
        command.CommandText = sql;
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DisposeAsync()
    {
        _cache?.Dispose();
        await DropDatabaseAsync(_databaseName, CancellationToken.None);
    }

    /// <summary>
    /// Provisions an empty tenant database and wires a fully-constructed <see cref="SqlServerFhirRepository"/>
    /// against it (Task 1-4's compressor/cache/merge-repository pieces, wired once here). Note the
    /// construction order: <see cref="SqlServerPostMergeExtensionUpdater"/> is built BEFORE
    /// <see cref="SqlServerMergeRepository"/> and passed into it (Task 4's constructor requires it).
    /// This is the single place all of Tasks 6-9's tests get a fully-wired <see cref="SqlServerFhirRepository"/>
    /// from -- built once here, reused by every later task's test via this same factory method.
    /// </summary>
    public static async Task<TestTenantDatabase> CreateSqlServerFhirRepositoryAsync()
    {
        var database = await CreateEmptyAsync();
        var cache = new SqlServerSearchIndexReferenceDataCache(
            database.SqlExecutionService, database.TenantId, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        database._cache = cache;
        await cache.PreloadResourceTypesAsync(CancellationToken.None);
        var compressor = new GzipResourceCompressor(new RecyclableMemoryStreamManager());
        var extensionUpdater = new SqlServerPostMergeExtensionUpdater(
            database.SqlExecutionService, database.TenantId, NullLogger<SqlServerPostMergeExtensionUpdater>.Instance);
        var mergeRepository = new SqlServerMergeRepository(
            database.SqlExecutionService, database.TenantId, compressor, cache, extensionUpdater, NullLogger<SqlServerMergeRepository>.Instance);
        database.MergeRepository = mergeRepository;
        database.Repository = new SqlServerFhirRepository(
            database.SqlExecutionService, database.TenantId, compressor, cache, mergeRepository,
            NullLogger<SqlServerFhirRepository>.Instance);
        return database;
    }

    public SqlServerFhirRepository Repository { get; private set; } = null!;

    /// <summary>
    /// The same <see cref="SqlServerMergeRepository"/> instance <see cref="Repository"/> was constructed
    /// with, exposed so a test can drive a merge directly and then assert against what the repository above
    /// sees -- the two must share one instance or the assertion proves nothing.
    /// </summary>
    public SqlServerMergeRepository MergeRepository { get; private set; } = null!;

    private sealed class SingleTenantStore : ITenantConfigurationStore
    {
        private readonly TenantConfiguration _tenant;

        public SingleTenantStore(string connectionString)
        {
            _tenant = new TenantConfiguration
            {
                TenantId = TestTenantId,
                DisplayName = "Test Tenant",
                FhirVersion = "4.0",
                Storage = new TenantStorageConfiguration { Type = "SqlServer", ConnectionString = connectionString },
            };
        }

        public TenantMode Mode => TenantMode.Isolated;

        public ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(int tenantId, CancellationToken ct = default)
            => new(tenantId == TestTenantId ? _tenant : null);

        public ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(CancellationToken ct = default)
            => new((IReadOnlyList<TenantConfiguration>)new List<TenantConfiguration> { _tenant });

        public ValueTask<TenantConfiguration?> ResolveByHostAsync(string host, CancellationToken cancellationToken = default)
            => new(_tenant.Hostnames.Contains(host, StringComparer.OrdinalIgnoreCase) ? _tenant : null);
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
            throw new InvalidOperationException(
                "TEST_SQL_CONNECTION_STRING must be set to run this test (see docker-compose.test.yml).");
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

    /// <summary>
    /// Serialises CREATE DATABASE across the whole test process.
    /// <para>
    /// SQL Server already serialises database creation internally — it copies <c>model</c> — so sixteen
    /// xUnit collections issuing it at once do not go any faster; they queue on the server while each
    /// client's 30-second command timeout runs down. Tests at the back of that queue failed in fixture
    /// setup with "Execution Timeout Expired" after 1ms of their own work, scattered randomly across
    /// classes on every run. Queueing on this side of the wire makes the wait visible instead of fatal.
    /// </para>
    /// </summary>
    private static readonly SemaphoreSlim DatabaseCreationGate = new(1, 1);

    /// <summary>
    /// The deployed schema, captured once per test process as a backup that every test database is restored from.
    /// <para>
    /// A DacFx deploy costs 16-18 seconds and cannot run concurrently (sixteen xUnit collections deploying at once
    /// put 32 sleeping DacFx sessions on the server and the deploys time out inside SqlReverseEngineer), so one
    /// serialised deploy per test made fixture setup, not the tests, take most of a two-hour CI run. A restore of
    /// the same schema takes about a second. Tests whose subject is deployment itself (SchemaDeployer*,
    /// SchemaVersionResolver, PopulatedTerminologySchemaUpgrade, PostDeploymentScriptIdempotency) deploy through
    /// their own code paths and never come through here.
    /// </para>
    /// <para>
    /// The <see cref="Lazy{T}"/> default (ExecutionAndPublication) guarantees exactly one deploy; a failed deploy
    /// stays cached, so every test reports the same root cause instead of retrying a two-minute operation.
    /// </para>
    /// </summary>
    private static readonly Lazy<Task<SchemaTemplateBackup>> SchemaTemplate = new(CreateSchemaTemplateBackupAsync);

    private const int DatabaseLifecycleCommandTimeoutSeconds = 180;

    private static async Task<SchemaTemplateBackup> CreateSchemaTemplateBackupAsync()
    {
        var templateName = $"IgnixaDataLayerSqlServerTestTemplate_{Guid.NewGuid():N}";
        var cancellationToken = CancellationToken.None;

        await CreateEmptyDatabaseAsync(templateName, cancellationToken);

        var tenantConfigurationStore = new SingleTenantStore(BuildConnectionStringForDatabase(templateName));
        var deployer = new SchemaDeployer(
            tenantConfigurationStore,
            new FakeHostEnvironment(),
            Options.Create(new SqlServerOptions { AutomaticSchemaDeploymentEnabled = true }),
            new SchemaVersionResolver(tenantConfigurationStore, NullLogger<SchemaVersionResolver>.Instance),
            NullLogger<SchemaDeployer>.Instance);
        await deployer.DeployIfEmptyAsync(TestTenantId, cancellationToken);

        var template = await ReadTemplateFilesAsync(templateName, cancellationToken);
        await BackupTemplateAsync(template, cancellationToken);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteBackupFile(template.BackupPath);

        // Only the backup is needed from here on; restores never touch the source database. Not in a finally: a
        // failed deploy leaves the template behind for diagnosis rather than risk a drop failure replacing the
        // deploy error that every test will report.
        await DropDatabaseAsync(templateName, cancellationToken);
        return template;
    }

    private static async Task<SchemaTemplateBackup> ReadTemplateFilesAsync(string templateName, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(BuildConnectionStringForDatabase("master"));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT type_desc, name, physical_name FROM sys.master_files WHERE database_id = DB_ID(@Name)";
        command.Parameters.AddWithValue("@Name", templateName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var files = new List<(string Type, string LogicalName, string PhysicalName)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            files.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        // RESTORE ... WITH MOVE must relocate every file, and the restore statement below names exactly one data
        // and one log file. A schema that adds a filegroup or file needs RestoreTemplateAsync extended to match.
        if (files.Count != 2 || files.Count(f => f.Type == "ROWS") != 1 || files.Count(f => f.Type == "LOG") != 1)
        {
            throw new InvalidOperationException(
                $"Expected the deployed template database '{templateName}' to have exactly one ROWS and one LOG file, but found: " +
                string.Join(", ", files.Select(f => $"{f.Type} '{f.LogicalName}'")) + ".");
        }

        var data = files.Single(f => f.Type == "ROWS");
        var log = files.Single(f => f.Type == "LOG");

        // The data directory, not InstanceDefaultBackupPath: it is necessarily writable by the SQL Server service
        // account, and deriving it from the server's own path keeps '\' vs '/' correct for Windows and the Linux
        // container alike. The path is server-side, which is what BACKUP/RESTORE need when the server is in Docker.
        var directory = data.PhysicalName[..(data.PhysicalName.LastIndexOfAny(['\\', '/']) + 1)];

        return new SchemaTemplateBackup(
            templateName,
            directory + templateName + ".bak",
            data.LogicalName,
            data.PhysicalName,
            log.LogicalName,
            log.PhysicalName);
    }

    private static async Task BackupTemplateAsync(SchemaTemplateBackup template, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(BuildConnectionStringForDatabase("master"));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = DatabaseLifecycleCommandTimeoutSeconds;
        command.CommandText = "BACKUP DATABASE @Name TO DISK = @Path WITH INIT, COPY_ONLY";
        command.Parameters.AddWithValue("@Name", template.TemplateName);
        command.Parameters.AddWithValue("@Path", template.BackupPath);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // Serialised on the creation gate for the reason that gate exists: a restore creates a database too.
    private static async Task RestoreTemplateAsync(SchemaTemplateBackup template, string databaseName, CancellationToken cancellationToken)
    {
        await DatabaseCreationGate.WaitAsync(cancellationToken);

        try
        {
            await using var connection = new SqlConnection(BuildConnectionStringForDatabase("master"));
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandTimeout = DatabaseLifecycleCommandTimeoutSeconds;
            command.CommandText = """
                RESTORE DATABASE @Name FROM DISK = @BackupPath
                WITH MOVE @DataLogicalName TO @DataPath, MOVE @LogLogicalName TO @LogPath
                """;
            command.Parameters.AddWithValue("@Name", databaseName);
            command.Parameters.AddWithValue("@BackupPath", template.BackupPath);
            command.Parameters.AddWithValue("@DataLogicalName", template.DataLogicalName);
            command.Parameters.AddWithValue("@DataPath", template.DataPathFor(databaseName));
            command.Parameters.AddWithValue("@LogLogicalName", template.LogLogicalName);
            command.Parameters.AddWithValue("@LogPath", template.LogPathFor(databaseName));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            DatabaseCreationGate.Release();
        }
    }

    // ProcessExit handlers are synchronous, hence the blocking calls. xp_delete_files is the server-side way to
    // remove a file the test process cannot reach directly (the server may be in a container). A failure here
    // cannot fail a test run that has already finished, so it is reported rather than thrown.
    private static void DeleteBackupFile(string backupPath)
    {
        try
        {
            using var connection = new SqlConnection(BuildConnectionStringForDatabase("master"));
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "EXEC master.sys.xp_delete_files @Path";
            command.Parameters.AddWithValue("@Path", backupPath);
            command.ExecuteNonQuery();
        }
        catch (SqlException ex)
        {
            Console.Error.WriteLine($"Could not delete the integration-test schema template backup '{backupPath}': {ex.Message}");
        }
    }

    private sealed record SchemaTemplateBackup(
        string TemplateName,
        string BackupPath,
        string DataLogicalName,
        string DataPhysicalName,
        string LogLogicalName,
        string LogPhysicalName)
    {
        // The template's file names embed its unique database name, so substituting it yields unique, collision-free
        // files for each restored copy in the same directory.
        public string DataPathFor(string databaseName) => DataPhysicalName.Replace(TemplateName, databaseName, StringComparison.Ordinal);

        public string LogPathFor(string databaseName) => LogPhysicalName.Replace(TemplateName, databaseName, StringComparison.Ordinal);
    }

    private static async Task CreateEmptyDatabaseAsync(string databaseName, CancellationToken cancellationToken)
    {
        await DatabaseCreationGate.WaitAsync(cancellationToken);

        try
        {
            var masterConnectionString = BuildConnectionStringForDatabase("master");
            await using var connection = new SqlConnection(masterConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandTimeout = DatabaseLifecycleCommandTimeoutSeconds;
#pragma warning disable CA2100
            command.CommandText = $"CREATE DATABASE [{databaseName}]";
#pragma warning restore CA2100
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            DatabaseCreationGate.Release();
        }
    }

    private static async Task DropDatabaseAsync(string databaseName, CancellationToken cancellationToken)
    {
        var masterConnectionString = BuildConnectionStringForDatabase("master");
        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = DatabaseLifecycleCommandTimeoutSeconds;
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
}
