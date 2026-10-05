using Ignixa.DataLayer.SqlServer;
using Microsoft.Data.SqlClient;

namespace Ignixa.Api.Tests.Events;

/// <summary>
/// Fails every call, so a test can prove a failure propagates rather than being swallowed.
/// <para>
/// Hand-written rather than an NSubstitute mock because <see cref="ExecuteReaderAsync"/> is generic:
/// configuring a substitute for one type argument leaves every other instantiation returning an empty list,
/// so a test meaning "the database is unreachable" quietly becomes "the database returned nothing".
/// </para>
/// </summary>
internal sealed class ThrowingSqlExecutionService : ISqlExecutionService
{
    public Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
        int tenantId,
        SqlCommand command,
        Func<SqlDataReader, TResult> readRow,
        CancellationToken cancellationToken,
        SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
        => throw new InvalidOperationException("execution service is deliberately unusable in this test");

    public Task<int> ExecuteNonQueryAsync(int tenantId, SqlCommand command, CancellationToken cancellationToken, SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
        => throw new InvalidOperationException("execution service is deliberately unusable in this test");

    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        int tenantId,
        Func<ISqlTransactionContext, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken)
        => throw new InvalidOperationException("execution service is deliberately unusable in this test");

    public Task ExecuteInTransactionAsync(
        int tenantId,
        Func<ISqlTransactionContext, CancellationToken, Task> work,
        CancellationToken cancellationToken)
        => throw new InvalidOperationException("execution service is deliberately unusable in this test");
}
