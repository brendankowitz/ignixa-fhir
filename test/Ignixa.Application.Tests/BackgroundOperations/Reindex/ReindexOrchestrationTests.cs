using System.Text.Json;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Activities;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.BackgroundOperations.Reindex.Orchestrations;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class ReindexOrchestrationTests
{
    [Fact]
    public async Task GivenActivationDebounce_WhenOrchestrated_ThenDurableTimerRunsBeforeLifecycleStart()
    {
        var withoutDebounce = new ExecutingContext();
        await new ReindexOrchestration().RunTask(
            withoutDebounce,
            ReindexOrchestrationInput.CreateForTest(
                "control",
                targetEventId: 42,
                barrierDelay: TimeSpan.Zero,
                tenantIds: [1]));
        var context = new ExecutingContext();
        var input = ReindexOrchestrationInput.CreateForTest(
            "job",
            targetEventId: 42,
            barrierDelay: TimeSpan.Zero,
            tenantIds: [1]) with
        {
            StartDebounce = TimeSpan.FromSeconds(10)
        };

        await new ReindexOrchestration().RunTask(context, input);

        context.TimerCalls.ShouldBe(withoutDebounce.TimerCalls + 1);
        context.StartCalls.ShouldBe(1);
    }

    [Fact]
    public async Task GivenProgressPersistenceFails_WhenRetried_ThenOnlyProgressIsRetriedNotRanges()
    {
        var context = new ExecutingContext(failProgressOnce: true);
        var input = ReindexOrchestrationInput.CreateForTest(
            "job", targetEventId: 42, barrierDelay: TimeSpan.Zero, tenantIds: [1]);

        var result = await new ReindexOrchestration().RunTask(context, input);

        result.Success.ShouldBeTrue();
        context.ProgressFailures.ShouldBe(1);
        context.ProgressCalls.ShouldBeGreaterThan(1);
        context.RangeCalls.ShouldBe(3);
        context.Snapshots.Last().Tenants.Single().ResourcesReindexed.ShouldBe(30);
    }

    [Fact]
    public async Task GivenContinueAsNewThreshold_WhenOrchestrated_ThenResumedOutcomeMatchesUninterruptedRun()
    {
        var uninterrupted = await RunToCompletionAsync(continueAsNewThreshold: 100);
        var continued = await RunToCompletionAsync(continueAsNewThreshold: 2);

        continued.Output.Success.ShouldBe(uninterrupted.Output.Success);
        JsonSerializer.Serialize(continued.Output.Tenants)
            .ShouldBe(JsonSerializer.Serialize(uninterrupted.Output.Tenants));
        continued.Output.IgnoredLifecycleEvents.ShouldBe(
            uninterrupted.Output.IgnoredLifecycleEvents,
            ignoreOrder: false);
        continued.Context.StartCalls.ShouldBe(1);
        continued.Context.BarrierCalls.ShouldBe(1);
        continued.Context.ContinuationCount.ShouldBeGreaterThan(0);
        JsonSerializer.Serialize(continued.Context.Snapshots)
            .ShouldBe(JsonSerializer.Serialize(uninterrupted.Context.Snapshots));
    }

    [Fact]
    public async Task GivenLongBarrierDelay_WhenOrchestrated_ThenProgressHeartbeatsSplitTheDurableWait()
    {
        var context = new ExecutingContext();
        var input = ReindexOrchestrationInput.CreateForTest(
            "job", targetEventId: 42, barrierDelay: TimeSpan.FromSeconds(95), tenantIds: [1]);

        await new ReindexOrchestration().RunTask(context, input);

        context.TimerCalls.ShouldBe(4);
        context.Snapshots.Count(snapshot => snapshot.Phase == "BarrierDelay").ShouldBe(3);
        context.RangeCalls.ShouldBe(3);
    }

    [Fact]
    public async Task GivenJob_WhenOrchestrated_ThenLifecycleDelayTenantWorkAndCompletionAreOrdered()
    {
        var context = new ExecutingContext();

        var input = ReindexOrchestrationInput.CreateForTest(
            "job",
            targetEventId: 42,
            barrierDelay: TimeSpan.FromSeconds(30),
            tenantIds: [1]);

        var result = await new ReindexOrchestration().RunTask(context, input);

        result.Success.ShouldBeTrue();
        context.StartCalls.ShouldBe(1);
        context.BarrierCalls.ShouldBe(1);
        context.TimerCalls.ShouldBe(1);
        context.CompletionCalls.ShouldBe(1);
    }

    [Fact]
    public async Task GivenFailureSampleIsFull_WhenLaterResourceTypeFails_ThenEveryFailedTypeReachesCompletion()
    {
        var context = new ExecutingContext(includeResourceFailures: true);
        var input = ReindexOrchestrationInput.CreateForTest(
            "job",
            targetEventId: 42,
            barrierDelay: TimeSpan.Zero,
            tenantIds: [1]) with
        {
            ResourceTypes = ["Observation", "Patient"],
            ContinueAsNewThreshold = 100
        };

        var output = await new ReindexOrchestration().RunTask(context, input);

        output.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenStartActivityFailsAfterRetries_WhenOrchestrated_ThenJobIsFinalizedFailed()
    {
        var context = new ExecutingContext(failStart: true);
        var input = ReindexOrchestrationInput.CreateForTest(
            "job",
            targetEventId: 42,
            barrierDelay: TimeSpan.Zero,
            tenantIds: [1]);

        var output = await new ReindexOrchestration().RunTask(context, input);

        output.Success.ShouldBeFalse();
        context.CompletionCalls.ShouldBe(1);
        context.LastCompletionInput!.FailureMessage.ShouldContain("start failed");
    }

    private static async Task<(ReindexOrchestrationOutput Output, ExecutingContext Context)> RunToCompletionAsync(
        int continueAsNewThreshold)
    {
        var context = new ExecutingContext();
        var input = ReindexOrchestrationInput.CreateForTest(
            "job",
            targetEventId: 42,
            barrierDelay: TimeSpan.Zero,
            tenantIds: [1]) with
        {
            ContinueAsNewThreshold = continueAsNewThreshold
        };

        while (true)
        {
            try
            {
                var output = await new ReindexOrchestration().RunTask(context, input);
                return (output, context);
            }
            catch (ContinueAsNewException ex)
            {
                input = ex.Input;
            }
        }
    }

    private sealed class ContinueAsNewException(ReindexOrchestrationInput input) : Exception
    {
        public ReindexOrchestrationInput Input { get; } = input;
    }

    private sealed class ExecutingContext(
        bool includeResourceFailures = false,
        bool failStart = false,
        bool failProgressOnce = false) : OrchestrationContext
    {
        public int StartCalls { get; private set; }
        public int BarrierCalls { get; private set; }
        public int TimerCalls { get; private set; }
        public int CompletionCalls { get; private set; }
        public int ContinuationCount { get; private set; }
        public int ProgressCalls { get; private set; }
        public int ProgressFailures { get; private set; }
        public int RangeCalls { get; private set; }
        public List<PersistReindexProgressInput> Snapshots { get; } = [];
        public CompleteReindexInput? LastCompletionInput { get; private set; }

        public override Task<T> ScheduleTask<T>(string name, string version, params object[] parameters)
        {
            object result = name switch
            {
                var value when value == typeof(StartReindexActivity).FullName => Start(),
                var value when value == typeof(RaiseBarrierActivity).FullName => Barrier(),
                var value when value == typeof(AwaitDrainActivity).FullName =>
                    new AwaitDrainOutput(1, true, 10),
                var value when value == typeof(PlanReindexActivity).FullName =>
                    Plan((PlanReindexInput)parameters.Single()),
                var value when value == typeof(ReindexRangeActivity).FullName =>
                    Range((ReindexRangeInput)parameters.Single()),
                var value when value == typeof(CompleteReindexActivity).FullName =>
                    Complete((CompleteReindexInput)parameters.Single()),
                var value when value == typeof(PersistReindexProgressActivity).FullName =>
                    Progress((PersistReindexProgressInput)parameters.Single()),
                _ => throw new InvalidOperationException($"Unexpected activity {name}.")
            };

            return Task.FromResult((T)result);
        }

        public override Task<T> CreateTimer<T>(DateTime fireAt, T state)
        {
            TimerCalls++;
            return Task.FromResult(state);
        }

        public override Task<T> CreateTimer<T>(
            DateTime fireAt,
            T state,
            CancellationToken cancellationToken) => CreateTimer(fireAt, state);

        public override Task<T> CreateSubOrchestrationInstance<T>(
            string name,
            string version,
            object input) => throw new NotSupportedException();

        public override Task<T> CreateSubOrchestrationInstance<T>(
            string name,
            string version,
            string instanceId,
            object input) => throw new NotSupportedException();

        public override Task<T> CreateSubOrchestrationInstance<T>(
            string name,
            string version,
            string instanceId,
            object input,
            IDictionary<string, string> tags) => throw new NotSupportedException();

        public override void SendEvent(
            OrchestrationInstance orchestrationInstance,
            string eventName,
            object eventData) => throw new NotSupportedException();

        public override void ContinueAsNew(object input)
        {
            ContinuationCount++;
            throw new ContinueAsNewException((ReindexOrchestrationInput)input);
        }

        public override void ContinueAsNew(string newVersion, object input) => ContinueAsNew(input);

        private StartReindexOutput Start()
        {
            StartCalls++;
            if (failStart)
            {
                throw new InvalidOperationException("start failed");
            }

            return new StartReindexOutput([]);
        }

        private RaiseBarrierOutput Barrier()
        {
            BarrierCalls++;
            return new RaiseBarrierOutput(1, 10, 30);
        }

        private static PlanReindexOutput Plan(PlanReindexInput input) =>
            input.StartAfterSurrogateId < 0
                ? new PlanReindexOutput(
                    [new ReindexRange(1, 10, 10), new ReindexRange(11, 20, 10)],
                    20)
                : new PlanReindexOutput([new ReindexRange(21, 30, 10)], null);

        private ReindexRangeOutput Range(ReindexRangeInput input)
        {
            RangeCalls++;
            if (!includeResourceFailures)
            {
                return new(10, 10, input.StartSurrogateId == 11 ? 1 : 0, []);
            }

            var count = input.ResourceType == "Observation" ? 101 : 1;
            return new ReindexRangeOutput(
                10,
                10 - count,
                0,
                Enumerable.Range(0, count)
                    .Select(index => new ReindexFailedResource(
                        input.ResourceType,
                        $"{input.ResourceType}-{index}",
                        "extraction failed"))
                    .ToArray());
        }

        private bool Progress(PersistReindexProgressInput input)
        {
            ProgressCalls++;
            if (failProgressOnce && RangeCalls > 0 && ProgressFailures == 0)
            {
                ProgressFailures++;
                throw new InvalidOperationException("progress storage unavailable");
            }

            Snapshots.Add(input);
            return true;
        }

        private CompleteReindexOutput Complete(CompleteReindexInput input)
        {
            CompletionCalls++;
            LastCompletionInput = input;
            if (input.FailureMessage is not null)
            {
                return new CompleteReindexOutput(false, []);
            }

            if (includeResourceFailures)
            {
                input.Tenants.Single().FailedResourceTypes.ShouldBe(
                    ["Observation", "Patient"],
                    ignoreOrder: true);
                input.Tenants.Single().FailedResources.Count.ShouldBe(100);
                input.Tenants.Single().FailedResourceCount.ShouldBe(306);
                return new CompleteReindexOutput(false, []);
            }

            input.Tenants.Single().ResourcesToReindex.ShouldBe(30);
            input.Tenants.Single().ResourcesReindexed.ShouldBe(30);
            input.Tenants.Single().Conflicts.ShouldBe(1);
            return new CompleteReindexOutput(true, []);
        }
    }
}
