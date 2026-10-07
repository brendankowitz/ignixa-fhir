using System.Text.Json;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Activities;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.BackgroundOperations.Reindex.Orchestrations;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class ReindexOrchestrationTests
{
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
    }

    [Fact]
    public async Task GivenJob_WhenOrchestrated_ThenLifecycleDelayTenantWorkAndCompletionAreOrdered()
    {
        var context = Substitute.For<OrchestrationContext>();
        var now = new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);
        context.CurrentUtcDateTime.Returns(now);
        context.ScheduleTask<StartReindexOutput>(
                typeof(StartReindexActivity),
                Arg.Any<object[]>())
            .Returns(new StartReindexOutput([]));
        context.CreateTimer(now.AddSeconds(30), true).Returns(Task.FromResult(true));
        context.ScheduleTask<RaiseBarrierOutput>(
                typeof(RaiseBarrierActivity),
                Arg.Any<object[]>())
            .Returns(new RaiseBarrierOutput(1, 10, 20));
        context.ScheduleTask<AwaitDrainOutput>(
                typeof(AwaitDrainActivity),
                Arg.Any<object[]>())
            .Returns(new AwaitDrainOutput(1, true, 10));
        context.ScheduleTask<PlanReindexOutput>(
                typeof(PlanReindexActivity),
                Arg.Any<object[]>())
            .Returns(new PlanReindexOutput([], null));
        context.ScheduleTask<CompleteReindexOutput>(
                typeof(CompleteReindexActivity),
                Arg.Any<object[]>())
            .Returns(new CompleteReindexOutput(true, []));

        var input = ReindexOrchestrationInput.CreateForTest(
            "job",
            targetEventId: 42,
            barrierDelay: TimeSpan.FromSeconds(30),
            tenantIds: [1]);

        var result = await new ReindexOrchestration().RunTask(context, input);

        result.Success.ShouldBeTrue();
        _ = context.Received(1).ScheduleTask<StartReindexOutput>(
            typeof(StartReindexActivity),
            Arg.Any<object[]>());
        _ = context.Received(1).CreateTimer(now.AddSeconds(30), true);
        _ = context.Received(1).ScheduleTask<RaiseBarrierOutput>(
            typeof(RaiseBarrierActivity),
            Arg.Any<object[]>());
        _ = context.Received(1).ScheduleTask<AwaitDrainOutput>(
            typeof(AwaitDrainActivity),
            Arg.Any<object[]>());
        _ = context.Received(1).ScheduleTask<PlanReindexOutput>(
            typeof(PlanReindexActivity),
            Arg.Any<object[]>());
        _ = context.Received(1).ScheduleTask<CompleteReindexOutput>(
            typeof(CompleteReindexActivity),
            Arg.Any<object[]>());
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

    private sealed class ExecutingContext : OrchestrationContext
    {
        public int StartCalls { get; private set; }
        public int BarrierCalls { get; private set; }
        public int ContinuationCount { get; private set; }

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
                _ => throw new InvalidOperationException($"Unexpected activity {name}.")
            };

            return Task.FromResult((T)result);
        }

        public override Task<T> CreateTimer<T>(DateTime fireAt, T state) => Task.FromResult(state);

        public override Task<T> CreateTimer<T>(
            DateTime fireAt,
            T state,
            CancellationToken cancellationToken) => Task.FromResult(state);

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

        private static ReindexRangeOutput Range(ReindexRangeInput input) =>
            new(10, 10, input.StartSurrogateId == 11 ? 1 : 0, []);

        private static CompleteReindexOutput Complete(CompleteReindexInput input)
        {
            input.Tenants.Single().ResourcesToReindex.ShouldBe(30);
            input.Tenants.Single().ResourcesReindexed.ShouldBe(30);
            input.Tenants.Single().Conflicts.ShouldBe(1);
            return new CompleteReindexOutput(true, []);
        }
    }
}
