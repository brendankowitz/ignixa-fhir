using System.Text.Json;
using DurableTask.Core;
using DurableTask.Core.Serializing;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.BackgroundOperations.Reindex.Activities;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.BackgroundOperations.Reindex.Orchestrations;
using Ignixa.Domain.Models;
using Shouldly;
using static Ignixa.Application.Tests.BackgroundOperations.Reindex.ReindexTestHelper;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class ReindexOrchestrationTests
{
    [Fact]
    public async Task GivenJob_WhenOrchestrated_ThenEveryActivityIsScheduledWithRetryAndEveryRangeIsCounted()
    {
        var context = new FakeContext();

        var result = await new ReindexOrchestration().RunTask(context, CreateOrchestrationInput());

        result.Success.ShouldBeTrue();
        context.DirectSchedules.ShouldBeEmpty();
        var tenant = context.Completion.Tenants.ShouldHaveSingleItem();
        tenant.Status.ShouldBe(ReindexTenantStatus.Completed);
        (tenant.ResourcesToReindex, tenant.ResourcesReindexed, tenant.Conflicts).ShouldBe((30L, 30L, 1L));
    }

    // Spec 8.2: progress is persisted outside the range failure boundary, so retrying it never reruns a range.
    [Fact]
    public async Task GivenProgressPersistenceFails_WhenRetried_ThenOnlyProgressIsRetriedNotRanges()
    {
        var context = new FakeContext();
        var snapshotsAfterRanges = 0;
        context.Progress = _ => context.RangeAttempts > 0 && snapshotsAfterRanges++ == 0 ? throw ProgressUnavailable() : true;

        var result = await new ReindexOrchestration().RunTask(context, CreateOrchestrationInput());

        result.Success.ShouldBeTrue();
        snapshotsAfterRanges.ShouldBeGreaterThan(1);
        context.RangeAttempts.ShouldBe(3);
        context.Inputs<PersistReindexProgressInput>().Last().Progress.Tenants.Single().ResourcesReindexed.ShouldBe(30);
    }

    // Spec 8.2: a snapshot that still cannot be written fails the job visibly and never reruns a range,
    // whichever phase boundary it is written at.
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 3)]
    public async Task GivenProgressCannotBePersistedAfterItsRetries_WhenOrchestrated_ThenTheJobIsFinalizedFailed(
        bool failOnlyAfterRanges,
        int expectedRangeAttempts)
    {
        var context = new FakeContext();
        context.Progress = _ => !failOnlyAfterRanges || context.RangeAttempts > 0 ? throw ProgressUnavailable() : true;

        var output = await new ReindexOrchestration().RunTask(context, CreateOrchestrationInput());

        output.Success.ShouldBeFalse();
        context.Completion.FailureMessage.ShouldContain("progress storage unavailable");
        context.RangeAttempts.ShouldBe(expectedRangeAttempts);
    }

    // Spec 8.6: a range activity that fails after its retries fails only that (tenant, type); the other
    // tenant still completes and the job ends Failed.
    [Fact]
    public async Task GivenARangeActivityFailsAfterItsRetries_WhenOrchestrated_ThenOnlyThatTenantsTypeFailsAndTheOtherTenantCompletes()
    {
        var context = new FakeContext();
        context.Range = range => range.TenantId == 2
            ? throw new InvalidOperationException("range worker crashed")
            : FakeContext.HealthyRange(range);

        var output = await new ReindexOrchestration().RunTask(context, CreateOrchestrationInput(tenantIds: [1, 2]));

        output.Success.ShouldBeFalse();
        var tenants = context.Completion.Tenants;
        var healthy = tenants.Single(tenant => tenant.TenantId == 1);
        healthy.Success.ShouldBeTrue();
        healthy.ResourcesReindexed.ShouldBe(30);
        var failed = tenants.Single(tenant => tenant.TenantId == 2);
        failed.Status.ShouldBe(ReindexTenantStatus.Completed);
        failed.Success.ShouldBeFalse();
        failed.FailedResourceTypes.ShouldBe(["Patient"]);
        failed.FailedResources.Count.ShouldBe(3);
        failed.FailedResources.ShouldAllBe(failure =>
            failure.ResourceType == "Patient" && failure.Reason.Contains("range worker crashed"));
    }

    // Spec 8.5: a job closed elsewhere is authoritative, whether start or the first phase boundary finds it.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenJobClosedElsewhere_WhenStartOrAPhaseBoundaryFindsIt_ThenOrchestrationStopsWithoutCompleting(bool foundByStart)
    {
        var context = new FakeContext { Start = () => new(!foundByStart, []), Progress = _ => false };

        var result = await new ReindexOrchestration().RunTask(context, CreateOrchestrationInput());

        result.Success.ShouldBeFalse();
        context.Scheduled.ShouldBe(foundByStart
            ? [nameof(StartReindexInput)]
            : [nameof(StartReindexInput), nameof(PersistReindexProgressInput)]);
        context.Continuations.ShouldBeEmpty();
    }

    // M5: workers whose definitions are behind the target report it as data; the tenant backs off and retries.
    [Fact]
    public async Task GivenDefinitionsBehindTargetForThreeWaves_WhenRangesReportNotReady_ThenTenantWaitsWithBackoffAndCompletes()
    {
        var context = new FakeContext();
        context.Range = range => context.RangeAttempts <= 6
            ? ReindexRangeOutput.DefinitionsNotReady(41)
            : FakeContext.HealthyRange(range);

        var result = await new ReindexOrchestration().RunTask(context, CreateOrchestrationInput());

        result.Success.ShouldBeTrue();
        context.RangeAttempts.ShouldBe(9);
        context.TimerDelays.Skip(1).ShouldBe([1, 2, 4]);
        result.Tenants.Single().Success.ShouldBeTrue();
        result.Tenants.Single().ResourcesReindexed.ShouldBe(30);
    }

    [Fact]
    public async Task GivenDefinitionsNeverCatchUp_WhenStaleJobTimeoutElapses_ThenTenantFailsWithoutAnExceptionCrossingTheActivityBoundary()
    {
        var context = new FakeContext { Range = _ => ReindexRangeOutput.DefinitionsNotReady(41) };
        var input = CreateOrchestrationInput() with { StaleJobTimeout = TimeSpan.FromSeconds(10), ContinueAsNewThreshold = 100 };

        var result = await new ReindexOrchestration().RunTask(context, input);

        result.Success.ShouldBeFalse();
        var tenant = result.Tenants.Single();
        tenant.Status.ShouldBe(ReindexTenantStatus.Failed);
        tenant.ErrorMessage.ShouldContain("behind target event 42");
        tenant.FailedResourceTypes.ShouldBe(["Patient"]);
        context.TimerDelays.Skip(1).ShouldBe([1, 2, 4, 8]);
        context.Inputs<CompleteReindexInput>().ShouldHaveSingleItem();
    }

    // B1: continuing as new is invisible in the outcome and in every persisted snapshot.
    [Fact]
    public async Task GivenContinueAsNewThreshold_WhenOrchestrated_ThenResumedOutcomeMatchesUninterruptedRun()
    {
        var uninterrupted = await RunToCompletionAsync(continueAsNewThreshold: 100);
        var continued = await RunToCompletionAsync(continueAsNewThreshold: 2);

        continued.Output.Success.ShouldBe(uninterrupted.Output.Success);
        JsonSerializer.Serialize(continued.Output.Tenants).ShouldBe(JsonSerializer.Serialize(uninterrupted.Output.Tenants));
        continued.Output.IgnoredLifecycleEvents.ShouldBe(uninterrupted.Output.IgnoredLifecycleEvents, ignoreOrder: false);
        continued.Context.Inputs<StartReindexInput>().Count.ShouldBe(1);
        continued.Context.Inputs<RaiseBarrierInput>().Count.ShouldBe(1);
        continued.Context.Continuations.ShouldNotBeEmpty();
        JsonSerializer.Serialize(continued.Context.Inputs<PersistReindexProgressInput>())
            .ShouldBe(JsonSerializer.Serialize(uninterrupted.Context.Inputs<PersistReindexProgressInput>()));
    }

    [Fact]
    public async Task GivenContinueAsNew_WhenRequested_ThenCurrentStateIsCarriedAndNoMoreWorkIsScheduled()
    {
        var context = new FakeContext();

        await new ReindexOrchestration().RunTask(context, CreateOrchestrationInput() with { ContinueAsNewThreshold = 1 });

        context.Continuations.ShouldHaveSingleItem().State!.Started.ShouldBeTrue();
        context.Scheduled.ShouldBe([nameof(StartReindexInput)]);
    }

    [Fact]
    public async Task GivenBarrierDelayAndCutoffs_WhenOrchestrated_ThenTheBarrierWaitsAndPlanningRangesAndDrainingUseTheCorrectWatermarks()
    {
        var context = new FakeContext
        {
            // Two ranges, then one more that ends at the barrier's surrogate cutoff.
            Plan = plan => plan.StartAfterSurrogateId switch
            {
                null => new([new SurrogateIdRange(1, 10, 10), new SurrogateIdRange(11, 20, 10)], 20),
                var after when after < plan.CutoffSurrogateId => new([new SurrogateIdRange(21, plan.CutoffSurrogateId, 10)], null),
                _ => new([], null)
            }
        };

        await new ReindexOrchestration().RunTask(context, CreateOrchestrationInput(barrierDelay: TimeSpan.FromSeconds(30)));

        context.TimerDelays[0].ShouldBe(30);
        context.Inputs<PlanReindexInput>().ShouldAllBe(plan => plan.CutoffSurrogateId == 30);
        var ranges = context.Inputs<ReindexRangeInput>();
        ranges.ShouldAllBe(range => range.EndSurrogateId <= 30);
        ranges.Select(range => range.EndSurrogateId).ShouldContain(30);
        context.Inputs<AwaitDrainInput>().ShouldHaveSingleItem().CutoffTransactionId.ShouldBe(10);
    }

    [Fact]
    public async Task GivenContinuationAfterBarrier_WhenSerialized_ThenTypedStateRoundTripsWithReadableEnums()
    {
        var context = new FakeContext();
        await new ReindexOrchestration().RunTask(context, CreateOrchestrationInput() with { ContinueAsNewThreshold = 3 });
        var continuation = context.Continuations.ShouldHaveSingleItem();

        var serialized = JsonDataConverter.Default.Serialize(continuation);
        var roundTripped = JsonDataConverter.Default.Deserialize<ReindexOrchestrationInput>(serialized)!;
        await new ReindexOrchestration().RunTask(context, roundTripped);

        serialized.ShouldContain("\"Draining\"");
        serialized.ShouldNotContain("\"Status\":1");
        JsonDataConverter.Default.Serialize(roundTripped.State).ShouldBe(JsonDataConverter.Default.Serialize(continuation.State));
        roundTripped.State!.Phase.ShouldBe(ReindexPhase.Draining);
        var tenant = roundTripped.State.Tenants.Single();
        tenant.Status.ShouldBe(ReindexTenantStatus.Draining);
        (tenant.Progress.CutoffSurrogateId, tenant.Progress.CutoffTransactionId).ShouldBe((30L, 10L));
        tenant.Wait.ShouldNotBeNull();
        tenant.PlannerCursor.ShouldBeNull();
        var plan = context.Inputs<PlanReindexInput>().ShouldHaveSingleItem();
        (plan.CutoffSurrogateId, plan.StartAfterSurrogateId).ShouldBe((30L, (long?)null));
        context.Inputs<AwaitDrainInput>().ShouldHaveSingleItem().CutoffTransactionId.ShouldBe(10);
    }

    // F11: the failure sample is capped, but every failed type and the full count still reach completion.
    [Fact]
    public async Task GivenFailureSampleIsFull_WhenLaterResourceTypeFails_ThenEveryFailedTypeReachesCompletion()
    {
        var context = new FakeContext
        {
            Range = range =>
            {
                var failures = range.ResourceType == "Observation" ? 101 : 1;
                return new(10 - failures, 0, Enumerable.Range(0, failures)
                    .Select(index => new ReindexFailedResource(range.ResourceType, $"{range.ResourceType}-{index}", "extraction failed"))
                    .ToArray());
            }
        };
        var input = CreateOrchestrationInput() with { ResourceTypes = ["Observation", "Patient"], ContinueAsNewThreshold = 100 };

        var output = await new ReindexOrchestration().RunTask(context, input);

        output.Success.ShouldBeFalse();
        var tenant = context.Completion.Tenants.Single();
        tenant.FailedResourceTypes.ShouldBe(["Observation", "Patient"], ignoreOrder: true);
        tenant.FailedResources.Count.ShouldBe(ReindexTenantProgress.FailedResourceSampleSize);
        tenant.FailedResourceCount.ShouldBe(306);
    }

    [Fact]
    public async Task GivenStartActivityFailsAfterRetries_WhenOrchestrated_ThenJobIsFinalizedFailed()
    {
        var context = new FakeContext { Start = () => throw new InvalidOperationException("start failed") };

        var output = await new ReindexOrchestration().RunTask(context, CreateOrchestrationInput());

        output.Success.ShouldBeFalse();
        context.Completion.FailureMessage.ShouldContain("start failed");
    }

    [Fact]
    public async Task GivenDrainNeverCompletes_WhenStaleJobTimeoutElapses_ThenTenantFails()
    {
        var context = new FakeContext { Drain = _ => false };
        var input = CreateOrchestrationInput() with
        {
            State = DrainingState(context.CurrentUtcDateTime),
            StaleJobTimeout = TimeSpan.FromSeconds(2),
            ContinueAsNewThreshold = 100
        };

        var output = await new ReindexOrchestration().RunTask(context, input);

        output.Success.ShouldBeFalse();
        var tenant = output.Tenants.Single();
        tenant.Status.ShouldBe(ReindexTenantStatus.Failed);
        tenant.ErrorMessage.ShouldContain("drain");
        context.TimerDelays.Count.ShouldBe(2);
        context.Inputs<PersistReindexProgressInput>().Count.ShouldBe(1);
    }

    [Fact]
    public async Task GivenOneTenantStillDraining_WhenAnotherReindexes_ThenTheReindexingTenantIsNotHeldByTheDrainPoll()
    {
        var context = new FakeContext();
        context.Drain = drain => drain.TenantId != 2 || context.Inputs<AwaitDrainInput>().Count(poll => poll.TenantId == 2) > 2;

        var output = await new ReindexOrchestration().RunTask(context, CreateOrchestrationInput(tenantIds: [1, 2]));

        output.Success.ShouldBeTrue();
        output.Tenants.Count.ShouldBe(2);
        var lastDrainOfTenant2 = context.Activities.FindLastIndex(activity => activity is AwaitDrainInput { TenantId: 2 });
        var rangesOfTenant1 = context.Activities
            .Select((activity, index) => (activity, index))
            .Where(item => item.activity is ReindexRangeInput { TenantId: 1 })
            .Select(item => item.index)
            .ToArray();
        rangesOfTenant1.Length.ShouldBe(3);
        rangesOfTenant1.ShouldAllBe(index => index < lastDrainOfTenant2);
        context.RangeAttempts.ShouldBe(6);
    }

    private static InvalidOperationException ProgressUnavailable() => new("progress storage unavailable");

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

    private static async Task<(ReindexOrchestrationOutput Output, FakeContext Context)> RunToCompletionAsync(
        int continueAsNewThreshold)
    {
        var context = new FakeContext();
        var input = CreateOrchestrationInput() with { ContinueAsNewThreshold = continueAsNewThreshold };
        while (true)
        {
            var continuations = context.Continuations.Count;
            var output = await new ReindexOrchestration().RunTask(context, input);
            if (context.Continuations.Count == continuations)
            {
                return (output, context);
            }

            input = context.Continuations[^1];
        }
    }

    /// <summary>
    /// Runs each activity in-process through a replaceable handler and records every attempt's input in order.
    /// The defaults are a healthy job: the barrier cuts at transaction 10 / surrogate 30, the drain is done, and
    /// each resource type plans three ranges of ten resources over two pages, one range with a conflict.
    /// </summary>
    private sealed class FakeContext : OrchestrationContext
    {
        private DateTime _now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
        private bool _withinRetry;

        public Func<StartReindexOutput> Start { get; init; } = () => new(true, []);

        public Func<PlanReindexInput, SurrogateIdRangePage> Plan { get; init; } = TwoPagesOfRanges;

        public Func<ReindexRangeInput, ReindexRangeOutput> Range { get; set; } = HealthyRange;

        public Func<AwaitDrainInput, bool> Drain { get; set; } = _ => true;

        public Func<PersistReindexProgressInput, bool> Progress { get; set; } = _ => true;

        public List<object> Activities { get; } = [];

        /// <summary>Activity names scheduled directly, outside any retry wrapper.</summary>
        public List<string> DirectSchedules { get; } = [];

        public List<double> TimerDelays { get; } = [];

        public List<ReindexOrchestrationInput> Continuations { get; } = [];

        public IEnumerable<string> Scheduled => Activities.Select(activity => activity.GetType().Name);

        public int RangeAttempts => Inputs<ReindexRangeInput>().Count;

        public CompleteReindexInput Completion => Inputs<CompleteReindexInput>().ShouldHaveSingleItem();

        public override DateTime CurrentUtcDateTime => _now;

        public List<T> Inputs<T>() => Activities.OfType<T>().ToList();

        public static ReindexRangeOutput HealthyRange(ReindexRangeInput range) => new(10, range.StartSurrogateId == 11 ? 1 : 0, []);

        // Every fake activity completes synchronously, so the retry interceptor's inner ScheduleTask calls
        // all happen before this returns and the flag classifies them correctly.
        public override Task<T> ScheduleWithRetry<T>(Type taskActivityType, RetryOptions retryOptions, params object[] parameters)
        {
            _withinRetry = true;
            try
            {
                return base.ScheduleWithRetry<T>(taskActivityType, retryOptions, parameters);
            }
            finally
            {
                _withinRetry = false;
            }
        }

        public override Task<T> ScheduleTask<T>(string name, string version, params object[] parameters)
        {
            if (!_withinRetry)
            {
                DirectSchedules.Add(name);
            }

            var input = parameters.Single();
            Activities.Add(input);
            object result = input switch
            {
                StartReindexInput => Start(),
                RaiseBarrierInput => new BarrierCutoff(10, 30),
                AwaitDrainInput drain => Drain(drain),
                PlanReindexInput plan => Plan(plan),
                ReindexRangeInput range => Range(range),
                PersistReindexProgressInput progress => Progress(progress),
                CompleteReindexInput complete => new CompleteReindexOutput(
                    complete.FailureMessage is null && complete.Tenants.All(tenant => tenant.Success),
                    []),
                _ => throw new InvalidOperationException($"Unexpected activity {name}.")
            };
            return Task.FromResult((T)result);
        }

        public override Task<T> CreateTimer<T>(DateTime fireAt, T state)
        {
            TimerDelays.Add((fireAt - _now).TotalSeconds);
            if (TimerDelays.Count > 100)
            {
                throw new InvalidOperationException("The orchestration kept waiting.");
            }

            _now = fireAt;
            return Task.FromResult(state);
        }

        public override Task<T> CreateTimer<T>(DateTime fireAt, T state, CancellationToken cancellationToken) =>
            CreateTimer(fireAt, state);

        public override void ContinueAsNew(object input) => Continuations.Add((ReindexOrchestrationInput)input);

        public override void ContinueAsNew(string newVersion, object input) => ContinueAsNew(input);

        public override Task<T> CreateSubOrchestrationInstance<T>(string name, string version, object input) =>
            throw new NotSupportedException();

        public override Task<T> CreateSubOrchestrationInstance<T>(string name, string version, string instanceId, object input) =>
            throw new NotSupportedException();

        public override Task<T> CreateSubOrchestrationInstance<T>(
            string name, string version, string instanceId, object input, IDictionary<string, string> tags) =>
            throw new NotSupportedException();

        public override void SendEvent(OrchestrationInstance orchestrationInstance, string eventName, object eventData) =>
            throw new NotSupportedException();

        // Two ranges of ten on the first page, one on the last.
        private static SurrogateIdRangePage TwoPagesOfRanges(PlanReindexInput plan)
        {
            var first = (plan.StartAfterSurrogateId ?? 0) + 1;
            return first == 1
                ? new([new SurrogateIdRange(1, 10, 10), new SurrogateIdRange(11, 20, 10)], 20)
                : new([new SurrogateIdRange(first, first + 9, 10)], null);
        }
    }
}
