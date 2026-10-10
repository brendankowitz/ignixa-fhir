using System.Text.Json;
using DurableTask.Core;
using DurableTask.Core.Serializing;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.BackgroundOperations.Reindex.Activities;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.BackgroundOperations.Reindex.Orchestrations;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class ReindexOrchestrationTests
{
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
    public async Task GivenManyWaves_WhenOrchestrated_ThenProgressIsPersistedAtPhaseBoundariesAndEveryFewWaves()
    {
        var context = new ExecutingContext(rangesPerPage: 1, pages: 12);
        var input = ReindexTestHelper.CreateOrchestrationInput(
            "job", targetEventId: 42, barrierDelay: TimeSpan.Zero, tenantIds: [1]);

        var result = await new ReindexOrchestration().RunTask(context, input);

        result.Success.ShouldBeTrue();
        context.RangeCalls.ShouldBe(12);
        // Draining, Reindexing, two snapshots after five waves each, then Completing.
        context.Snapshots.Select(snapshot => snapshot.Phase).ShouldBe(
        [
            ReindexPhase.Draining,
            ReindexPhase.Reindexing,
            ReindexPhase.Reindexing,
            ReindexPhase.Reindexing,
            ReindexPhase.Completing
        ]);
        context.Snapshots[2].Tenants.Single().ResourcesReindexed.ShouldBe(50);
        context.Snapshots[3].Tenants.Single().ResourcesReindexed.ShouldBe(100);
    }

    [Fact]
    public async Task GivenJobClosedElsewhere_WhenProgressIsPersistedAtAPhaseBoundary_ThenOrchestrationStopsWithoutCompleting()
    {
        var context = new ExecutingContext(jobClosed: true);
        var input = ReindexTestHelper.CreateOrchestrationInput(
            "job", targetEventId: 42, barrierDelay: TimeSpan.Zero, tenantIds: [1]);

        var result = await new ReindexOrchestration().RunTask(context, input);

        result.Success.ShouldBeFalse();
        context.ProgressCalls.ShouldBe(1);
        context.BarrierCalls.ShouldBe(0);
        context.RangeCalls.ShouldBe(0);
        context.CompletionCalls.ShouldBe(0);
        context.ContinuationCount.ShouldBe(0);
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
    public async Task GivenDefinitionsBehindTargetForThreeWaves_WhenRangesReportNotReady_ThenTenantWaitsWithBackoffAndCompletes()
    {
        var context = new ExecutingContext(definitionsNotReadyAttempts: 6);
        var input = ReindexTestHelper.CreateOrchestrationInput(
            "job", targetEventId: 42, barrierDelay: TimeSpan.Zero, tenantIds: [1]);

        var result = await new ReindexOrchestration().RunTask(context, input);

        result.Success.ShouldBeTrue();
        context.RangeCalls.ShouldBe(9);
        context.TimerDelays.Skip(1).ShouldBe([1, 2, 4]);
        result.Tenants.Single().Success.ShouldBeTrue();
        result.Tenants.Single().ResourcesReindexed.ShouldBe(30);
    }

    [Fact]
    public async Task GivenDefinitionsNeverCatchUp_WhenStaleJobTimeoutElapses_ThenTenantFailsWithoutAnExceptionCrossingTheActivityBoundary()
    {
        var context = new ExecutingContext(definitionsNotReadyAttempts: int.MaxValue);
        var input = ReindexTestHelper.CreateOrchestrationInput(
            "job", targetEventId: 42, barrierDelay: TimeSpan.Zero, tenantIds: [1]) with
        {
            StaleJobTimeout = TimeSpan.FromSeconds(10),
            ContinueAsNewThreshold = 100
        };

        var result = await new ReindexOrchestration().RunTask(context, input);

        result.Success.ShouldBeFalse();
        var tenant = result.Tenants.Single();
        tenant.Status.ShouldBe(ReindexTenantStatus.Failed);
        tenant.ErrorMessage.ShouldContain("behind target event 42");
        tenant.FailedResourceTypes.ShouldBe(["Patient"]);
        context.TimerDelays.Skip(1).ShouldBe([1, 2, 4, 8]);
        context.CompletionCalls.ShouldBe(1);
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
    public async Task GivenContinuationAfterBarrier_WhenSerialized_ThenTypedStateRoundTripsWithReadableEnums()
    {
        var context = new ExecutingContext();
        var input = ReindexTestHelper.CreateOrchestrationInput(
            "job",
            targetEventId: 42,
            barrierDelay: TimeSpan.Zero,
            tenantIds: [1]) with
        {
            ContinueAsNewThreshold = 3
        };

        await new ReindexOrchestration().RunTask(context, input);

        var serialized = JsonDataConverter.Default.Serialize(context.LastContinuationInput);
        serialized.ShouldContain("\"Draining\"");
        serialized.ShouldNotContain("\"Status\":1");
        var roundTripped = JsonDataConverter.Default.Deserialize<ReindexOrchestrationInput>(serialized)!;
        JsonDataConverter.Default.Serialize(roundTripped.State)
            .ShouldBe(JsonDataConverter.Default.Serialize(context.LastContinuationInput!.State));
        roundTripped.State!.Phase.ShouldBe(ReindexPhase.Draining);
        var tenant = roundTripped.State.Tenants.Single();
        tenant.Status.ShouldBe(ReindexTenantStatus.Draining);
        tenant.Progress.CutoffSurrogateId.ShouldBe(30);
        tenant.Progress.CutoffTransactionId.ShouldBe(10);
        tenant.Wait.ShouldNotBeNull();
        tenant.PlannerCursor.ShouldBeNull();

        await new ReindexOrchestration().RunTask(context, roundTripped);

        context.PlanInputs.ShouldHaveSingleItem().CutoffSurrogateId.ShouldBe(30);
        context.PlanInputs.Single().StartAfterSurrogateId.ShouldBeNull();
        context.DrainInputs.ShouldHaveSingleItem().CutoffTransactionId.ShouldBe(10);
    }

    [Fact]
    public async Task GivenLongBarrierDelay_WhenOrchestrated_ThenTheDurableWaitIsSplitWithHeartbeatsBetween()
    {
        var context = new ExecutingContext();
        var input = ReindexTestHelper.CreateOrchestrationInput(
            "job", targetEventId: 42, barrierDelay: TimeSpan.FromSeconds(95), tenantIds: [1]) with
        {
            StaleJobTimeout = TimeSpan.FromSeconds(60)
        };

        await new ReindexOrchestration().RunTask(context, input);

        context.TimerDelays.Take(4).ShouldBe([30, 30, 30, 5]);
        context.Snapshots.Count(snapshot => snapshot.Phase == ReindexPhase.BarrierDelay).ShouldBe(3);
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
        var input = ReindexTestHelper.CreateOrchestrationInput(
            "job", targetEventId: 42, barrierDelay: TimeSpan.Zero, tenantIds: [1]) with
        {
            State = DrainingState(context.CurrentUtcDateTime),
            StaleJobTimeout = TimeSpan.FromSeconds(2),
            ContinueAsNewThreshold = 100
        };

        var output = await new ReindexOrchestration().RunTask(context, input);

        output.Success.ShouldBeFalse();
        output.Tenants.Single().Success.ShouldBeFalse();
        output.Tenants.Single().Status.ShouldBe(ReindexTenantStatus.Failed);
        output.Tenants.Single().ErrorMessage.ShouldContain("drain");
        context.TimerCalls.ShouldBe(2);
        context.ProgressCalls.ShouldBe(1);
    }

    [Fact]
    public async Task GivenDrainWaits_WhenPolled_ThenPollsBackOffFromOneSecondToThirtyOnDurableTimers()
    {
        var context = new ExecutingContext(drainPollsBeforeDrained: 7);
        var input = ReindexTestHelper.CreateOrchestrationInput(
            "job", targetEventId: 42, barrierDelay: TimeSpan.Zero, tenantIds: [1]) with
        {
            State = DrainingState(context.CurrentUtcDateTime),
            StaleJobTimeout = TimeSpan.FromMinutes(5),
            ContinueAsNewThreshold = 100
        };

        var output = await new ReindexOrchestration().RunTask(context, input);

        output.Success.ShouldBeTrue();
        context.DrainInputs.Count.ShouldBe(8);
        context.TimerDelays.ShouldBe([1, 2, 4, 8, 16, 30, 30]);
        context.DrainInputs.Last().DrainElapsed.ShouldBe(TimeSpan.FromSeconds(91));
    }

    [Fact]
    public async Task GivenOneTenantStillDraining_WhenAnotherReindexes_ThenTheReindexingTenantIsNotHeldByTheDrainPoll()
    {
        var context = new ExecutingContext(drainPollsBeforeDrained: 2, slowDrainTenantId: 2);
        var input = ReindexTestHelper.CreateOrchestrationInput(
            "job", targetEventId: 42, barrierDelay: TimeSpan.Zero, tenantIds: [1, 2]);

        var output = await new ReindexOrchestration().RunTask(context, input);

        output.Success.ShouldBeTrue();
        output.Tenants.Count.ShouldBe(2);
        var lastDrainOfTenant2 = context.Log.LastIndexOf("drain:2");
        var rangesOfTenant1 = context.Log.Select((entry, index) => (entry, index))
            .Where(item => item.entry == "range:1")
            .Select(item => item.index)
            .ToArray();
        rangesOfTenant1.Length.ShouldBe(3);
        rangesOfTenant1.ShouldAllBe(index => index < lastDrainOfTenant2);
        context.RangeCalls.ShouldBe(6);
    }

    private static ReindexOrchestrationState DrainingState(DateTime now) =>
        ReindexOrchestrationState.Create([1]) with
        {
            Started = true,
            Phase = ReindexPhase.Draining,
            Tenants =
            [
                ReindexTenantState.Create(1) with
                {
                    Progress = ReindexTenantProgress.Create(1) with
                    {
                        Status = ReindexTenantStatus.Draining,
                        CutoffTransactionId = 30,
                        CutoffSurrogateId = 10
                    },
                    Wait = ReindexWait.Begin(now)
                }
            ]
        };

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
        bool jobClosed = false,
        bool drainNeverCompletes = false,
        int drainPollsBeforeDrained = 0,
        int? slowDrainTenantId = null,
        bool failBarrierOnce = false,
        int definitionsNotReadyAttempts = 0,
        bool startShouldContinue = true,
        bool useBarrierCutoffForRanges = false,
        int rangesPerPage = 2,
        int pages = 2) : OrchestrationContext
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
        public List<double> TimerDelays { get; } = [];
        public List<string> Log { get; } = [];
        public List<ReindexProgress> Snapshots { get; } = [];
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
                var value when value == typeof(RaiseBarrierActivity).FullName =>
                    Barrier((RaiseBarrierInput)parameters.Single()),
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

            TimerDelays.Add((fireAt - _currentUtcDateTime).TotalSeconds);
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

        private RaiseBarrierOutput Barrier(RaiseBarrierInput input)
        {
            BarrierCalls++;
            if (failBarrierOnce && BarrierCalls == 1)
            {
                throw new InvalidOperationException("transient barrier failure");
            }

            return new RaiseBarrierOutput(input.TenantId, 10, 30);
        }

        private PlanReindexOutput Plan(PlanReindexInput input)
        {
            PlanInputs.Add(input);
            if (useBarrierCutoffForRanges)
            {
                return input.StartAfterSurrogateId is null
                    ? new PlanReindexOutput(
                        [new ReindexRange(1, 10, 10), new ReindexRange(11, 20, 10)],
                        20)
                    : input.StartAfterSurrogateId < input.CutoffSurrogateId
                        ? new PlanReindexOutput(
                            [new ReindexRange(21, input.CutoffSurrogateId, 10)],
                            null)
                        : new PlanReindexOutput([], null);
            }

            // Pages of rangesPerPage ranges of ten resources each; the last page ends the type.
            var page = (int)((input.StartAfterSurrogateId ?? 0) / (rangesPerPage * 10));
            var first = (input.StartAfterSurrogateId ?? 0) + 1;
            var ranges = Enumerable.Range(0, page == pages - 1 ? Math.Max(1, rangesPerPage - 1) : rangesPerPage)
                .Select(index => new ReindexRange(first + index * 10, first + index * 10 + 9, 10))
                .ToArray();
            return new PlanReindexOutput(ranges, page == pages - 1 ? null : ranges[^1].End);
        }

        private ReindexRangeOutput Range(ReindexRangeInput input)
        {
            RangeInputs.Add(input);
            Log.Add($"range:{input.TenantId}");
            RangeCalls++;
            if (RangeCalls <= definitionsNotReadyAttempts)
            {
                return ReindexRangeOutput.DefinitionsNotReady(41);
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
            Log.Add($"drain:{input.TenantId}");
            var polls = DrainInputs.Count(drain => drain.TenantId == input.TenantId);
            var isSlow = slowDrainTenantId is null || slowDrainTenantId == input.TenantId;
            var pending = drainNeverCompletes || isSlow && polls <= drainPollsBeforeDrained;
            return new AwaitDrainOutput(input.TenantId, !pending, 10);
        }

        private bool Progress(PersistReindexProgressInput input)
        {
            ProgressCalls++;
            if (failProgressOnce && RangeCalls > 0 && ProgressFailures == 0)
            {
                ProgressFailures++;
                throw new InvalidOperationException("progress storage unavailable");
            }

            Snapshots.Add(input.Progress);
            return !jobClosed;
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
                input.Tenants.Single().ErrorMessage.ShouldContain("definitions");
                return new CompleteReindexOutput(false, []);
            }

            var expectedPerTenant = 10L * RangeInputs
                .Where(range => range.TenantId == input.Tenants[0].TenantId)
                .Select(range => (range.StartSurrogateId, range.EndSurrogateId))
                .Distinct()
                .Count();
            foreach (var tenant in input.Tenants)
            {
                tenant.Status.ShouldBe(ReindexTenantStatus.Completed);
                tenant.ResourcesToReindex.ShouldBe(expectedPerTenant);
                tenant.ResourcesReindexed.ShouldBe(expectedPerTenant);
                tenant.Conflicts.ShouldBe(1);
            }

            return new CompleteReindexOutput(true, []);
        }
    }
}
