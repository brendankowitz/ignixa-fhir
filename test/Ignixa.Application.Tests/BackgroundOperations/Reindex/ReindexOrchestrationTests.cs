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
    // §5.2, §8.2, §8.6 (M5): a job waits BarrierDelay, then plans and drains to the barrier's cutoffs with every
    // activity scheduled with retry. Ranges whose workers report definitions behind the target are retried after a
    // backoff, not planned again. Progress is persisted at phase boundaries and after every five range waves, every
    // range is counted once, and a type with nothing to reindex plans one empty page and moves on.
    [Fact]
    public async Task GivenAJobWhoseWorkersBrieflyLagTheTarget_WhenOrchestrated_ThenItBacksOffAndPersistsAndCountsEveryWaveOnce()
    {
        var context = new FakeContext
        {
            Plan = plan => plan.ResourceType == "Observation"
                ? new([], null)
                : new([.. Enumerable.Range(0, 11).Select(index => new SurrogateIdRange((index * 10) + 1, (index * 10) + 10, 10))], null)
        };
        context.Range = range => context.RangeAttempts <= 3 ? ReindexRangeOutput.DefinitionsNotReady(41) : FakeContext.HealthyRange(range);
        var input = CreateOrchestrationInput(barrierDelay: TimeSpan.FromSeconds(30)) with
        {
            ResourceTypes = ["Patient", "Observation"],
            Parameters = ReindexJobParameters.Create(maximumConcurrency: 1)
        };

        var result = await new ReindexOrchestration().RunTask(context, input);

        result.Success.ShouldBeTrue();
        context.RetriedSchedules.ShouldBe(context.Activities.Count);
        context.TimerDelays.ShouldBe([30, 1, 2, 4]);
        context.RangeAttempts.ShouldBe(14);
        context.Inputs<PlanReindexInput>().ShouldAllBe(plan => plan.CutoffSurrogateId == 30);
        context.Inputs<AwaitDrainInput>().ShouldHaveSingleItem().CutoffTransactionId.ShouldBe(10);
        context.Inputs<PersistReindexProgressInput>()
            .Select(snapshot => snapshot.Progress.Tenants.Single().ResourcesReindexed)
            .ShouldBe([0L, 0L, 50L, 100L, 110L]);
        var tenant = context.Completion.Tenants.ShouldHaveSingleItem();
        tenant.Status.ShouldBe(ReindexTenantStatus.Completed);
        (tenant.ResourcesToReindex, tenant.ResourcesReindexed, tenant.Conflicts).ShouldBe((110L, 110L, 1L));
    }

    // Spec §8.2, §8.6 (N4): an activity still failing after its retries ends the job Failed with its reason; a
    // snapshot that cannot be written never reruns a range, whichever phase boundary it is written at.
    [Theory]
    [InlineData("start", false, 0)]
    [InlineData("progress", false, 0)]
    [InlineData("progress", true, 3)]
    public async Task GivenAnActivityFailsAfterItsRetries_WhenOrchestrated_ThenTheJobIsFinalizedFailedWithItsReason(
        string failingActivity,
        bool failOnlyAfterRanges,
        int expectedRangeAttempts)
    {
        var context = new FakeContext();
        var failure = new InvalidOperationException($"{failingActivity} failed");
        context.Start = failingActivity == "start" ? () => throw failure : context.Start;
        context.Progress = _ => failingActivity == "progress" && (!failOnlyAfterRanges || context.RangeAttempts > 0)
            ? throw failure
            : true;

        var output = await new ReindexOrchestration().RunTask(context, CreateOrchestrationInput());

        output.Success.ShouldBeFalse();
        context.Completion.FailureMessage.ShouldContain($"{failingActivity} failed");
        context.RangeAttempts.ShouldBe(expectedRangeAttempts);
    }

    // Spec §8.2, §8.6: tenants are isolated. One still draining does not hold another's range waves, and a range
    // activity that fails after its retries fails only that (tenant, type); the other completes, the job ends Failed.
    [Fact]
    public async Task GivenTwoTenants_WhenOneDrainsSlowlyAndItsRangesFailAfterRetries_ThenTheOtherIsNeitherHeldNorFailed()
    {
        var context = new FakeContext();
        context.Drain = drain => drain.TenantId != 2 || context.Inputs<AwaitDrainInput>().Count(poll => poll.TenantId == 2) > 2;
        context.Range = range => range.TenantId == 2
            ? throw new InvalidOperationException("range worker crashed")
            : FakeContext.HealthyRange(range);

        var output = await new ReindexOrchestration().RunTask(context, CreateOrchestrationInput(tenantIds: [1, 2]));

        output.Success.ShouldBeFalse();
        var lastDrainOfTenant2 = context.Activities.FindLastIndex(activity => activity is AwaitDrainInput { TenantId: 2 });
        var rangesOfTenant1 = context.Activities.Index()
            .Where(item => item.Item is ReindexRangeInput { TenantId: 1 }).Select(item => item.Index).ToArray();
        rangesOfTenant1.Length.ShouldBe(3);
        rangesOfTenant1.ShouldAllBe(index => index < lastDrainOfTenant2);
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

    // Spec §8.5: a job closed elsewhere is authoritative, whether start, the barrier-delay boundary or a later
    // phase boundary (snapshot 0, 1 or 2) finds it.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GivenJobClosedElsewhere_WhenStartOrAPhaseBoundaryFindsIt_ThenOrchestrationStopsWithoutCompleting(int closedAtSnapshot)
    {
        var context = new FakeContext { Start = () => new(closedAtSnapshot > 0, []) };
        context.Progress = _ => context.Inputs<PersistReindexProgressInput>().Count < closedAtSnapshot;

        var result = await new ReindexOrchestration().RunTask(context, CreateOrchestrationInput());

        result.Success.ShouldBeFalse();
        context.Inputs<PersistReindexProgressInput>().Count.ShouldBe(closedAtSnapshot);
        context.Scheduled.Last().ShouldBe(closedAtSnapshot == 0 ? nameof(StartReindexInput) : nameof(PersistReindexProgressInput));
        context.Continuations.ShouldBeEmpty();
    }

    // §8.4: a tenant whose definitions stay behind for StaleJobTimeout fails, reported as data.
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

    // §8.2 (B1): ContinueAsNew carries the typed state and returns without scheduling more work; continuing after
    // Start, the barrier delay and the barrier, the carried ReindexOrchestrationState round-trips with enums as strings.
    [Fact]
    public async Task GivenContinuationsAtEveryPhaseBoundary_WhenSerializedAndResumed_ThenTheTypedStateIsCarried()
    {
        var context = new FakeContext();
        await new ReindexOrchestration().RunTask(context, CreateOrchestrationInput() with { ContinueAsNewThreshold = 1 });
        var afterStart = context.Continuations.ShouldHaveSingleItem();
        var scheduledBeforeContinuing = context.Scheduled.ToArray();
        await new ReindexOrchestration().RunTask(context, afterStart);
        await new ReindexOrchestration().RunTask(context, context.Continuations[^1]);
        var continuation = context.Continuations[^1];

        var serialized = JsonDataConverter.Default.Serialize(continuation);
        var roundTripped = JsonDataConverter.Default.Deserialize<ReindexOrchestrationInput>(serialized)!;
        // The chain ran at threshold 1; resume at 3 so this execution plans and drains once before continuing again.
        await new ReindexOrchestration().RunTask(context, roundTripped with { ContinueAsNewThreshold = 3 });

        afterStart.State!.Started.ShouldBeTrue();
        scheduledBeforeContinuing.ShouldBe([nameof(StartReindexInput)]);
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

    // §5.2 step 3, §8.6: a drain still waiting at StaleJobTimeout fails the tenant.
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
                        Status = ReindexTenantStatus.Draining, CutoffTransactionId = 30, CutoffSurrogateId = 10
                    },
                    Wait = ReindexWait.Begin(now)
                }
            ]
        };

    /// <summary>
    /// Runs each activity in-process through a replaceable handler and records every attempt's input in order.
    /// The defaults are a healthy job: the barrier cuts at transaction 10 / surrogate 30, the drain is done, and
    /// each resource type plans three ranges of ten resources over two pages, one range with a conflict.
    /// </summary>
    private sealed class FakeContext : OrchestrationContext
    {
        private DateTime _now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

        public Func<StartReindexOutput> Start { get; set; } = () => new(true, []);

        public Func<PlanReindexInput, SurrogateIdRangePage> Plan { get; init; } = TwoPagesOfRanges;

        public Func<ReindexRangeInput, ReindexRangeOutput> Range { get; set; } = HealthyRange;

        public Func<AwaitDrainInput, bool> Drain { get; set; } = _ => true;

        public Func<PersistReindexProgressInput, bool> Progress { get; set; } = _ => true;

        public List<object> Activities { get; } = [];

        /// <summary>Activities scheduled through the retry wrapper.</summary>
        public int RetriedSchedules { get; private set; }

        public List<double> TimerDelays { get; } = [];

        public List<ReindexOrchestrationInput> Continuations { get; } = [];

        public IEnumerable<string> Scheduled => Activities.Select(activity => activity.GetType().Name);

        public int RangeAttempts => Inputs<ReindexRangeInput>().Count;

        public CompleteReindexInput Completion => Inputs<CompleteReindexInput>().ShouldHaveSingleItem();

        public override DateTime CurrentUtcDateTime => _now;

        public List<T> Inputs<T>() => Activities.OfType<T>().ToList();

        public static ReindexRangeOutput HealthyRange(ReindexRangeInput range) => new(10, range.StartSurrogateId == 11 ? 1 : 0, []);

        public override Task<T> ScheduleWithRetry<T>(Type taskActivityType, RetryOptions retryOptions, params object[] parameters)
        {
            RetriedSchedules++;
            return base.ScheduleWithRetry<T>(taskActivityType, retryOptions, parameters);
        }

        public override Task<T> ScheduleTask<T>(string name, string version, params object[] parameters)
        {
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
                    complete.FailureMessage is null && complete.Tenants.All(tenant => tenant.Success), []),
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
        public static SurrogateIdRangePage TwoPagesOfRanges(PlanReindexInput plan)
        {
            var first = (plan.StartAfterSurrogateId ?? 0) + 1;
            return first == 1
                ? new([new SurrogateIdRange(1, 10, 10), new SurrogateIdRange(11, 20, 10)], 20)
                : new([new SurrogateIdRange(first, first + 9, 10)], null);
        }
    }
}
