using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class PostDeploymentScriptIdempotencyTests
{
    private const string DacpacResourceName = "Ignixa.DataLayer.SqlServer.Schema.dacpac";

    [Fact]
    public async Task GivenAnAlreadyDeployedDatabase_WhenPublishedAgain_ThenSucceedsWithoutError()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "TEST_SQL_CONNECTION_STRING is not set. Run the docker-compose.test.yml SQL Server " +
                "container and set this environment variable before running integration tests.");

        var databaseName = $"IgnixaPostDeployIdempotency_{Guid.NewGuid():N}";
        var builder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = databaseName };

        await using (var masterConnection = new SqlConnection(new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }.ConnectionString))
        {
            await masterConnection.OpenAsync();
            await using var createCommand = masterConnection.CreateCommand();
#pragma warning disable CA2100
            createCommand.CommandText = $"CREATE DATABASE [{databaseName}]";
#pragma warning restore CA2100
            await createCommand.ExecuteNonQueryAsync();
        }

        try
        {
            using var dacpacStream = typeof(SchemaDeployer).Assembly.GetManifestResourceStream(DacpacResourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{DacpacResourceName}' not found in {typeof(SchemaDeployer).Assembly.FullName}.");
            using var package = DacPackage.Load(dacpacStream);
            var dacServices = new DacServices(builder.ConnectionString);

            // First publish -- establishes the schema, including the post-deployment script's
            // partition split (770 boundaries) and 3 ResourceChangeType seed rows.
            dacServices.Deploy(package, databaseName, upgradeExisting: true);

            // Second publish against the SAME now-populated database -- this is exactly the
            // scenario that failed with SQL72014 before this task's fix.
            Should.NotThrow(() => dacServices.Deploy(package, databaseName, upgradeExisting: true));

            await using var verifyConnection = new SqlConnection(builder.ConnectionString);
            await verifyConnection.OpenAsync();
            await using var countCommand = verifyConnection.CreateCommand();
            countCommand.CommandText = "SELECT COUNT(*) FROM dbo.ResourceChangeType";
            var rowCount = (int)(await countCommand.ExecuteScalarAsync())!;
            rowCount.ShouldBe(3);

            await using var partitionCommand = verifyConnection.CreateCommand();
            partitionCommand.CommandText = @"
                SELECT COUNT(*) FROM sys.partition_range_values prv
                JOIN sys.partition_functions pf ON pf.function_id = prv.function_id
                WHERE pf.name = 'PartitionFunction_ResourceChangeData_Timestamp'";
            var boundaryCount = (int)(await partitionCommand.ExecuteScalarAsync())!;
            boundaryCount.ShouldBe(770);
        }
        finally
        {
            await using var masterConnection = new SqlConnection(new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }.ConnectionString);
            await masterConnection.OpenAsync();
            await using var dropCommand = masterConnection.CreateCommand();
#pragma warning disable CA2100
            dropCommand.CommandText = $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]";
#pragma warning restore CA2100
            await dropCommand.ExecuteNonQueryAsync();
        }
    }
}
