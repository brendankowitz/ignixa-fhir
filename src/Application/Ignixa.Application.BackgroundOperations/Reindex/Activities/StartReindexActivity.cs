using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex.Activities;

public sealed class StartReindexActivity(
    ReindexLifecycleEventWriter lifecycle,
    ReindexJobUpdater jobs)
    : AsyncTaskActivity<StartReindexInput, StartReindexOutput>
{
    protected override async Task<StartReindexOutput> ExecuteAsync(
        TaskContext context,
        StartReindexInput input)
    {
        IReadOnlyList<string> ignored = [];
        IReadOnlyList<ReindexParameterDefinition> targets = input.Targets;
        ReindexJobDefinition? definition = null;
        var shouldContinue = await jobs.UpdateAsync(
            input.JobId,
            async (job, cancellationToken) =>
            {
                definition = job.Definition;
                if (job.Definition.SearchParameters.Count > 0 || input.Targets.Count == 0)
                {
                    targets = job.Definition.SearchParameters;
                }

                ignored = await lifecycle.StartAsync(
                    input.JobId,
                    targets,
                    cancellationToken);
                ReindexProgressReporter.InitializeBarrierDelay(job, input.TenantIds, ignored);
            },
            CancellationToken.None);

        var usesPersistedDefinition =
            definition is { SearchParameters.Count: > 0 } || input.Targets.Count == 0;
        return new StartReindexOutput(ignored)
        {
            ShouldContinue = shouldContinue,
            TargetEventId = usesPersistedDefinition
                ? definition?.TargetEventId ?? input.TargetEventId
                : input.TargetEventId,
            ResourceTypes = usesPersistedDefinition ? definition?.ResourceTypes : null,
            Targets = targets
        };
    }
}
