using DurableTask.Core;

namespace Ignixa.Application.BackgroundOperations.Conformance;

public sealed class SearchParameterTransitionOrchestration
    : TaskOrchestration<TransitionCommitResult, SearchParameterTransitionOrchestrationInput>
{
    public override async Task<TransitionCommitResult> RunTask(
        OrchestrationContext context,
        SearchParameterTransitionOrchestrationInput input)
    {
        await context.CreateTimer(context.CurrentUtcDateTime.Add(input.TransitionGrace), true);
        return await context.ScheduleWithRetry<TransitionCommitResult>(
            typeof(SearchParameterTransitionCommitActivity),
            new RetryOptions(TimeSpan.FromSeconds(1), 2),
            new SearchParameterTransitionCommitActivityInput(input.HideEventId));
    }
}
