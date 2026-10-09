using System.Text.Json;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex;
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
            ReindexTestHelper.CreateOrchestrationInput(
                "control",
                targetEventId: 42,
                barrierDelay: TimeSpan.Zero,
                tenantIds: [1]));
        var context = new ExecutingContext();
        var input = ReindexTestHelper.CreateOrchestrationInput(
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
        var input = ReindexTestHelper.CreateOrchestrationInput(
            "job", targetEventId: 42, barrierDelay: TimeSpan.Zero, tenantIds: [1]);

        var result = await new ReindexOrchestration().RunTask(context, input);

        result.Success.ShouldBeTrue();
        context.ProgressFailures.ShouldBe(1);
        context.ProgressCalls.ShouldBeGreaterThan(1);
        context.RangeCalls.ShouldBe(3);
        context.Snapshots.Last().Tenants.Single().ResourcesReindexed.ShouldBe(30);
    }

    [Fact]
    public async Task GivenBarrierActivityThrowsTransiently_WhenRetried_ThenTenantCompletes()
    {
        var context = new ExecutingContext(failBarrierOnce: true);
        var input = ReindexTestHelper.CreateOrchestrationInput(
            "job", targetEventId: 42, barrierDelay: TimeSpan.Zero, tenantIds: [1]);

        var result = await new ReindexOrchestration().RunTask(context, input);

        result.Success.ShouldBeTrue();
        context.BarrierCalls.ShouldBe(2);
        result.Tenants.Single().Success.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenDefinitionsRemainBehindForFiveAttempts_WhenRangeIsRetried_ThenTenantCompletes()
    {
        var context = new ExecutingContext(definitionsNotReadyAttempts: 6);
        var input = ReindexTestHelper.CreateOrchestrationInput(
            "job", targetEventId: 42, barrierDelay: TimeSpan.Zero, tenantIds: [1]);

        var result = await new ReindexOrchestration().RunTask(context, input);

        result.Success.ShouldBeTrue();
        context.RangeCalls.ShouldBe(9);
        result.Tenants.Single().Success.ShouldBeTrue();
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
    public async Task GivenContinueAsNew_WhenRequested_ThenCurrentStateIsCarriedAndNoMoreWorkIsScheduled()
    {
        var context = new ExecutingContext();
        var input = ReindexTestHelper.CreateOrchestrationInput(
            "job",
            targetEventId: 42,
            barrierDelay: TimeSpan.Zero,
            tenantIds: [1]) with
        {
            ContinueAsNewThreshold = 1
        };

        await new ReindexOrchestration().RunTask(context, input);

        context.ContinuationCount.ShouldBe(1);
        context.LastContinuationInput.ShouldNotBeNull();
        context.LastContinuationInput.State!.Started.ShouldBeTrue();
        context.BarrierCalls.ShouldBe(0);
        context.ProgressCalls.ShouldBe(0);
        context.CompletionCalls.ShouldBe(0);
    }

    [Fact]
    public async Task GivenBarrierCutoffs_WhenOrchestrated_ThenPlanningRangesAndDrainingUseTheCorrectWatermarks()
    {
        var context = new ExecutingContext(useBarrierCutoffForRanges: true);
        var input = ReindexTestHelper.CreateOrchestrationInput(
            "job",
            targetEventId: 42,
            barrierDelay: TimeSpan.Zero,
            tenantIds: [1]);

        await new ReindexOrchestration().RunTask(context, input);

        context.PlanInputs.ShouldAllBe(plan => plan.CutoffSurrogateId == 30);
        context.RangeInputs.ShouldAllBe(range => range.EndSurrogateId <= 30);
        context.RangeInputs.Select(range => range.EndSurrogateId).ShouldContain(30);
        context.DrainInputs.ShouldHaveSingleItem().CutoffTransactionId.ShouldBe(10);
    }

    [Fact]
    public async Task GivenContinuationAfterBarrier_WhenSerialized_ThenBarrierCutoffsRoundTrip()
    {
        var context = new ExecutingContext();
        var input = ReindexTestHelper.CreateOrchestrationInput(
            "job",
            targetEventId: 42,
            barrierDelay: TimeSpan.Zero,
            tenantIds: [1]) with
        {
            ContinueAsNewThreshold = 2
        };

        await new ReindexOrchestration().RunTask(context, input);

        var roundTripped = JsonSerializer.Deserialize<ReindexOrchestrationInput>(
            JsonSerializer.Serialize(context.LastContinuationInput))!;
        var tenant = roundTripped.State!.Tenants.Single();
        tenant.CutoffSurrogateId.ShouldBe(30);
        tenant.CutoffTransactionId.ShouldBe(10);
    }

    [Fact]
    public async Task GivenLongBarrierDelay_WhenOrchestrated_ThenProgressHeartbeatsSplitTheDurableWait()
    {
        var context = new ExecutingContext();
        var input = ReindexTestHelper.CreateOrchestrationInput(
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

        var input = ReindexTestHelper.CreateOrchestrationInput(
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
        var input = ReindexTestHelper.CreateOrchestrationInput(
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
        var input = ReindexTestHelper.CreateOrchestrationInput(
            "job",
            targetEventId: 42,
            barrierDelay: TimeSpan.Zero,
            tenantIds: [1]);

        var output = await new ReindexOrchestration().RunTask(context, input);

        output.Success.ShouldBeFalse();
        context.CompletionCalls.ShouldBe(1);
        context.LastCompletionInput!.FailureMessage.ShouldContain("start failed");
    }

    [Fact]
    public async Task GivenStartActivityFindsClosedJob_WhenOrchestrated_ThenNoTenantOrCompletionWorkRuns()
    {
        var context = new ExecutingContext(startShouldContinue: false);
        var input = ReindexTestHelper.CreateOrchestrationInput(
            "job",
            targetEventId: 42,
            barrierDelay: TimeSpan.Zero,
            tenantIds: [1]);

        var output = await new ReindexOrchestration().RunTask(context, input);

        output.Success.ShouldBeFalse();
        context.BarrierCalls.ShouldBe(0);
        context.RangeCalls.ShouldBe(0);
        context.CompletionCalls.ShouldBe(0);
    }

    [Fact]
    public async Task GivenDrainNeverCompletes_WhenStaleJobTimeoutElapses_ThenTenantFails()
    {
        var context = new ExecutingContext(drainNeverCompletes: true);
        var state = ReindexOrchestrationState.Create([1]) with
        {
            Started = true,
            BarrierDelayCompleted = true,
            Tenants =
            [
                ReindexTenantState.Create(1) with
                {
                    Phase = "Draining",
                    CutoffTransactionId = 30,
                    CutoffSurrogateId = 10,
                    DrainStartedUtc = context.CurrentUtcDateTime
                }
            ]
        };
        var input = ReindexTestHelper.CreateOrchestrationInput(
            "job", targetEventId: 42, barrierDelay: TimeSpan.Zero, tenantIds: [1]) with
        {
            State = state,
            StaleJobTimeout = TimeSpan.FromSeconds(2),
            ContinueAsNewThreshold = 100
        };

        var output = await new ReindexOrchestration().RunTask(context, input);

        output.Success.ShouldBeFalse();
        output.Tenants.Single().Success.ShouldBeFalse();
        output.Tenants.Single().ErrorMessage.ShouldContain("drain");
        context.TimerCalls.ShouldBe(2);
    }

    private static async Task<(ReindexOrchestrationOutput Output, ExecutingContext Context)> RunToCompletionAsync(
        int continueAsNewThreshold)
    {
        var context = new ExecutingContext();
        var input = ReindexTestHelper.CreateOrchestrationInput(
            "job",
            targetEventId: 42,
            barrierDelay: TimeSpan.Zero,
            tenantIds: [1]) with
        {
            ContinueAsNewThreshold = continueAsNewThreshold
        };

        while (true)
        {
            var continuationCount = context.ContinuationCount;
            var output = await new ReindexOrchestration().RunTask(context, input);
            if (context.ContinuationCount == continuationCount)
            {
                return (output, context);
            }

            input = context.LastContinuationInput!;
        }
    }

    private sealed class ExecutingContext(
        bool includeResourceFailures = false,
        bool failStart = false,
        bool failProgressOnce = false,
        bool drainNeverCompletes = false,
        bool failBarrierOnce = false,
        int definitionsNotReadyAttempts = 0,
        bool startShouldContinue = true,
        bool useBarrierCutoffForRanges = false) : OrchestrationContext
    {
        private DateTime _currentUtcDateTime = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

        public int StartCalls { get; private set; }
        public int BarrierCalls { get; private set; }
        public int TimerCalls { get; private set; }
        public int CompletionCalls { get; private set; }
        public int ContinuationCount { get; private set; }
        public int ProgressCalls { get; private set; }
        public int ProgressFailures { get; private set; }
        public int RangeCalls { get; private set; }
        public List<PersistReindexProgressInput> Snapshots { get; } = [];
        public List<PlanReindexInput> PlanInputs { get; } = [];
        public List<ReindexRangeInput> RangeInputs { get; } = [];
        public List<AwaitDrainInput> DrainInputs { get; } = [];
        public CompleteReindexInput? LastCompletionInput { get; private set; }
        public ReindexOrchestrationInput? LastContinuationInput { get; private set; }

        public override DateTime CurrentUtcDateTime => _currentUtcDateTime;

        public override Task<T> ScheduleTask<T>(string name, string version, params object[] parameters)
        {
            object result = name switch
            {
                var value when value == typeof(StartReindexActivity).FullName => Start(),
                var value when value == typeof(RaiseBarrierActivity).FullName => Barrier(),
                var value when value == typeof(AwaitDrainActivity).FullName =>
                    Drain((AwaitDrainInput)parameters.Single()),
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
            if (drainNeverCompletes && TimerCalls > 10)
            {
                throw new InvalidOperationException("The drain did not complete.");
            }

            _currentUtcDateTime = fireAt;
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
            LastContinuationInput = (ReindexOrchestrationInput)input;
        }

        public override void ContinueAsNew(string newVersion, object input) => ContinueAsNew(input);

        private StartReindexOutput Start()
        {
            StartCalls++;
            if (failStart)
            {
                throw new InvalidOperationException("start failed");
            }

            return new StartReindexOutput([])
            {
                ShouldContinue = startShouldContinue
            };
        }

        private RaiseBarrierOutput Barrier()
        {
            BarrierCalls++;
            if (failBarrierOnce && BarrierCalls == 1)
            {
                throw new InvalidOperationException("transient barrier failure");
            }

            return new RaiseBarrierOutput(1, 10, 30);
        }

        private PlanReindexOutput Plan(PlanReindexInput input)
        {
            PlanInputs.Add(input);
            if (useBarrierCutoffForRanges)
            {
                return input.StartAfterSurrogateId < input.CutoffSurrogateId
                    ? input.StartAfterSurrogateId < 0
                        ? new PlanReindexOutput(
                            [new ReindexRange(1, 10, 10), new ReindexRange(11, 20, 10)],
                            20)
                        : new PlanReindexOutput(
                            [new ReindexRange(21, input.CutoffSurrogateId, 10)],
                            null)
                    : new PlanReindexOutput([], null);
            }

            return input.StartAfterSurrogateId < 0
                ? new PlanReindexOutput(
                    [new ReindexRange(1, 10, 10), new ReindexRange(11, 20, 10)],
                    20)
                : new PlanReindexOutput([new ReindexRange(21, 30, 10)], null);
        }

        private ReindexRangeOutput Range(ReindexRangeInput input)
        {
            RangeInputs.Add(input);
            RangeCalls++;
            if (RangeCalls <= definitionsNotReadyAttempts)
            {
                throw new ReindexDefinitionsNotReadyException(41, 42);
            }

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

        private AwaitDrainOutput Drain(AwaitDrainInput input)
        {
            DrainInputs.Add(input);
            return new AwaitDrainOutput(1, !drainNeverCompletes, 10);
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

            if (drainNeverCompletes)
            {
                input.Tenants.Single().Success.ShouldBeFalse();
                input.Tenants.Single().ErrorMessage.ShouldContain("drain");
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

            if (definitionsNotReadyAttempts > 0 && input.Tenants.Single().Success is false)
            {
                return new CompleteReindexOutput(false, []);
            }

            input.Tenants.Single().ResourcesToReindex.ShouldBe(30);
            input.Tenants.Single().ResourcesReindexed.ShouldBe(30);
            input.Tenants.Single().Conflicts.ShouldBe(1);
            return new CompleteReindexOutput(true, []);
        }
    }
}
