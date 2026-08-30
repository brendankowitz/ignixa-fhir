using System.Data;
using Microsoft.Data.SqlClient;

namespace Ignixa.DataLayer.SqlServer;

public sealed class LastNCodeGroupBackfillService : ILastNCodeGroupBackfillService
{
    private readonly ISqlExecutionService _executionService;

    public LastNCodeGroupBackfillService(ISqlExecutionService executionService)
    {
        ArgumentNullException.ThrowIfNull(executionService);
        _executionService = executionService;
    }

    public async Task EnableScopeAsync(
        int tenantId,
        LastNCodeGroupScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        using SqlCommand command = CreateScopeCommand("dbo.EnableLastNCodeGroupScope", scope);
        await _executionService.ExecuteNonQueryAsync(tenantId, command, cancellationToken);
    }

    public async Task BuildAsync(
        int tenantId,
        LastNCodeGroupScope scope,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        LastNCodeGroupGenerationStatus? generation = null;
        try
        {
            generation = await StartAsync(tenantId, scope, cancellationToken);
            if (generation.SnapshotHighWaterSurrogateId is long highWater)
            {
                long? minimum = await ReadMinimumCurrentSurrogateIdAsync(
                    tenantId,
                    scope,
                    highWater,
                    cancellationToken);
                if (minimum is long start)
                {
                    await BackfillRangesAsync(
                        tenantId,
                        scope,
                        generation.Generation,
                        start,
                        highWater,
                        batchSize,
                        cancellationToken);
                }
            }

            await CompleteAsync(tenantId, scope, generation.Generation, cancellationToken);
        }
        catch (Exception exception)
        {
            if (generation is not null)
            {
                string failureReason = exception is OperationCanceledException
                    ? "Generation cancelled."
                    : exception.Message;
                try
                {
                    await FailAsync(
                        tenantId,
                        scope,
                        generation.Generation,
                        failureReason,
                        CancellationToken.None);
                }
                catch (Exception failureException)
                {
                    exception.Data["LastNCodeGroupFailureRecordingException"] = failureException;
                }
            }

            throw;
        }
    }

    private async Task<LastNCodeGroupGenerationStatus> StartAsync(
        int tenantId,
        LastNCodeGroupScope scope,
        CancellationToken cancellationToken)
    {
        using SqlCommand command = CreateScopeCommand("dbo.StartLastNCodeGroupGeneration", scope);
        SqlParameter generation = AddOutputParameter(command, "@StartedGeneration", SqlDbType.BigInt);
        SqlParameter state = AddOutputParameter(command, "@StartedState", SqlDbType.VarChar, 16);
        SqlParameter highWater = AddOutputParameter(
            command,
            "@StartedSnapshotHighWaterSurrogateId",
            SqlDbType.BigInt);

        await _executionService.ExecuteNonQueryAsync(
            tenantId,
            command,
            cancellationToken,
            disableRetries: true);

        return new LastNCodeGroupGenerationStatus(
            Convert.ToInt64(generation.Value),
            Convert.ToString(state.Value, System.Globalization.CultureInfo.InvariantCulture)
                ?? throw new InvalidOperationException("Generation start did not return a state."),
            highWater.Value is DBNull ? null : Convert.ToInt64(highWater.Value));
    }

    private async Task<long?> ReadMinimumCurrentSurrogateIdAsync(
        int tenantId,
        LastNCodeGroupScope scope,
        long highWater,
        CancellationToken cancellationToken)
    {
        using var command = new SqlCommand(
            """
            SELECT MIN(ResourceSurrogateId)
            FROM dbo.Resource
            WHERE ResourceTypeId = @ResourceTypeId
                AND IsHistory = 0
                AND IsDeleted = 0
                AND ResourceSurrogateId <= @SnapshotHighWaterSurrogateId;
            """);
        command.Parameters.Add(new SqlParameter("@ResourceTypeId", SqlDbType.SmallInt)
        {
            Value = scope.ResourceTypeId,
        });
        command.Parameters.Add(new SqlParameter("@SnapshotHighWaterSurrogateId", SqlDbType.BigInt)
        {
            Value = highWater,
        });

        IReadOnlyList<long?> values = await _executionService.ExecuteReaderAsync<long?>(
            tenantId,
            command,
            reader => reader.IsDBNull(0) ? null : reader.GetInt64(0),
            cancellationToken);
        return values.Single();
    }

    private async Task BackfillRangesAsync(
        int tenantId,
        LastNCodeGroupScope scope,
        long generation,
        long start,
        long highWater,
        int batchSize,
        CancellationToken cancellationToken)
    {
        long currentStart = start;
        while (currentStart <= highWater)
        {
            long batchOffset = (long)batchSize - 1;
            long currentEnd = highWater - currentStart < batchOffset
                ? highWater
                : checked(currentStart + batchOffset);

            using SqlCommand command = CreateScopeCommand("dbo.BackfillLastNCodeGroupBatch", scope);
            command.Parameters.Add(new SqlParameter("@Generation", SqlDbType.BigInt) { Value = generation });
            command.Parameters.Add(new SqlParameter("@StartResourceSurrogateId", SqlDbType.BigInt) { Value = currentStart });
            command.Parameters.Add(new SqlParameter("@EndResourceSurrogateId", SqlDbType.BigInt) { Value = currentEnd });
            await _executionService.ExecuteNonQueryAsync(tenantId, command, cancellationToken);

            if (currentEnd == highWater)
            {
                break;
            }

            currentStart = checked(currentEnd + 1);
        }
    }

    private async Task CompleteAsync(
        int tenantId,
        LastNCodeGroupScope scope,
        long generation,
        CancellationToken cancellationToken)
    {
        using SqlCommand command = CreateScopeCommand("dbo.CompleteLastNCodeGroupGeneration", scope);
        command.Parameters.Add(new SqlParameter("@Generation", SqlDbType.BigInt) { Value = generation });
        await _executionService.ExecuteNonQueryAsync(
            tenantId,
            command,
            cancellationToken,
            disableRetries: true);
    }

    private async Task FailAsync(
        int tenantId,
        LastNCodeGroupScope scope,
        long generation,
        string failureReason,
        CancellationToken cancellationToken)
    {
        using SqlCommand command = CreateScopeCommand("dbo.FailLastNCodeGroupGeneration", scope);
        command.Parameters.Add(new SqlParameter("@Generation", SqlDbType.BigInt) { Value = generation });
        command.Parameters.Add(new SqlParameter("@FailureReason", SqlDbType.VarChar, -1) { Value = failureReason });
        await _executionService.ExecuteNonQueryAsync(tenantId, command, cancellationToken);
    }

    private static SqlCommand CreateScopeCommand(string procedureName, LastNCodeGroupScope scope)
    {
#pragma warning disable CA2100
        var command = new SqlCommand(procedureName)
#pragma warning restore CA2100
        {
            CommandType = CommandType.StoredProcedure,
        };
        command.Parameters.Add(new SqlParameter("@ResourceTypeId", SqlDbType.SmallInt)
        {
            Value = scope.ResourceTypeId,
        });
        command.Parameters.Add(new SqlParameter("@SearchParamId", SqlDbType.SmallInt)
        {
            Value = scope.SearchParamId,
        });
        return command;
    }

    private static SqlParameter AddOutputParameter(
        SqlCommand command,
        string name,
        SqlDbType type,
        int size = 0)
    {
        var parameter = new SqlParameter(name, type, size)
        {
            Direction = ParameterDirection.Output,
        };
        command.Parameters.Add(parameter);
        return parameter;
    }
}
