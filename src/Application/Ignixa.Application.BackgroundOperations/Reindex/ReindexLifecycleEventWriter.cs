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

    /// <summary>
    /// Whether the projection already shows the job's successful completion: every planned target is Enabled
    /// for the same activation and the job owns nothing, which is what its completion events leave behind.
    /// </summary>
    public async Task<bool> HasCompletedAsync(
        string jobId,
        IReadOnlyList<ReindexParameterDefinition> targets,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targets);
        using (await conformanceState.AcquireActivationLockAsync(cancellationToken))
        {
            await conformanceState.CatchUpWhileActivationLockHeldAsync(
                eventStore,
                cancellationToken);
            return targets.Count > 0 &&
                targets.All(target =>
                    conformanceState.GetSearchParameter(target.ResourceType, target.Code) is { } current &&
                    current.Canonical == target.Canonical &&
                    current.ActivationEventId == target.ActivationEventId &&
                    current.Status == SearchParameterStatus.Enabled) &&
                !GetOwnedTargets(jobId).Any();
        }
    }

    /// <summary>
    /// Returns every parameter the job still owns to Pending with a guarded Failed event, planned or not.
    /// </summary>
    public async Task FailOwnedAsync(
        string jobId,
        string reason,
        CancellationToken cancellationToken)
    {
        using (await conformanceState.AcquireActivationLockAsync(cancellationToken))
        {
            await conformanceState.AppendWhileActivationLockHeldAsync(
                eventStore,
                () => GetOwnedTargets(jobId)
                    .Select(target => new NewSourceEvent(
                        $"reindex:{target.Canonical}",
                        nameof(SearchParameterReindexFailed),
                        new SearchParameterReindexFailed(
                            target.Canonical,
                            target.Code,
                            target.ResourceType,
                            jobId,
                            reason,
                            target.ActivationEventId)))
                    .ToArray(),
                cancellationToken);
        }
    }

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
                .Select(parameter => new OwnedReindexTarget(parameter.ReindexJobId!, ToTarget(parameter)))
                .ToArray();
        }
    }

    // The caller holds the activation lock.
    private IEnumerable<ReindexParameterDefinition> GetOwnedTargets(string jobId) =>
        conformanceState.AllSearchParameters.Values
            .Where(parameter =>
                parameter.Status == SearchParameterStatus.Reindexing &&
                parameter.ReindexJobId == jobId)
            .Select(ToTarget);

    private static ReindexParameterDefinition ToTarget(ActiveSearchParameter parameter) =>
        new(
            parameter.Canonical,
            parameter.Code,
            parameter.ResourceType,
            parameter.SearchParamId,
            parameter.ActivationEventId,
            [parameter.ResourceType]);

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
