using DurableTask.Core;
using DurableTask.Core.History;
using Ignixa.Api.Services;
using Ignixa.Application.Features.Conformance;
using NSubstitute;

namespace Ignixa.Api.Tests.Services;

public class DurableSearchParameterTransitionSchedulerTests
{
    [Fact]
    public async Task GivenCompletedTransitionInstance_WhenScheduled_ThenItsTerminalStatusDoesNotDeduplicateTheFullGraceReplacement()
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
}
