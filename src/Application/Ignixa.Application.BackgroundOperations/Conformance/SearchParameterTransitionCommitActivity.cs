using DurableTask.Core;
using Ignixa.Application.Features.Conformance;

namespace Ignixa.Application.BackgroundOperations.Conformance;

public sealed class SearchParameterTransitionCommitActivity(
    SearchParameterTransitionCommitter committer)
    : AsyncTaskActivity<SearchParameterTransitionCommitActivityInput, bool>
{
    /// <returns>Whether this run committed the transition.</returns>
    protected override Task<bool> ExecuteAsync(
        TaskContext context,
        SearchParameterTransitionCommitActivityInput input) =>
        committer.CommitAsync(input.HideEventId, CancellationToken.None);
}
