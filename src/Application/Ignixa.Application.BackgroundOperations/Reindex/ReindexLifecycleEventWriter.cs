using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed class ReindexLifecycleEventWriter(
    ISourceEventStore eventStore,
    ConformanceState conformanceState)
{
    public Task<IReadOnlyList<string>> StartAsync(
        string jobId,
        IReadOnlyList<ReindexParameterDefinition> targets,
        CancellationToken cancellationToken) =>
        AppendAsync(
            targets,
            target => new SearchParameterReindexStarted(
                target.Canonical,
                target.Code,
                target.ResourceType,
                jobId,
                target.AffectedResourceTypes,
                target.ActivationEventId),
            jobId,
            requireOwnership: false,
            cancellationToken);

    public Task<IReadOnlyList<string>> CompleteAsync(
        string jobId,
        IReadOnlyList<ReindexTargetCompletion> completions,
        CancellationToken cancellationToken) =>
        AppendAsync(
            completions.Select(completion => completion.Target).ToArray(),
            target =>
            {
                var completion = completions.Single(item =>
                    item.Target.Canonical == target.Canonical &&
                    item.Target.ResourceType == target.ResourceType &&
                    item.Target.Code == target.Code);
                return completion.Success
                    ? new SearchParameterReindexCompleted(
                        target.Canonical,
                        target.Code,
                        target.ResourceType,
                        jobId,
                        completion.ResourcesIndexed,
                        completion.Duration,
                        target.ActivationEventId)
                    : new SearchParameterReindexFailed(
                        target.Canonical,
                        target.Code,
                        target.ResourceType,
                        jobId,
                        completion.ErrorMessage ?? "Reindex failed.",
                        target.ActivationEventId);
            },
            jobId,
            requireOwnership: true,
            cancellationToken);

    public async Task<IReadOnlyList<OwnedReindexTarget>> GetOwnedTargetsAsync(
        CancellationToken cancellationToken)
    {
        using (await conformanceState.AcquireActivationLockAsync(cancellationToken))
        {
            await conformanceState.CatchUpWhileActivationLockHeldAsync(
                eventStore,
                cancellationToken);
            return conformanceState.AllSearchParameters.Values
                .Where(parameter =>
                    parameter.Status == SearchParameterStatus.Reindexing &&
                    parameter.ReindexJobId is not null)
                .Select(parameter => new OwnedReindexTarget(
                    parameter.ReindexJobId!,
                    new ReindexParameterDefinition(
                        parameter.Canonical,
                        parameter.Code,
                        parameter.ResourceType,
                        parameter.SearchParamId,
                        parameter.ActivationEventId,
                        [parameter.ResourceType])))
                .ToArray();
        }
    }

    private async Task<IReadOnlyList<string>> AppendAsync(
        IReadOnlyList<ReindexParameterDefinition> targets,
        Func<ReindexParameterDefinition, object> createEvent,
        string jobId,
        bool requireOwnership,
        CancellationToken cancellationToken)
    {
        using (await conformanceState.AcquireActivationLockAsync(cancellationToken))
        {
            IReadOnlyList<string> ignored = [];
            IReadOnlyList<(ReindexParameterDefinition Target, object Data)> appended = [];
            await conformanceState.AppendWhileActivationLockHeldAsync(
                eventStore,
                () =>
                {
                    var currentTargets = requireOwnership
                        ? targets.Where(target => IsOwnedByJob(target, jobId)).ToArray()
                        : targets;
                    ignored = requireOwnership
                        ? targets.Where(target => !IsOwnedByJob(target, jobId)).Select(TargetIdentity).ToArray()
                        : [];
                    appended = currentTargets.Select(target => (target, createEvent(target))).ToArray();
                    return appended
                        .Select(item => new NewSourceEvent(
                            $"reindex:{item.Target.Canonical}",
                            item.Data.GetType().Name,
                            item.Data))
                        .ToArray();
                },
                cancellationToken);

            return ignored
                .Concat(appended
                    .Where(item => !WasApplied(item.Target, item.Data, jobId))
                    .Select(item => TargetIdentity(item.Target)))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
    }

    private bool WasApplied(ReindexParameterDefinition target, object data, string jobId)
    {
        var current = conformanceState.GetSearchParameter(target.ResourceType, target.Code);
        if (current?.ActivationEventId != target.ActivationEventId)
        {
            return false;
        }

        return data switch
        {
            SearchParameterReindexStarted =>
                current.Status == SearchParameterStatus.Reindexing && current.ReindexJobId == jobId,
            SearchParameterReindexCompleted =>
                current.Status == SearchParameterStatus.Enabled && current.ReindexJobId is null,
            SearchParameterReindexFailed =>
                current.Status == SearchParameterStatus.Pending && current.ReindexJobId is null,
            _ => false
        };
    }

    private bool IsOwnedByJob(ReindexParameterDefinition target, string jobId)
    {
        var current = conformanceState.GetSearchParameter(target.ResourceType, target.Code);
        return current?.ActivationEventId == target.ActivationEventId &&
            current.Status == SearchParameterStatus.Reindexing &&
            current.ReindexJobId == jobId;
    }

    private static string TargetIdentity(ReindexParameterDefinition target) =>
        $"{target.Canonical}|{target.ResourceType}|{target.Code}";
}

public sealed record ReindexTargetCompletion(
    ReindexParameterDefinition Target,
    bool Success,
    long ResourcesIndexed,
    TimeSpan Duration,
    string? ErrorMessage);

public sealed record OwnedReindexTarget(string JobId, ReindexParameterDefinition Target);
