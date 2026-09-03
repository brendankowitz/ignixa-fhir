using System.Data;
using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Microsoft.Data.SqlClient;
using Shouldly;
using Xunit.Sdk;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class LastNSearchConcurrencyTests
{
    [SkippableFact]
    public async Task GivenAReadyReadBlockedDuringMaterialization_WhenRebuildStarts_ThenRebuildWaitsForTheRead()
    {
        // Arrange
        await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();
        await SeedReadyScopeAsync(database);
        await using SqlConnection blockerConnection = await database.OpenConnectionAsync(CancellationToken.None);
        await using SqlTransaction blockerTransaction = (SqlTransaction)await blockerConnection.BeginTransactionAsync();
        await using (SqlCommand blockCandidate = blockerConnection.CreateCommand())
        {
            blockCandidate.Transaction = blockerTransaction;
            blockCandidate.CommandText = """
                SELECT COUNT_BIG(*)
                FROM dbo.Resource WITH (TABLOCKX, HOLDLOCK);
                """;
            await blockCandidate.ExecuteScalarAsync();
        }

        await using SqlConnection readConnection = await database.OpenConnectionAsync(CancellationToken.None);
        await using (SqlCommand isolation = readConnection.CreateCommand())
        {
            isolation.CommandText = "SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;";
            await isolation.ExecuteNonQueryAsync();
        }

        CompiledSearch compiled = CreateCompiledLastN();
#pragma warning disable CA2025 // Both tasks are awaited before their owning connections leave scope.
        Task<IReadOnlyList<long>> readTask = ReadRowsAsync(readConnection, compiled);
#pragma warning restore CA2025
        await WaitForSharedScopeLockAsync(database);

        await using SqlConnection rebuildConnection = await database.OpenConnectionAsync(CancellationToken.None);
#pragma warning disable CA2025 // Both tasks are awaited before their owning connections leave scope.
        Task<int> rebuildTask = StartGenerationAsync(rebuildConnection);
#pragma warning restore CA2025

        // Act
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        // Assert
        rebuildTask.IsCompleted.ShouldBeFalse();
        (await ReadGenerationStateAsync(database)).ShouldBe("Ready");

        await blockerTransaction.CommitAsync();
        (await readTask).ShouldBe([1L]);
        await rebuildTask;
        (await ReadGenerationStateAsync(database)).ShouldBe("Building");
    }

    private static CompiledSearch CreateCompiledLastN()
    {
        var plan = new SearchPlan
        {
            Query = new QueryPlan(
                [new CteDefinition.ResourceSource(104)],
                new MatchPageSpec(
                    new CteRef(0),
                    Shape: new ResultShape.LastN(new LastNSpec(104, 210, 211, 1)))),
        };
        return plan.Compile();
    }

    private static SqlCommand CreateCommand(SqlConnection connection, CompiledSearch compiled)
    {
        SqlCommand command = connection.CreateCommand();
#pragma warning disable CA2100
        command.CommandText = compiled.Sql;
#pragma warning restore CA2100
        foreach (EmittedSqlParameter emittedParameter in compiled.Parameters)
        {
            SqlParameter parameter = emittedParameter.Value switch
            {
                short value => new SqlParameter(emittedParameter.Name, SqlDbType.SmallInt) { Value = value },
                int value => new SqlParameter(emittedParameter.Name, SqlDbType.Int) { Value = value },
                _ => throw new InvalidOperationException("Unexpected compiled test parameter."),
            };
            command.Parameters.Add(parameter);
        }

        return command;
    }

    private static async Task<IReadOnlyList<long>> ReadRowsAsync(
        SqlConnection connection,
        CompiledSearch compiled)
    {
        await using SqlCommand command = CreateCommand(connection, compiled);
        List<long> rows = [];
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetInt64(1));
        }

        return rows;
    }

    private static async Task<int> StartGenerationAsync(SqlConnection connection)
    {
        await using SqlCommand command = CreateStartGenerationCommand(connection);
        return await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedReadyScopeAsync(LastNTestDatabase database)
    {
        await database.SeedResourceAsync(104, 1, "observation-1", 1, false, false, CancellationToken.None);
        await using SqlCommand command = database.Connection.CreateCommand();
        command.CommandText = """
            INSERT INTO dbo.LastNCodeGroupGeneration
                (ResourceTypeId, SearchParamId, Generation, State, StartedDateTime, CompletedDateTime)
            VALUES (104, 210, 1, 'Ready', SYSUTCDATETIME(), SYSUTCDATETIME());

            INSERT INTO dbo.LastNObservationCodeGroup
                (ResourceTypeId, SearchParamId, ResourceSurrogateId, GroupKind, CodeGroupId)
            VALUES (104, 210, 1, 0, 1);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static SqlCommand CreateStartGenerationCommand(SqlConnection connection)
    {
        SqlCommand command = connection.CreateCommand();
        command.CommandText = "dbo.StartLastNCodeGroupGeneration";
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.Add("@ResourceTypeId", SqlDbType.SmallInt).Value = (short)104;
        command.Parameters.Add("@SearchParamId", SqlDbType.SmallInt).Value = (short)210;
        command.Parameters.Add("@AttemptId", SqlDbType.UniqueIdentifier).Value = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;
        command.Parameters.Add("@CurrentDateTime", SqlDbType.DateTime2).Value = now;
        command.Parameters.Add("@LeaseExpiresDateTime", SqlDbType.DateTime2).Value = now.AddMinutes(1);
        return command;
    }

    private static async Task<string> ReadGenerationStateAsync(LastNTestDatabase database)
    {
        await using SqlCommand command = database.Connection.CreateCommand();
        command.CommandText = """
            SELECT State
            FROM dbo.LastNCodeGroupGeneration
            WHERE ResourceTypeId = 104 AND SearchParamId = 210;
            """;
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task WaitForSharedScopeLockAsync(LastNTestDatabase database)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            await using SqlConnection connection = await database.OpenConnectionAsync(CancellationToken.None);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = """
                DECLARE @result int;
                BEGIN TRANSACTION;
                EXEC @result = sys.sp_getapplock
                    @Resource = 'LastNCodeGroup:104:210',
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Transaction',
                    @LockTimeout = 0;
                ROLLBACK TRANSACTION;
                SELECT @result;
                """;
            int result = (int)(await command.ExecuteScalarAsync())!;
            if (result < 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }

        throw new XunitException("The compiled read did not acquire the shared LastN scope lock.");
    }
}
