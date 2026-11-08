using Ignixa.Domain.Abstractions;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

/// <summary>Serializes lock bodies like the SQL application lock, and counts acquisitions.</summary>
internal sealed class TestReindexJobLock : IReindexJobLock
{
    private readonly Lock _gate = new();
    private Task _tail = Task.CompletedTask;
    private int _acquisitions;

    public int Acquisitions => Volatile.Read(ref _acquisitions);

    /// <summary>Runs after the lock is acquired and before the body, so a test can race the body.</summary>
    public Func<Task>? OnAcquired { get; set; }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _acquisitions);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task previous;
        lock (_gate)
        {
            previous = _tail;
            _tail = release.Task;
        }

        try
        {
            await previous.WaitAsync(cancellationToken);
            if (OnAcquired is { } onAcquired)
            {
                await onAcquired();
            }

            return await action(cancellationToken);
        }
        finally
        {
            release.SetResult();
        }
    }
}
