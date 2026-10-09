using Microsoft.Data.SqlClient;

namespace Ignixa.DataLayer.SqlServer.Tests;

public sealed class ReindexJobLockTests
{
    [Fact]
    public async Task GivenLockBodyFailsAfterSideEffect_WhenExecutionRetries_ThenBodyRunsOnce()
    {
        var execution = new RetryingExecutionService();
        var jobLock = new ReindexJobLock(execution);
        var sideEffects = 0;

        Func<Task> execute = () => jobLock.ExecuteAsync<int>(
            _ =>
            {
                sideEffects++;
                throw new InvalidOperationException("failure after side effect");
            },
            CancellationToken.None);

        (await execute.ShouldThrowAsync<InvalidOperationException>())
            .Message.ShouldBe("failure after side effect");
        sideEffects.ShouldBe(1);
        execution.Attempts.ShouldBe(1);
    }

    private sealed class RetryingExecutionService : ISqlExecutionService
    {
        public int Attempts { get; private set; }

        public async Task<TResult> ExecuteInTransactionAsync<TResult>(
            int tenantId,
            Func<ISqlTransactionContext, CancellationToken, Task<TResult>> work,
            CancellationToken cancellationToken)
        {
            for (var attempt = 0; ; attempt++)
            {
                Attempts++;
                try
                {
                    return await work(new LockAcquiredTransaction(), cancellationToken);
                }
                catch (InvalidOperationException) when (attempt == 0)
                {
                }
            }
        }

        public Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
            int tenantId,
            SqlCommand command,
            Func<SqlDataReader, TResult> readRow,
            CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent) =>
            throw new NotSupportedException();

        public Task<int> ExecuteNonQueryAsync(
            int tenantId,
            SqlCommand command,
            CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent) =>
            throw new NotSupportedException();

        public Task ExecuteInTransactionAsync(
            int tenantId,
            Func<ISqlTransactionContext, CancellationToken, Task> work,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class LockAcquiredTransaction : ISqlTransactionContext
    {
        public Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
            SqlCommand command,
            Func<SqlDataReader, TResult> readRow,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TResult>>([(TResult)(object)0]);

        public Task<int> ExecuteNonQueryAsync(
            SqlCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
