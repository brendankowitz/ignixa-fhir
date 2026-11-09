using DurableTask.Core;
using DurableTask.Core.Exceptions;
using DurableTask.Core.History;
using Ignixa.DataLayer.FileSystem.DurableTask;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.DataLayer.FileSystem.Tests;

/// <summary>
/// The file-system provider's in-memory DurableTask host runs the reindex and transition orchestrations (spec §7,
/// §4.4): deterministic instance ids must dedupe, durable timers must fire when due, and ContinueAsNew must cycle.
/// </summary>
public class InMemoryOrchestrationServiceTests
{
    [Fact]
    public async Task GivenPendingInstance_WhenCreatedWithPendingDedupeStatus_ThenItRejectsTheDuplicate()
    {
        var client = new TaskHubClient(NewService());
        await client.CreateOrchestrationInstanceAsync(typeof(object), "transition", null);

        await Should.ThrowAsync<OrchestrationAlreadyExistsException>(() =>
            client.CreateOrchestrationInstanceAsync(typeof(object), "transition", null, [OrchestrationStatus.Pending]));
    }

    [Fact]
    public async Task GivenFutureTimer_WhenCompleted_ThenItIsEnqueuedAtItsDueTime()
    {
        var service = NewService();
        await service.StartAsync();
        var instance = new OrchestrationInstance { InstanceId = "transition" };
        var timer = new TaskMessage { OrchestrationInstance = instance, Event = new TimerCreatedEvent(1, DateTime.UtcNow.AddMilliseconds(250)) };

        await service.CompleteTaskOrchestrationWorkItemAsync(
            new TaskOrchestrationWorkItem { InstanceId = instance.InstanceId },
            new OrchestrationRuntimeState([new ExecutionStartedEvent(-1, null) { OrchestrationInstance = instance, Name = "transition" }]),
            [], [], [timer], null!, new OrchestrationState { OrchestrationInstance = instance });

        (await service.LockNextTaskOrchestrationWorkItemAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None)).ShouldBeNull();
        var workItem = (await service.LockNextTaskOrchestrationWorkItemAsync(TimeSpan.FromSeconds(1), CancellationToken.None)).ShouldNotBeNull();
        workItem.OrchestrationRuntimeState.Events.ShouldContain(evt => evt is ExecutionStartedEvent);
        workItem.NewMessages.Single().Event.ShouldBeOfType<TimerFiredEvent>();
    }

    [Fact]
    public async Task GivenCompletedInstance_WhenRecreatedWithTheSameId_ThenTheNewExecutionRunsFromItsOwnInput()
    {
        OrchestrationStatus[] active = [OrchestrationStatus.Running, OrchestrationStatus.Pending, OrchestrationStatus.ContinuedAsNew];

        await RunAsync<TimerOrchestration>(async client =>
        {
            var first = await client.CreateOrchestrationInstanceAsync(typeof(TimerOrchestration), "transition", "first", active);
            (await client.WaitForOrchestrationAsync(first, TimeSpan.FromSeconds(2), CancellationToken.None)).Output.ShouldContain("first");
            var second = await client.CreateOrchestrationInstanceAsync(typeof(TimerOrchestration), "transition", "second", active);
            var state = await client.WaitForOrchestrationAsync(second, TimeSpan.FromSeconds(2), CancellationToken.None);

            state.OrchestrationStatus.ShouldBe(OrchestrationStatus.Completed);
            state.OrchestrationInstance.ExecutionId.ShouldBe(second.ExecutionId);
            state.Output.ShouldContain("second");
        });
    }

    [Fact]
    public async Task GivenEternalTimerOrchestration_WhenContinuedAsNew_ThenItRunsTheNextCycle()
    {
        CountingActivity.Reset();

        await RunAsync<EternalTimerOrchestration>(async client =>
        {
            var instance = await client.CreateOrchestrationInstanceAsync(typeof(EternalTimerOrchestration), "transaction-watcher-eternal", 0);
            var state = await client.WaitForOrchestrationAsync(instance, TimeSpan.FromSeconds(2), CancellationToken.None);

            state.OrchestrationStatus.ShouldBe(OrchestrationStatus.Completed);
            CountingActivity.ExecutionCount.ShouldBe(2);
        });
    }

    private static InMemoryOrchestrationService NewService() => new(NullLogger<InMemoryOrchestrationService>.Instance);

    private static async Task RunAsync<TOrchestration>(Func<TaskHubClient, Task> act)
    {
        var service = NewService();
        using var worker = new TaskHubWorker(service);
        worker.AddTaskOrchestrations(typeof(TOrchestration));
        worker.AddTaskActivities(typeof(CountingActivity));
        await worker.StartAsync();
        try
        {
            await act(new TaskHubClient(service));
        }
        finally
        {
            await worker.StopAsync(true);
        }
    }

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
}
