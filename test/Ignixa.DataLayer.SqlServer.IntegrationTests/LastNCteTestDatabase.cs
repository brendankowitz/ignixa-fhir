using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public sealed class LastNCteTestDatabase : IAsyncLifetime
{
    private readonly string _databaseName = $"LastNCteTest_{Guid.NewGuid():N}";
    private readonly string? _baseConnectionString = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING");
    private bool _created;

    public string GetConnectionString() => string.IsNullOrEmpty(_baseConnectionString)
        ? throw new SkipException("TEST_SQL_CONNECTION_STRING is not set -- skipping live SQL tests.")
        : BuildConnectionString(_databaseName);

    public async Task InitializeAsync()
    {
        if (string.IsNullOrEmpty(_baseConnectionString))
        {
            return;
        }

        await using var connection = new SqlConnection(BuildConnectionString("master"));
        await connection.OpenAsync();
#pragma warning disable CA2100
        await using var create = new SqlCommand($"CREATE DATABASE [{_databaseName}]", connection);
#pragma warning restore CA2100
        await create.ExecuteNonQueryAsync();
        _created = true;

        try
        {
            var deployer = new SchemaDeployer(
                new LastNSingleTenantStore(GetConnectionString()),
                new LastNFakeHostEnvironment(),
                Options.Create(new SqlServerOptions
                {
                    AutomaticSchemaDeploymentEnabled = true,
                    AllowIncompatiblePlatform = true,
                }),
                new LastNThrowingSchemaVersionResolver(),
                NullLogger<SchemaDeployer>.Instance);
            await deployer.DeployIfEmptyAsync(1, CancellationToken.None);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (!_created)
        {
            return;
        }

        await using var connection = new SqlConnection(BuildConnectionString("master"));
        await connection.OpenAsync();
        // The identifier belongs to this fixture; never drop the caller's configured database.
#pragma warning disable CA2100
        await using var drop = new SqlCommand(
            $"""
            ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
            DROP DATABASE [{_databaseName}];
            """, connection);
#pragma warning restore CA2100
        await drop.ExecuteNonQueryAsync();
        _created = false;
    }

    private string BuildConnectionString(string databaseName)
        => new SqlConnectionStringBuilder(_baseConnectionString) { InitialCatalog = databaseName }.ConnectionString;
}
