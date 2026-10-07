using DurableTask.Core;
using Ignixa.Application.Features.Conformance;

namespace Ignixa.Application.BackgroundOperations.Conformance;

public sealed class SearchParameterTransitionCommitActivity(
    SearchParameterTransitionCommitter committer)
    : AsyncTaskActivity<SearchParameterTransitionCommitActivityInput, TransitionCommitResult>
{
    protected override async Task<TransitionCommitResult> ExecuteAsync(
        TaskContext context,
        SearchParameterTransitionCommitActivityInput input) =>
        new(await committer.CommitAsync(input.HideEventId, CancellationToken.None));
}
