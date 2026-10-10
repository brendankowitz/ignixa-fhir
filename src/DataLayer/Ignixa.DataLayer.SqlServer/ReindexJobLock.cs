using System.Runtime.ExceptionServices;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Microsoft.Data.SqlClient;

namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// Runs an action while holding the singleton reindex application lock on the global conformance database.
/// </summary>
/// <remarks>
/// The lock is owned by the SQL transaction that acquires it, so the action has to run inside that transaction's
/// callback. Only the lock acquisition itself is inside the transient-fault retry pipeline: a failure of the
/// action is captured and rethrown after the transaction ends, so the pipeline never re-runs an action whose
/// side effects may already have happened.
/// </remarks>
public sealed class ReindexJobLock(ISqlExecutionService sqlExecutionService) : IReindexJobLock
{
    private const string ResourceName = "Ignixa.Reindex.Singleton";
    private readonly ISqlExecutionService _sqlExecutionService =
        sqlExecutionService ?? throw new ArgumentNullException(nameof(sqlExecutionService));

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        ExceptionDispatchInfo? actionFailure = null;
        var result = await _sqlExecutionService.ExecuteInTransactionAsync(
            SystemConstants.GlobalTenantId,
            async (transaction, ct) =>
            {
                await AcquireAsync(transaction, ct);
                try
                {
                    return await action(ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    actionFailure = ExceptionDispatchInfo.Capture(ex);
                    return default!;
                }
            },
            cancellationToken);

        actionFailure?.Throw();
        return result;
    }

    private static async Task AcquireAsync(ISqlTransactionContext transaction, CancellationToken cancellationToken)
    {
        using var command = new SqlCommand(
            """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @DbPrincipal = 'public',
                @LockTimeout = 30000;
            SELECT @result;
            """);
        command.Parameters.AddWithValue("@resource", ResourceName);
        var result = await transaction.ExecuteReaderAsync(
            command,
            static reader => reader.GetInt32(0),
            cancellationToken);
        if (result.Single() < 0)
        {
            throw new TimeoutException("Timed out acquiring the singleton reindex job lock.");
        }
    }
}
