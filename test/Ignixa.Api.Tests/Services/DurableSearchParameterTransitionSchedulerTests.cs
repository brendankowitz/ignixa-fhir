using DurableTask.Core;
using DurableTask.Core.Exceptions;
using DurableTask.Core.History;
using Ignixa.Api.Services;
using NSubstitute;
using Shouldly;

namespace Ignixa.Api.Tests.Services;

/// <summary>Spec §4.4: one transition orchestration per hide event, so a repeated schedule merges into it.</summary>
public class DurableSearchParameterTransitionSchedulerTests
{
    private readonly IOrchestrationServiceClient _orchestrations = Substitute.For<IOrchestrationServiceClient>();

    [Fact]
    public async Task GivenActivationTransition_WhenScheduledTwice_ThenBothUseTheDeterministicInstanceIdAndTheDuplicateIsMerged()
    {
        _orchestrations.CreateTaskOrchestrationAsync(Arg.Any<TaskMessage>(), Arg.Any<OrchestrationStatus[]>())
            .Returns(
                Task.CompletedTask,
                Task.FromException(new OrchestrationAlreadyExistsException(
                    "An orchestration with instance ID 'search-parameter-transition-20' and status 'Pending' already exists.")));
        var scheduler = new DurableSearchParameterTransitionScheduler(new TaskHubClient(_orchestrations));

        await scheduler.ScheduleAsync(20, TimeSpan.FromMinutes(3), CancellationToken.None);
        await Should.NotThrowAsync(() => scheduler.ScheduleAsync(20, TimeSpan.FromMinutes(3), CancellationToken.None));

        await _orchestrations.Received(2).CreateTaskOrchestrationAsync(
            Arg.Is<TaskMessage>(message => ((ExecutionStartedEvent)message.Event).OrchestrationInstance.InstanceId == "search-parameter-transition-20"),
            Arg.Is<OrchestrationStatus[]>(statuses =>
                statuses.SequenceEqual(new[] { OrchestrationStatus.Running, OrchestrationStatus.Pending, OrchestrationStatus.ContinuedAsNew })));
    }
}
