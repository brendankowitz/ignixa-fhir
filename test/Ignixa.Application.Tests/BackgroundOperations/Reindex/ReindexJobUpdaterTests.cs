using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class ReindexJobUpdaterTests
{
    [Fact]
    public async Task GivenTerminalEffectsFail_WhenCompletionIsRetried_ThenPersistedDecisionIsResumed()
    {
        var current = CreateJob();
        var repository = Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>();
        repository.GetAsync("job", 1, Arg.Any<CancellationToken>())
            .Returns(_ => current);
        repository.UpdateAsync(
                Arg.Any<BackgroundJob<ReindexJobDefinition>>(),
                1,
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                current = call.Arg<BackgroundJob<ReindexJobDefinition>>();
                return Task.CompletedTask;
            });
        var completionHook = Substitute.For<IReindexCompletionHook>();
        using var jobLock = new TestJobLock();
        var updater = new ReindexJobUpdater(repository, jobLock, completionHook);
        var attempts = 0;

        await Should.ThrowAsync<InvalidOperationException>(() => updater.TryCompleteAsync(
            "job",
            "Completed",
            (_, _) =>
            {
                attempts++;
                current.Status.ShouldBe("Completing");
                throw new InvalidOperationException("event store unavailable");
            },
            job => job.Status = "Completed",
            CancellationToken.None));

        current.Status.ShouldBe("Completing");

        var won = await updater.TryCompleteAsync(
            "job",
            "Completed",
            (_, _) =>
            {
                attempts++;
                return Task.CompletedTask;
            },
            job => job.Status = "Completed",
            CancellationToken.None);

        won.ShouldBeTrue();
        attempts.ShouldBe(2);
        current.Status.ShouldBe("Completed");
    }

    [Fact]
    public async Task GivenConcurrentTerminalDecisions_WhenCompleted_ThenOnlyOneDecisionAppendsAndPersists()
    {
        var current = CreateJob();
        var repository = Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>();
        repository.GetAsync("job", 1, Arg.Any<CancellationToken>())
            .Returns(_ => current);
        repository.UpdateAsync(
                Arg.Any<BackgroundJob<ReindexJobDefinition>>(),
                1,
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                current = call.Arg<BackgroundJob<ReindexJobDefinition>>();
                return Task.CompletedTask;
            });
        var completionHook = Substitute.For<IReindexCompletionHook>();
        using var jobLock = new TestJobLock();
        var updater = new ReindexJobUpdater(repository, jobLock, completionHook);
        var terminalCallbacks = 0;

        var completed = updater.TryCompleteAsync(
            "job",
            "Completed",
            (_, _) =>
            {
                Interlocked.Increment(ref terminalCallbacks);
                return Task.CompletedTask;
            },
            job => job.Status = "Completed",
            CancellationToken.None);
        var cancelled = updater.TryCompleteAsync(
            "job",
            "Cancelled",
            (_, _) =>
            {
                Interlocked.Increment(ref terminalCallbacks);
                return Task.CompletedTask;
            },
            job => job.Status = "Cancelled",
            CancellationToken.None);

        var results = await Task.WhenAll(completed, cancelled);

        results.Count(result => result).ShouldBe(1);
        terminalCallbacks.ShouldBe(1);
        current.Status.ShouldBeOneOf("Completed", "Cancelled");
        await repository.Received(2).UpdateAsync(
            Arg.Any<BackgroundJob<ReindexJobDefinition>>(),
            1,
            Arg.Any<CancellationToken>());
        await completionHook.Received(1).OnCompletedAsync(
            Arg.Any<BackgroundJob<ReindexJobDefinition>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenCompletionDecisionIsPersisted_WhenCancellationRaces_ThenCancellationCannotOverrideIt()
    {
        var current = CreateJob();
        var repository = Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>();
        repository.GetAsync("job", 1, Arg.Any<CancellationToken>())
            .Returns(_ => current);
        repository.UpdateAsync(
                Arg.Any<BackgroundJob<ReindexJobDefinition>>(),
                1,
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                current = call.Arg<BackgroundJob<ReindexJobDefinition>>();
                return Task.CompletedTask;
            });
        using var jobLock = new TestJobLock();
        var updater = new ReindexJobUpdater(
            repository,
            jobLock,
            Substitute.For<IReindexCompletionHook>());
        var decisionPersisted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseEffects = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var completing = updater.TryCompleteAsync(
            "job",
            "Completed",
            async (_, _) =>
            {
                decisionPersisted.SetResult();
                await releaseEffects.Task;
            },
            job => job.Status = "Completed",
            CancellationToken.None);
        await decisionPersisted.Task;
        current.Status.ShouldBe("Completing");

        var cancelling = updater.TryCompleteAsync(
            "job",
            "Cancelled",
            (_, _) => Task.CompletedTask,
            job => job.Status = "Cancelled",
            CancellationToken.None);
        releaseEffects.SetResult();

        (await completing).ShouldBeTrue();
        (await cancelling).ShouldBeFalse();
        current.Status.ShouldBe("Completed");
    }

    private static BackgroundJob<ReindexJobDefinition> CreateJob() =>
        new()
        {
            JobId = "job",
            JobType = (int)BackgroundJobType.Reindex,
            Status = "Running",
            Definition = ReindexTestHelper.CreateJobDefinition()
        };

    private sealed class TestJobLock : IReindexJobLock, IDisposable
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                return await action(cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public void Dispose() => _semaphore.Dispose();
    }
}
