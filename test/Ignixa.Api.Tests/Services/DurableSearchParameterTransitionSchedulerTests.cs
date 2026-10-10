using DurableTask.Core;
using DurableTask.Core.Exceptions;
using DurableTask.Core.History;
using Ignixa.Api.Services;
using Ignixa.Application.Features.Conformance;
using NSubstitute;
using Shouldly;

namespace Ignixa.Api.Tests.Services;

public class DurableSearchParameterTransitionSchedulerTests
{
    [Fact]
    public async Task GivenActivationTransition_WhenScheduled_ThenItUsesTheDeterministicInstanceId()
    {
        var orchestrationService = Substitute.For<IOrchestrationServiceClient>();
        orchestrationService.CreateTaskOrchestrationAsync(
                Arg.Any<TaskMessage>(),
                Arg.Any<OrchestrationStatus[]>())
            .Returns(Task.CompletedTask);
        var scheduler = new DurableSearchParameterTransitionScheduler(new TaskHubClient(orchestrationService));
        var grace = TimeSpan.FromMinutes(3);

        await scheduler.ScheduleAsync(20, grace, CancellationToken.None);

        await orchestrationService.Received(1).CreateTaskOrchestrationAsync(
            Arg.Is<TaskMessage>(message =>
                ((ExecutionStartedEvent)message.Event).OrchestrationInstance.InstanceId ==
                DurableSearchParameterTransitionScheduler.GetInstanceId(20)),
            Arg.Is<OrchestrationStatus[]>(statuses =>
                statuses.Length == 3 &&
                statuses[0] == OrchestrationStatus.Running &&
                statuses[1] == OrchestrationStatus.Pending &&
                statuses[2] == OrchestrationStatus.ContinuedAsNew));
    }

    [Fact]
    public async Task GivenTransitionScheduledTwice_WhenScheduled_ThenBothSchedulesUseTheDeterministicInstanceId()
    {
        var orchestrationService = Substitute.For<IOrchestrationServiceClient>();
        orchestrationService.CreateTaskOrchestrationAsync(
                Arg.Any<TaskMessage>(),
                Arg.Any<OrchestrationStatus[]>())
            .Returns(Task.CompletedTask);
        var scheduler = new DurableSearchParameterTransitionScheduler(new TaskHubClient(orchestrationService));
        var grace = TimeSpan.FromMinutes(3);

        await scheduler.ScheduleAsync(20, grace, CancellationToken.None);
        await scheduler.ScheduleAsync(20, grace, CancellationToken.None);

        var instanceIds = orchestrationService.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IOrchestrationServiceClient.CreateTaskOrchestrationAsync))
            .Select(call => ((ExecutionStartedEvent)((TaskMessage)call.GetArguments()[0]!).Event).OrchestrationInstance.InstanceId)
            .ToList();
        instanceIds.ShouldBe(
        [
            DurableSearchParameterTransitionScheduler.GetInstanceId(20),
            DurableSearchParameterTransitionScheduler.GetInstanceId(20),
        ]);
    }

    [Fact]
    public async Task GivenActiveTransitionOrchestration_WhenScheduledAgain_ThenTheDuplicateIsMergedIntoIt()
    {
        var orchestrationService = Substitute.For<IOrchestrationServiceClient>();
        orchestrationService.CreateTaskOrchestrationAsync(
                Arg.Any<TaskMessage>(),
                Arg.Any<OrchestrationStatus[]>())
            .Returns(
                Task.CompletedTask,
                Task.FromException(new OrchestrationAlreadyExistsException(
                    "An orchestration with instance ID 'search-parameter-transition-20' and status 'Pending' already exists.")));
        var scheduler = new DurableSearchParameterTransitionScheduler(new TaskHubClient(orchestrationService));

        await scheduler.ScheduleAsync(20, TimeSpan.FromMinutes(3), CancellationToken.None);
        await Should.NotThrowAsync(() => scheduler.ScheduleAsync(20, TimeSpan.FromMinutes(3), CancellationToken.None));
    }
}
