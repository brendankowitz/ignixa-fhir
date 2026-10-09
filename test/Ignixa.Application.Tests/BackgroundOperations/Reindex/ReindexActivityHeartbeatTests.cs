using System.Diagnostics.Metrics;
using System.Text.Json;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.BackgroundOperations.Reindex.Activities;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class ReindexActivityHeartbeatTests
{
    [Theory]
    [InlineData(1800, 30)]
    [InlineData(20, 5)]
    public async Task GivenLongRunningWork_WhenHeartbeatStorageFailsThenRecovers_ThenWorkContinuesAndHeartbeatRetries(
        int staleSeconds, int intervalSeconds)
    {
        var time = new ControlledTimeProvider();
        var repository = Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>();
        repository.GetAsync("job", 1, Arg.Any<CancellationToken>()).Returns(_ => new BackgroundJob<ReindexJobDefinition>
        {
            JobId = "job", JobType = 4, Status = "Running", Definition = ReindexTestHelper.CreateJobDefinition()
        });
        var attempts = 0;
        var failureObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var heartbeatPersisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failedMeasurements = 0L;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Name == "reindex.progress.persistence_failures")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) =>
        {
            Interlocked.Add(ref failedMeasurements, value);
            failureObserved.TrySetResult();
        });
        listener.Start();
        repository.TryUpdateProgressAsync(Arg.Any<BackgroundJob<ReindexJobDefinition>>(), 1, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (++attempts == 1)
                {
                    return Task.FromException<bool>(new TimeoutException("storage unavailable"));
                }

                heartbeatPersisted.TrySetResult();
                return Task.FromResult(true);
            });
        var reporter = new ReindexProgressReporter(new ReindexJobUpdater(
            repository, new ForbiddenJobLock(), Substitute.For<IReindexCompletionHook>()));
        var heartbeat = new ReindexActivityHeartbeat(
            reporter,
            Options.Create(new ReindexOptions { StaleJobTimeout = TimeSpan.FromSeconds(staleSeconds) }),
            time,
            NullLogger<ReindexActivityHeartbeat>.Instance);
        var finishWork = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = heartbeat.RunAsync("job", _ => finishWork.Task, CancellationToken.None);
        try
        {
            time.Period.ShouldBe(TimeSpan.FromSeconds(intervalSeconds));
            time.Fire();
            await failureObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
            running.IsCompleted.ShouldBeFalse();
            time.Fire();
            await heartbeatPersisted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            attempts.ShouldBe(2);
            Interlocked.Read(ref failedMeasurements).ShouldBeGreaterThanOrEqualTo(1);
        }
        finally
        {
            finishWork.TrySetResult(17);
            (await running).ShouldBe(17);
        }

        time.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenProgressWriteFails_WhenDedicatedActivityRetries_ThenCommittedSnapshotIsNotDoubleCounted()
    {
        var repository = Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>();
        var current = new BackgroundJob<ReindexJobDefinition>
        {
            JobId = "job", JobType = 4, Status = "Running", Definition = ReindexTestHelper.CreateJobDefinition()
        };
        repository.GetAsync("job", 1, Arg.Any<CancellationToken>())
            .Returns(_ => JsonSerializer.Deserialize<BackgroundJob<ReindexJobDefinition>>(JsonSerializer.Serialize(current)));
        var attempts = 0;
        repository.TryUpdateProgressAsync(Arg.Any<BackgroundJob<ReindexJobDefinition>>(), 1, Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                current = call.Arg<BackgroundJob<ReindexJobDefinition>>();
                return ++attempts == 1
                    ? Task.FromException<bool>(new TimeoutException("response lost after commit"))
                    : Task.FromResult(true);
            });
        var activity = new PersistReindexProgressActivity(
            new ReindexProgressReporter(new ReindexJobUpdater(
                repository, new ForbiddenJobLock(), Substitute.For<IReindexCompletionHook>())),
            NullLogger<PersistReindexProgressActivity>.Instance);
        var input = JsonSerializer.Serialize(new[]
        {
            new PersistReindexProgressInput("job", 1, "Reindexing",
                [ReindexTenantState.Create(1) with { ResourcesToReindex = 10, ResourcesReindexed = 10 }])
        });

        await Should.ThrowAsync<DurableTask.Core.Exceptions.TaskFailureException>(() => activity.RunAsync(null!, input));
        await activity.RunAsync(null!, input);

        current.Progress!["resourcesSuccessfullyReindexed"]!.GetValue<long>().ShouldBe(10);
        attempts.ShouldBe(2);
    }

    private sealed class ForbiddenJobLock : IReindexJobLock
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Heartbeats must not acquire the singleton lock.");
    }

    private sealed class ControlledTimeProvider : TimeProvider
    {
        private TimerCallback? _callback;
        private object? _state;
        public TimeSpan Period { get; private set; }
        public bool Disposed { get; private set; }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _callback = callback;
            _state = state;
            Period = period;
            return new ControlledTimer(this);
        }

        public void Fire() => _callback!(_state);

        private sealed class ControlledTimer(ControlledTimeProvider owner) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() => owner.Disposed = true;
            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
