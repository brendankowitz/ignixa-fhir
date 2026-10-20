using System.Text.Json;
using Ignixa.DataLayer.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;
using Shouldly;

namespace Ignixa.SchemaUpgrade.Cli.Tests;

// The operator path must run the same online pre-deploy conversion of
// IX_Resource_ResourceTypeId_ResourceSurrgateId as SchemaDeployer's automatic path -- but only once the
// operator has confirmed, because declining promises "nothing was applied". The tenant is version-3-shaped:
// the current dacpac deployed, the index put back to its version-3 definition (no INCLUDE), stamped 3.
public class RunAsyncOnlineIndexMigrationTests
{
    private const string IndexName = "IX_Resource_ResourceTypeId_ResourceSurrgateId";

    [SkippableFact]
    public async Task GivenAV3ShapedTenant_WhenTheOperatorDeclines_ThenTheIndexIsNotConverted()
    {
        await using var tenant = await V3ShapedTenant.CreateAsync();

        using var input = new StringReader("n");
        using var output = new StringWriter();
        var exitCode = await Program.RunAsync(tenant.Options(autoConfirm: false), input, output, CancellationToken.None);

        exitCode.ShouldBe(1);
        output.ToString().ShouldContain($"{IndexName} on dbo.Resource will be converted in place ONLINE");
        output.ToString().ShouldNotContain($"converting {IndexName}");
        (await tenant.IndexIncludesResourceIdAsync()).ShouldBeFalse();
    }

    [SkippableFact]
    public async Task GivenAV3ShapedTenant_WhenTheOperatorConfirms_ThenTheIndexIsConvertedAndTheVersionStamped()
    {
        await using var tenant = await V3ShapedTenant.CreateAsync();

        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        var exitCode = await Program.RunAsync(tenant.Options(autoConfirm: true), input, output, CancellationToken.None);

        exitCode.ShouldBe(0, output.ToString());
        (await tenant.IndexIncludesResourceIdAsync()).ShouldBeTrue();
        (await tenant.ScalarAsync<int>("SELECT MAX(Version) FROM dbo.SchemaVersion")).ShouldBe(SchemaVersionConstants.CurrentVersion);

        // The end state alone cannot tell the online conversion from the deploy's offline Drop/Create, which
        // produces the same index. The progress the conversion writes to the output can -- and it is what the
        // operator watches during a build that can take hours.
        var text = output.ToString();
        text.ShouldContain($"converting {IndexName} on dbo.Resource ONLINE");
        text.ShouldContain("an interruption rolls the build back and the next attempt restarts it from scratch");
        text.ShouldContain($"converted {IndexName} on dbo.Resource ONLINE in");
    }

    private sealed class V3ShapedTenant : IAsyncDisposable
    {
        private readonly string _databaseName;
        private readonly DirectoryInfo _configDirectory;

        private V3ShapedTenant(string databaseName, string connectionString, DirectoryInfo configDirectory)
        {
            _databaseName = databaseName;
            ConnectionString = connectionString;
            _configDirectory = configDirectory;
        }

        private string ConnectionString { get; }

        private string ConfigPath => Path.Combine(_configDirectory.FullName, "appsettings.json");

        public static async Task<V3ShapedTenant> CreateAsync()
        {
            var configured = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING");
            if (string.IsNullOrEmpty(configured))
            {
                throw new SkipException(
                    "TEST_SQL_CONNECTION_STRING is not set (see docker-compose.test.yml) -- skipping, not failing.");
            }

            var databaseName = $"SchemaUpgradeCliOnlineIndexTest_{Guid.NewGuid():N}";
            var tenant = new V3ShapedTenant(
                databaseName,
                new SqlConnectionStringBuilder(configured) { InitialCatalog = databaseName }.ConnectionString,
                Directory.CreateTempSubdirectory("schema-upgrade-cli-online-index-test-"));
            try
            {
                await ExecuteAsync(tenant.MasterConnectionString(), $"CREATE DATABASE [{databaseName}]");
                using (var dacpacStream = typeof(SchemaDeployer).Assembly.GetManifestResourceStream("Ignixa.DataLayer.SqlServer.Schema.dacpac")
                    ?? throw new InvalidOperationException("Embedded schema dacpac not found."))
                using (var package = DacPackage.Load(dacpacStream))
                {
                    // The schema targets Azure SQL Database; the test server is a box SQL Server.
                    new DacServices(tenant.ConnectionString).Deploy(
                        package, databaseName, upgradeExisting: true,
                        options: new DacDeployOptions { AllowIncompatiblePlatform = true });
                }

                await ExecuteAsync(tenant.ConnectionString, $"""
                    CREATE UNIQUE NONCLUSTERED INDEX {IndexName}
                        ON dbo.Resource (ResourceTypeId, ResourceSurrogateId)
                        WHERE IsHistory = 0 AND IsDeleted = 0
                        WITH (DROP_EXISTING = ON)
                        ON PartitionScheme_ResourceTypeId (ResourceTypeId);
                    INSERT dbo.SchemaVersion (Version) VALUES (3);
                    """);
                await File.WriteAllTextAsync(tenant.ConfigPath, $$"""
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
                              "ConnectionString": {{JsonSerializer.Serialize(tenant.ConnectionString)}}
                            }
                          }
                        ]
                      }
                    }
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

        public CliUpgradeOptions Options(bool autoConfirm)
            => new(TenantId: 1, AutoConfirm: autoConfirm, AllowDataLoss: false, AllowIncompatiblePlatform: true, ConfigPath: ConfigPath);

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

        public async Task<T> ScalarAsync<T>(string sql)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
#pragma warning disable CA2100 // Test-only SQL built from constants.
            await using var command = new SqlCommand(sql, connection);
#pragma warning restore CA2100
            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async ValueTask DisposeAsync()
        {
            _configDirectory.Delete(recursive: true);
            await ExecuteAsync(MasterConnectionString(), $"""
                IF DB_ID('{_databaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{_databaseName}];
                END
                """);
        }

        private string MasterConnectionString()
            => new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" }.ConnectionString;

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
}
