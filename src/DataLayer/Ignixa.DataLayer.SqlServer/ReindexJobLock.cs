using Ignixa.Domain.Abstractions;
using Microsoft.Data.SqlClient;

namespace Ignixa.DataLayer.SqlServer;

public sealed class ReindexJobLock(ISqlExecutionService sqlExecutionService) : IReindexJobLock
{
    private const int GlobalConformanceTenantId = 1;
    private const string ResourceName = "Ignixa.Reindex.Singleton";
    private readonly ISqlExecutionService _sqlExecutionService =
        sqlExecutionService ?? throw new ArgumentNullException(nameof(sqlExecutionService));

    public Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        return _sqlExecutionService.ExecuteInTransactionAsync(
            GlobalConformanceTenantId,
            async (transaction, ct) =>
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
                    ct);
                if (result.Single() < 0)
                {
                    throw new TimeoutException("Timed out acquiring the singleton reindex job lock.");
                }

                return await action(ct);
            },
            cancellationToken);
    }
}
