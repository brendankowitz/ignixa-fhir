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
}
