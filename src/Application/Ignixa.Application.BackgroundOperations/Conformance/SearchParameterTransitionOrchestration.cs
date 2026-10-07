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
        return await context.ScheduleTask<TransitionCommitResult>(
            typeof(SearchParameterTransitionCommitActivity),
            new SearchParameterTransitionCommitActivityInput(input.HideEventId));
    }
}
