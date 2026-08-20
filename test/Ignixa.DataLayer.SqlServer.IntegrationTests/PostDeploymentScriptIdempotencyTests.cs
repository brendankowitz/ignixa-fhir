using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class PostDeploymentScriptIdempotencyTests
{
    private const string DacpacResourceName = "Ignixa.DataLayer.SqlServer.Schema.dacpac";

    [SkippableFact]
    public async Task GivenAnAlreadyDeployedDatabase_WhenPublishedAgain_ThenSucceedsWithoutError()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")
            ?? throw new SkipException(
                "TEST_SQL_CONNECTION_STRING is not set (see docker-compose.test.yml) -- skipping, not failing.");

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

            // The schema targets Azure SQL Database, so publishing to the box SQL Server container
            // these tests use is a platform mismatch DacFx blocks unless told otherwise.
            var deployOptions = new DacDeployOptions { AllowIncompatiblePlatform = true };

            // First publish -- establishes the schema, including the post-deployment script's
            // partition split (770 boundaries) and 3 ResourceChangeType seed rows.
            dacServices.Deploy(package, databaseName, upgradeExisting: true, options: deployOptions);

            await using var verifyConnection = new SqlConnection(builder.ConnectionString);
            await verifyConnection.OpenAsync();

            var seedRowsAfterFirst = await GetSeedRowsAsync(verifyConnection);
            var boundaryCountAfterFirst = await GetBoundaryCountAsync(verifyConnection);
            seedRowsAfterFirst.Count.ShouldBe(3);
            boundaryCountAfterFirst.ShouldBe(770);

            // Second publish against the SAME now-populated database -- this is exactly the
            // scenario that failed with SQL72014 before the post-deployment script was made
            // re-runnable.
            Should.NotThrow(() => dacServices.Deploy(package, databaseName, upgradeExisting: true, options: deployOptions));

            var seedRowsAfterSecond = await GetSeedRowsAsync(verifyConnection);
            var boundaryCountAfterSecond = await GetBoundaryCountAsync(verifyConnection);
            var distinctBoundariesAfterSecond = await GetDistinctBoundaryCountAsync(verifyConnection);

            // Compare the actual rows, not just how many there are: a re-publish that deleted and
            // re-inserted the seed rows would keep the count at 3 and still be wrong.
            seedRowsAfterSecond.ShouldBe(seedRowsAfterFirst);

            // Not ShouldBe(boundaryCountAfterFirst): the script maintains a ROLLING window anchored
            // on sysutcdatetime() (48h of history, 720h of future), so a second publish that lands
            // in a later UTC hour legitimately extends the window forward by the hours elapsed.
            // What must never happen is a boundary being duplicated or lost -- so assert the window
            // only ever grows, and that every boundary in it is distinct.
            boundaryCountAfterSecond.ShouldBeGreaterThanOrEqualTo(boundaryCountAfterFirst);
            distinctBoundariesAfterSecond.ShouldBe(boundaryCountAfterSecond);
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

    private const string BoundaryFromClause = @"
        FROM sys.partition_range_values prv
        JOIN sys.partition_functions pf ON pf.function_id = prv.function_id
        WHERE pf.name = 'PartitionFunction_ResourceChangeData_Timestamp'";

    private static async Task<int> GetBoundaryCountAsync(SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*)" + BoundaryFromClause;
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<int> GetDistinctBoundaryCountAsync(SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(DISTINCT CONVERT(DATETIME2(7), prv.value))" + BoundaryFromClause;
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<List<(byte Id, string Name)>> GetSeedRowsAsync(SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ResourceChangeTypeId, Name FROM dbo.ResourceChangeType ORDER BY ResourceChangeTypeId";
        await using var reader = await command.ExecuteReaderAsync();

        // ResourceChangeTypeId is TINYINT, so it comes back as a byte -- reading it as Int32 throws.
        var rows = new List<(byte Id, string Name)>();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetByte(0), reader.GetString(1)));
        }

        return rows;
    }
}
