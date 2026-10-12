using Microsoft.Data.SqlClient;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

/// <summary>Runs a callback before each non-transactional command reaches the inner service.</summary>
internal sealed class InterceptingSqlExecutionService(ISqlExecutionService inner, Func<SqlCommand, Task> beforeCommand)
    : ISqlExecutionService
{
    public async Task<int> ExecuteNonQueryAsync(
        int tenantId,
        SqlCommand command,
        CancellationToken cancellationToken,
        SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
    {
        await beforeCommand(command);
        return await inner.ExecuteNonQueryAsync(tenantId, command, cancellationToken, idempotency);
    }

    public async Task<IReadOnlyList<T>> ExecuteReaderAsync<T>(
        int tenantId,
        SqlCommand command,
        Func<SqlDataReader, T> readRow,
        CancellationToken cancellationToken,
        SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
    {
        await beforeCommand(command);
        return await inner.ExecuteReaderAsync(tenantId, command, readRow, cancellationToken, idempotency);
    }

    public Task<T> ExecuteInTransactionAsync<T>(
        int tenantId,
        Func<ISqlTransactionContext, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken) =>
        inner.ExecuteInTransactionAsync(tenantId, work, cancellationToken);

    public Task ExecuteInTransactionAsync(
        int tenantId,
        Func<ISqlTransactionContext, CancellationToken, Task> work,
        CancellationToken cancellationToken) =>
        inner.ExecuteInTransactionAsync(tenantId, work, cancellationToken);

    /// <summary>A detached copy of a command, so its plan can be read after it ran.</summary>
    public static SqlCommand Clone(SqlCommand command)
    {
        // The SQL comes from the store under test; only its bound parameters vary.
#pragma warning disable CA2100
        var clone = new SqlCommand(command.CommandText);
#pragma warning restore CA2100
        foreach (SqlParameter parameter in command.Parameters)
        {
            clone.Parameters.Add(new SqlParameter(parameter.ParameterName, parameter.SqlDbType, parameter.Size)
            {
                Value = parameter.Value,
            });
        }

        return clone;
    }
}
