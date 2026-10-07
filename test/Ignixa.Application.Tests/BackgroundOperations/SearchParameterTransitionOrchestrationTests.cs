using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Conformance;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations;

public class SearchParameterTransitionOrchestrationTests
{
    [Fact]
    public async Task GivenTransitionInput_WhenOrchestrated_ThenItWaitsForGraceAndSchedulesCommit()
    {
        var context = Substitute.For<OrchestrationContext>();
        var now = new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc);
        context.CurrentUtcDateTime.Returns(now);
        context.CreateTimer(now.AddSeconds(3), true).Returns(Task.FromResult(true));
        context.ScheduleTask<TransitionCommitResult>(
                typeof(SearchParameterTransitionCommitActivity),
                Arg.Any<object[]>())
            .Returns(new TransitionCommitResult(true));
        var orchestration = new SearchParameterTransitionOrchestration();

        var result = await orchestration.RunTask(
            context,
            new SearchParameterTransitionOrchestrationInput(20, TimeSpan.FromSeconds(3)));

        result.Committed.ShouldBeTrue();
        _ = context.Received(1).CreateTimer(now.AddSeconds(3), true);
        _ = context.Received(1).ScheduleTask<TransitionCommitResult>(
            typeof(SearchParameterTransitionCommitActivity),
            Arg.Is<object[]>(items => ((SearchParameterTransitionCommitActivityInput)items.Single()).HideEventId == 20));
    }
}
