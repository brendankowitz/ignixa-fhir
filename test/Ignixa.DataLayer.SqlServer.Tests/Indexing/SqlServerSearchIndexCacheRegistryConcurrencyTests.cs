using System.Collections.Concurrent;
using Ignixa.DataLayer.SqlServer.Indexing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Ignixa.DataLayer.SqlServer.Tests.Indexing;

public class SqlServerSearchIndexCacheRegistryConcurrencyTests
{
    [Fact]
    public async Task GivenTwoFailedWaiters_WhenTheSecondResumesAfterReplacement_ThenTheReplacementIsRetained()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sql = GatedSql(gate.Task);
        using var registry = new SqlServerSearchIndexCacheRegistry(sql, NullLoggerFactory.Instance);
        using var delayedContext = new QueuedSynchronizationContext();

        var first = registry.GetOrCreateAsync(1, CancellationToken.None);
        Task<SqlServerSearchIndexReferenceDataCache> second;
        var originalContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(delayedContext);
            second = registry.GetOrCreateAsync(1, CancellationToken.None);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }

        gate.SetException(new InvalidOperationException("First initialization failed"));
        await Should.ThrowAsync<InvalidOperationException>(() => first);
        var replacement = await registry.GetOrCreateAsync(1, CancellationToken.None);

        // Only this waiter is delayed: its failed entry has already been replaced before its catch runs.
        await delayedContext.RunOneAsync();
        await Should.ThrowAsync<InvalidOperationException>(() => second);

        var retained = await registry.GetOrCreateAsync(1, CancellationToken.None);
        retained.ShouldBeSameAs(replacement);
    }

    [Fact]
    public async Task GivenSharedInitialization_WhenOneWaiterCancels_ThenItIsReleasedAndSurvivorsKeepTheSameCache()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sql = GatedSql(gate.Task);
        using var registry = new SqlServerSearchIndexCacheRegistry(sql, NullLoggerFactory.Instance);
        using var cancellation = new CancellationTokenSource();
        var cancelledWaiter = registry.GetOrCreateAsync(1, cancellation.Token);
        var survivor = registry.GetOrCreateAsync(1, CancellationToken.None);

        try
        {
            await cancellation.CancelAsync();
            await Should.ThrowAsync<OperationCanceledException>(
                () => cancelledWaiter.WaitAsync(TimeSpan.FromSeconds(2)));

            var laterWaiter = registry.GetOrCreateAsync(1, CancellationToken.None);
            laterWaiter.IsCompleted.ShouldBeFalse();
            gate.SetResult();
            var cache = await survivor;
            (await laterWaiter).ShouldBeSameAs(cache);
            (await registry.GetOrCreateAsync(1, CancellationToken.None)).ShouldBeSameAs(cache);
        }
        finally
        {
            gate.TrySetResult();
            await survivor;
        }
    }

    private static ISqlExecutionService GatedSql(Task firstRead)
    {
        var sql = Substitute.For<ISqlExecutionService>();
        var calls = 0;
        sql.ExecuteReaderAsync(
                Arg.Any<int>(), Arg.Any<SqlCommand>(),
                Arg.Any<Func<SqlDataReader, (short Id, string Name)>>(),
                Arg.Any<CancellationToken>(), Arg.Any<SqlCommandIdempotency>())
            .Returns(async call =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    await firstRead.WaitAsync(call.Arg<CancellationToken>());
                }

                return (IReadOnlyList<(short Id, string Name)>)Array.Empty<(short Id, string Name)>();
            });
        return sql;
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _callbacks = new();
        private readonly SemaphoreSlim _ready = new(0);

        public override void Post(SendOrPostCallback d, object? state)
        {
            _callbacks.Enqueue((d, state));
            _ready.Release();
        }

        public async Task RunOneAsync()
        {
            (await _ready.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
            _callbacks.TryDequeue(out var work).ShouldBeTrue();
            work.Callback(work.State);
        }

        public void Dispose() => _ready.Dispose();
    }
}
