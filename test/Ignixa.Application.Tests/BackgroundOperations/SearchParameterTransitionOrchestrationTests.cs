using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Conformance;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations;

public class SearchParameterTransitionOrchestrationTests
{
    [Fact]
    public async Task GivenTransitionInput_WhenOrchestrated_ThenItWaitsForGraceAndRetriesCommit()
    {
        var context = Substitute.For<OrchestrationContext>();
        var now = new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc);
        context.CurrentUtcDateTime.Returns(now);
        context.CreateTimer(now.AddSeconds(3), true).Returns(Task.FromResult(true));
        context.ScheduleWithRetry<TransitionCommitResult>(
                typeof(SearchParameterTransitionCommitActivity),
                Arg.Any<RetryOptions>(),
                Arg.Any<object[]>())
            .Returns(new TransitionCommitResult(true));
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
        _ = context.Received(1).ScheduleWithRetry<TransitionCommitResult>(
            typeof(SearchParameterTransitionCommitActivity),
            Arg.Is<RetryOptions>(options =>
                options.FirstRetryInterval == TimeSpan.FromSeconds(1) &&
                options.MaxNumberOfAttempts == 2),
            Arg.Is<object[]>(items => ((SearchParameterTransitionCommitActivityInput)items.Single()).HideEventId == 20));
    }
}
