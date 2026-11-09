using Microsoft.Data.SqlClient;
using NSubstitute;

namespace Ignixa.DataLayer.SqlServer.Tests;

/// <summary>Spec §8.5: work done under the singleton reindex job lock must not be replayed by a SQL retry.</summary>
public sealed class ReindexJobLockTests
{
    [Fact]
    public async Task GivenLockBodyFailsAfterSideEffect_WhenExecutionRetries_ThenBodyRunsOnce()
    {
        var acquired = Substitute.For<ISqlTransactionContext>();
        acquired.ExecuteReaderAsync(Arg.Any<SqlCommand>(), Arg.Any<Func<SqlDataReader, int>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<int>>([0]));
        var sql = Substitute.For<ISqlExecutionService>();
        var attempts = 0;
        // An execution service that retries the transaction once, as the transient-fault policy would.
        sql.ExecuteInTransactionAsync(Arg.Any<int>(), Arg.Any<Func<ISqlTransactionContext, CancellationToken, Task<int>>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var work = call.Arg<Func<ISqlTransactionContext, CancellationToken, Task<int>>>();
                for (; ; )
                {
                    try
                    {
                        attempts++;
                        return await work(acquired, CancellationToken.None);
                    }
                    catch (InvalidOperationException) when (attempts == 1)
                    {
                    }
                }
            });
        var sideEffects = 0;

        var failure = await Should.ThrowAsync<InvalidOperationException>(() => new ReindexJobLock(sql).ExecuteAsync<int>(
            _ =>
            {
                sideEffects++;
                throw new InvalidOperationException("failure after side effect");
            },
            CancellationToken.None));

        failure.Message.ShouldBe("failure after side effect");
        sideEffects.ShouldBe(1);
        attempts.ShouldBe(1);
    }
}
