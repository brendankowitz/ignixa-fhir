using DurableTask.Core;

namespace Ignixa.Application.BackgroundOperations.Conformance;

public sealed class SearchParameterTransitionOrchestration
    : TaskOrchestration<bool, SearchParameterTransitionOrchestrationInput>
{
    public override async Task<bool> RunTask(
        OrchestrationContext context,
        SearchParameterTransitionOrchestrationInput input)
    {
        await context.CreateTimer(context.CurrentUtcDateTime.Add(input.TransitionGrace), true);
        return await context.ScheduleWithRetry<bool>(
            typeof(SearchParameterTransitionCommitActivity),
            new RetryOptions(TimeSpan.FromSeconds(1), 2),
            new SearchParameterTransitionCommitActivityInput(input.HideEventId));
    }
}
