using DurableTask.Core;
using DurableTask.Core.Exceptions;
using DurableTask.Core.History;
using Ignixa.DataLayer.FileSystem.DurableTask;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.DataLayer.FileSystem.Tests;

public class InMemoryOrchestrationServiceTests
{
    private sealed class CountingActivity : TaskActivity<int, int>
    {
        private static int _executionCount;

        public static int ExecutionCount => Volatile.Read(ref _executionCount);

        public static void Reset() => Interlocked.Exchange(ref _executionCount, 0);

        protected override int Execute(TaskContext context, int input)
        {
            Interlocked.Increment(ref _executionCount);
            return input;
        }
    }

    private sealed class EternalTimerOrchestration : TaskOrchestration<int, int>
    {
        public override async Task<int> RunTask(OrchestrationContext context, int input)
        {
            await context.ScheduleTask<int>(typeof(CountingActivity), input);
            await context.CreateTimer(context.CurrentUtcDateTime.AddMilliseconds(50), true);
            if (input == 0)
            {
                context.ContinueAsNew(1);
            }

            return input;
        }
    }

    private sealed class TimerOrchestration : TaskOrchestration<string, string>
    {
        public override async Task<string> RunTask(OrchestrationContext context, string input)
        {
            await context.CreateTimer(context.CurrentUtcDateTime.AddMilliseconds(100), true);
            return input;
        }
    }

    [Fact]
    public async Task GivenPendingInstance_WhenCreatedWithPendingDedupeStatus_ThenItRejectsTheDuplicate()
    {
        var service = new InMemoryOrchestrationService(NullLogger<InMemoryOrchestrationService>.Instance);
        var client = new TaskHubClient(service);
        const string instanceId = "transition";

        await client.CreateOrchestrationInstanceAsync(typeof(object), instanceId, null);

        await Should.ThrowAsync<OrchestrationAlreadyExistsException>(() =>
            client.CreateOrchestrationInstanceAsync(
                typeof(object),
                instanceId,
                null,
                [OrchestrationStatus.Pending]));
    }

    [Fact]
    public async Task GivenFutureTimer_WhenCompleted_ThenItIsEnqueuedAtItsDueTime()
    {
        var service = new InMemoryOrchestrationService(NullLogger<InMemoryOrchestrationService>.Instance);
        await service.StartAsync();
        var instance = new OrchestrationInstance { InstanceId = "transition" };
        var started = new ExecutionStartedEvent(-1, null)
        {
            OrchestrationInstance = instance,
            Name = "transition",
        };
        var timer = new TaskMessage
        {
            OrchestrationInstance = instance,
            Event = new TimerCreatedEvent(1, DateTime.UtcNow.AddMilliseconds(250)),
        };

        await service.CompleteTaskOrchestrationWorkItemAsync(
            new TaskOrchestrationWorkItem { InstanceId = instance.InstanceId },
            new OrchestrationRuntimeState([started]),
            [],
            [],
            [timer],
            null!,
            new OrchestrationState { OrchestrationInstance = instance });

        (await service.LockNextTaskOrchestrationWorkItemAsync(
            TimeSpan.FromMilliseconds(50),
            CancellationToken.None)).ShouldBeNull();

        var workItem = await service.LockNextTaskOrchestrationWorkItemAsync(
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        workItem.ShouldNotBeNull();
        workItem.OrchestrationRuntimeState.Events.ShouldContain(evt => evt is ExecutionStartedEvent);
        workItem.NewMessages.Single().Event.ShouldBeOfType<TimerFiredEvent>();
    }

    [Fact]
    public async Task GivenTimerOrchestration_WhenStarted_ThenItCompletesAfterItsTimer()
    {
        var service = new InMemoryOrchestrationService(NullLogger<InMemoryOrchestrationService>.Instance);
        using var worker = new TaskHubWorker(service);
        worker.AddTaskOrchestrations(typeof(TimerOrchestration));
        await worker.StartAsync();
        try
        {
            var client = new TaskHubClient(service);
            var instance = await client.CreateOrchestrationInstanceAsync(
                typeof(TimerOrchestration),
                "timer",
                "completed");

            var state = await client.WaitForOrchestrationAsync(
                instance,
                TimeSpan.FromSeconds(2),
                CancellationToken.None);

            state.OrchestrationStatus.ShouldBe(OrchestrationStatus.Completed);
        }
        finally
        {
            await worker.StopAsync(true);
        }
    }

    [Fact]
    public async Task GivenCompletedInstance_WhenRecreatedWithTheSameId_ThenTheNewExecutionRunsFromItsOwnInput()
    {
        var service = new InMemoryOrchestrationService(NullLogger<InMemoryOrchestrationService>.Instance);
        using var worker = new TaskHubWorker(service);
        worker.AddTaskOrchestrations(typeof(TimerOrchestration));
        await worker.StartAsync();
        try
        {
            var client = new TaskHubClient(service);
            OrchestrationStatus[] activeStatuses =
                [OrchestrationStatus.Running, OrchestrationStatus.Pending, OrchestrationStatus.ContinuedAsNew];
            var first = await client.CreateOrchestrationInstanceAsync(
                typeof(TimerOrchestration),
                "transition",
                "first",
                activeStatuses);
            (await client.WaitForOrchestrationAsync(first, TimeSpan.FromSeconds(2), CancellationToken.None))
                .Output.ShouldContain("first");

            var second = await client.CreateOrchestrationInstanceAsync(
                typeof(TimerOrchestration),
                "transition",
                "second",
                activeStatuses);
            var state = await client.WaitForOrchestrationAsync(second, TimeSpan.FromSeconds(2), CancellationToken.None);

            state.OrchestrationStatus.ShouldBe(OrchestrationStatus.Completed);
            state.OrchestrationInstance.ExecutionId.ShouldBe(second.ExecutionId);
            state.Output.ShouldContain("second");
        }
        finally
        {
            await worker.StopAsync(true);
        }
    }

    [Theory]
    [InlineData("ttl-cleanup-eternal")]
    [InlineData("transaction-watcher-eternal")]
    public async Task GivenEternalTimerOrchestration_WhenContinuedAsNew_ThenItRunsTheNextCycle(string instanceId)
    {
        CountingActivity.Reset();
        var service = new InMemoryOrchestrationService(NullLogger<InMemoryOrchestrationService>.Instance);
        using var worker = new TaskHubWorker(service);
        worker.AddTaskOrchestrations(typeof(EternalTimerOrchestration));
        worker.AddTaskActivities(typeof(CountingActivity));
        await worker.StartAsync();
        try
        {
            var client = new TaskHubClient(service);
            var instance = await client.CreateOrchestrationInstanceAsync(
                typeof(EternalTimerOrchestration),
                instanceId,
                0);

            var state = await client.WaitForOrchestrationAsync(
                instance,
                TimeSpan.FromSeconds(2),
                CancellationToken.None);

            state.OrchestrationStatus.ShouldBe(OrchestrationStatus.Completed);
            CountingActivity.ExecutionCount.ShouldBe(2);
        }
        finally
        {
            await worker.StopAsync(true);
        }
    }
}
