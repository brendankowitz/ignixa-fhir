using DurableTask.Core;
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
    public async Task GivenReconciledTransition_WhenScheduledTwice_ThenEachScheduleUsesAFreshInstanceId()
    {
        var orchestrationService = Substitute.For<IOrchestrationServiceClient>();
        orchestrationService.CreateTaskOrchestrationAsync(
                Arg.Any<TaskMessage>(),
                Arg.Any<OrchestrationStatus[]>())
            .Returns(Task.CompletedTask);
        var scheduler = new DurableSearchParameterTransitionScheduler(new TaskHubClient(orchestrationService));
        var grace = TimeSpan.FromMinutes(3);

        await scheduler.ScheduleReconciliationAsync(20, grace, CancellationToken.None);
        await scheduler.ScheduleReconciliationAsync(20, grace, CancellationToken.None);

        var creationMessages = orchestrationService.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IOrchestrationServiceClient.CreateTaskOrchestrationAsync))
            .Select(call => (TaskMessage)call.GetArguments()[0]!)
            .ToList();

        creationMessages.Count.ShouldBe(2);
        var instanceIds = creationMessages
            .Select(message => ((ExecutionStartedEvent)message.Event).OrchestrationInstance.InstanceId)
            .ToList();
        instanceIds.ShouldAllBe(instanceId =>
            instanceId.StartsWith(
                $"{DurableSearchParameterTransitionScheduler.GetInstanceId(20)}-r-",
                StringComparison.Ordinal));
        instanceIds.Distinct(StringComparer.Ordinal).Count().ShouldBe(2);
    }
}
