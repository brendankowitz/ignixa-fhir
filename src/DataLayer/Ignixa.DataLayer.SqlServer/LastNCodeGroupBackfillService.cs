using System.Data;
using Microsoft.Data.SqlClient;

namespace Ignixa.DataLayer.SqlServer;

public sealed class LastNCodeGroupBackfillService : ILastNCodeGroupBackfillService
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(1);
    private readonly ISqlExecutionService _executionService;
    private readonly TimeProvider _timeProvider;

    public LastNCodeGroupBackfillService(
        ISqlExecutionService executionService,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(executionService);
        _executionService = executionService;
        _timeProvider = timeProvider ?? TimeProvider.System;
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

        Guid attemptId = Guid.NewGuid();
        LastNCodeGroupGenerationStatus? generation = null;
        try
        {
            generation = await StartAsync(tenantId, scope, attemptId, cancellationToken);
            if (generation.SnapshotHighWaterSurrogateId is long highWater)
            {
                long? start = generation.LastCommittedResourceSurrogateId switch
                {
                    long committed when committed == highWater => null,
                    long committed => checked(committed + 1),
                    _ => await ReadMinimumCurrentSurrogateIdAsync(
                        tenantId,
                        scope,
                        highWater,
                        cancellationToken),
                };
                if (start is long firstUncommitted)
                {
                    await BackfillRangesAsync(
                        tenantId,
                        scope,
                        generation.Generation,
                        attemptId,
                        firstUncommitted,
                        highWater,
                        batchSize,
                        cancellationToken);
                }
            }

            await CompleteAsync(
                tenantId,
                scope,
                generation.Generation,
                attemptId,
                cancellationToken);
        }
        catch (Exception exception)
        {
            if (generation is null)
            {
                try
                {
                    generation = await ReadGenerationByAttemptAsync(
                        tenantId,
                        scope,
                        attemptId,
                        CancellationToken.None);
                }
                catch (Exception reconciliationException)
                {
                    exception.Data["LastNCodeGroupStartReconciliationException"] =
                        reconciliationException;
                }
            }

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
                        attemptId,
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
        Guid attemptId,
        CancellationToken cancellationToken)
    {
        using SqlCommand command = CreateScopeCommand("dbo.StartLastNCodeGroupGeneration", scope);
        command.Parameters.Add(new SqlParameter("@AttemptId", SqlDbType.UniqueIdentifier)
        {
            Value = attemptId,
        });
        AddLeaseParameters(command);
        SqlParameter generation = AddOutputParameter(command, "@StartedGeneration", SqlDbType.BigInt);
        SqlParameter state = AddOutputParameter(command, "@StartedState", SqlDbType.VarChar, 16);
        SqlParameter highWater = AddOutputParameter(
            command,
            "@StartedSnapshotHighWaterSurrogateId",
            SqlDbType.BigInt);
        SqlParameter lastCommitted = AddOutputParameter(
            command,
            "@StartedLastCommittedResourceSurrogateId",
            SqlDbType.BigInt);
        SqlParameter leaseExpires = AddOutputParameter(
            command,
            "@StartedLeaseExpiresDateTime",
            SqlDbType.DateTime2);

        await _executionService.ExecuteNonQueryAsync(
            tenantId,
            command,
            cancellationToken,
            disableRetries: true);

        return new LastNCodeGroupGenerationStatus(
            attemptId,
            Convert.ToInt64(generation.Value),
            Convert.ToString(state.Value, System.Globalization.CultureInfo.InvariantCulture)
                ?? throw new InvalidOperationException("Generation start did not return a state."),
            highWater.Value is DBNull ? null : Convert.ToInt64(highWater.Value),
            lastCommitted.Value is DBNull ? null : Convert.ToInt64(lastCommitted.Value),
            leaseExpires.Value is DBNull
                ? null
                : new DateTimeOffset(
                    DateTime.SpecifyKind(Convert.ToDateTime(leaseExpires.Value), DateTimeKind.Utc)));
    }

    private async Task<LastNCodeGroupGenerationStatus?> ReadGenerationByAttemptAsync(
        int tenantId,
        LastNCodeGroupScope scope,
        Guid attemptId,
        CancellationToken cancellationToken)
    {
        using var command = new SqlCommand(
            """
            SELECT AttemptId, Generation, State, SnapshotHighWaterSurrogateId,
                   LastCommittedResourceSurrogateId, LeaseExpiresDateTime
            FROM dbo.LastNCodeGroupGeneration
            WHERE ResourceTypeId = @ResourceTypeId
                AND SearchParamId = @SearchParamId
                AND AttemptId = @AttemptId;
            """);
        command.Parameters.Add(new SqlParameter("@ResourceTypeId", SqlDbType.SmallInt)
        {
            Value = scope.ResourceTypeId,
        });
        command.Parameters.Add(new SqlParameter("@SearchParamId", SqlDbType.SmallInt)
        {
            Value = scope.SearchParamId,
        });
        command.Parameters.Add(new SqlParameter("@AttemptId", SqlDbType.UniqueIdentifier)
        {
            Value = attemptId,
        });

        IReadOnlyList<LastNCodeGroupGenerationStatus> statuses =
            await _executionService.ExecuteReaderAsync(
                tenantId,
                command,
                reader => new LastNCodeGroupGenerationStatus(
                    reader.GetGuid(0),
                    reader.GetInt64(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetInt64(3),
                    reader.IsDBNull(4) ? null : reader.GetInt64(4),
                    reader.IsDBNull(5)
                        ? null
                        : new DateTimeOffset(
                            DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc))),
                cancellationToken);
        return statuses.SingleOrDefault();
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
        Guid attemptId,
        long start,
        long highWater,
        int batchSize,
        CancellationToken cancellationToken)
    {
        long currentStart = start;
        while (currentStart <= highWater)
        {
            long currentEnd = CalculateBatchEnd(currentStart, highWater, batchSize);

            using SqlCommand command = CreateScopeCommand("dbo.BackfillLastNCodeGroupBatch", scope);
            command.Parameters.Add(new SqlParameter("@Generation", SqlDbType.BigInt) { Value = generation });
            command.Parameters.Add(new SqlParameter("@AttemptId", SqlDbType.UniqueIdentifier) { Value = attemptId });
            command.Parameters.Add(new SqlParameter("@StartResourceSurrogateId", SqlDbType.BigInt) { Value = currentStart });
            command.Parameters.Add(new SqlParameter("@EndResourceSurrogateId", SqlDbType.BigInt) { Value = currentEnd });
            AddLeaseExpirationParameter(command);
            await _executionService.ExecuteNonQueryAsync(tenantId, command, cancellationToken);

            if (currentEnd == highWater)
            {
                break;
            }

            currentStart = checked(currentEnd + 1);
        }
    }

    internal static long CalculateBatchEnd(long currentStart, long highWater, int batchSize)
    {
        long batchOffset = (long)batchSize - 1;
        long uncappedEnd = currentStart > long.MaxValue - batchOffset
            ? long.MaxValue
            : currentStart + batchOffset;
        return Math.Min(uncappedEnd, highWater);
    }

    private async Task CompleteAsync(
        int tenantId,
        LastNCodeGroupScope scope,
        long generation,
        Guid attemptId,
        CancellationToken cancellationToken)
    {
        using SqlCommand command = CreateScopeCommand("dbo.CompleteLastNCodeGroupGeneration", scope);
        command.Parameters.Add(new SqlParameter("@Generation", SqlDbType.BigInt) { Value = generation });
        command.Parameters.Add(new SqlParameter("@AttemptId", SqlDbType.UniqueIdentifier) { Value = attemptId });
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
        Guid attemptId,
        string failureReason,
        CancellationToken cancellationToken)
    {
        using SqlCommand command = CreateScopeCommand("dbo.FailLastNCodeGroupGeneration", scope);
        command.Parameters.Add(new SqlParameter("@Generation", SqlDbType.BigInt) { Value = generation });
        command.Parameters.Add(new SqlParameter("@AttemptId", SqlDbType.UniqueIdentifier) { Value = attemptId });
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

    private void AddLeaseParameters(SqlCommand command)
    {
        DateTimeOffset currentTime = _timeProvider.GetUtcNow();
        command.Parameters.Add(new SqlParameter("@CurrentDateTime", SqlDbType.DateTime2)
        {
            Value = currentTime.UtcDateTime,
        });
        command.Parameters.Add(new SqlParameter("@LeaseExpiresDateTime", SqlDbType.DateTime2)
        {
            Value = currentTime.Add(LeaseDuration).UtcDateTime,
        });
    }

    private void AddLeaseExpirationParameter(SqlCommand command)
    {
        command.Parameters.Add(new SqlParameter("@LeaseExpiresDateTime", SqlDbType.DateTime2)
        {
            Value = _timeProvider.GetUtcNow().Add(LeaseDuration).UtcDateTime,
        });
    }
}
