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
    private const int MaxConcurrencyAttempts = 3;

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
            for (var attempt = 0; attempt < MaxConcurrencyAttempts; attempt++)
            {
                await conformanceState.CatchUpWhileActivationLockHeldAsync(
                    eventStore,
                    cancellationToken);
                var currentTargets = requireOwnership
                    ? targets.Where(target => IsOwnedByJob(target, jobId)).ToArray()
                    : targets;
                var ignored = requireOwnership
                    ? targets.Where(target => !IsOwnedByJob(target, jobId))
                        .Select(TargetIdentity)
                        .ToArray()
                    : Array.Empty<string>();
                if (currentTargets.Count == 0)
                {
                    return ignored.Distinct(StringComparer.Ordinal).ToArray();
                }

                var expectedPosition = conformanceState.LastProcessedEventId;
                var events = currentTargets.Select(target =>
                {
                    var data = createEvent(target);
                    return new
                    {
                        Target = target,
                        Data = data,
                        Event = new NewSourceEvent(
                            $"reindex:{target.Canonical}",
                            data.GetType().Name,
                            data)
                    };
                }).ToArray();
                IReadOnlyList<SourceEvent> committed;
                try
                {
                    committed = await eventStore.AppendAsync(
                        events.Select(item => item.Event),
                        expectedPosition,
                        cancellationToken);
                }
                catch (SourceEventConcurrencyException) when (attempt < MaxConcurrencyAttempts - 1)
                {
                    continue;
                }

                foreach (var evt in committed)
                {
                    conformanceState.ApplyAndTrack(evt);
                }

                return ignored.Concat(events
                    .Where(item =>
                    {
                        var current = conformanceState.GetSearchParameter(
                            item.Target.ResourceType,
                            item.Target.Code);
                        if (current?.ActivationEventId != item.Target.ActivationEventId)
                        {
                            return true;
                        }

                        return item.Data switch
                        {
                            SearchParameterReindexStarted =>
                                current.Status != SearchParameterStatus.Reindexing ||
                                current.ReindexJobId != jobId,
                            SearchParameterReindexCompleted =>
                                current.Status != SearchParameterStatus.Enabled ||
                                current.ReindexJobId is not null,
                            SearchParameterReindexFailed =>
                                current.Status != SearchParameterStatus.Pending ||
                                current.ReindexJobId is not null,
                            _ => true
                        };
                    })
                    .Select(item => TargetIdentity(item.Target)))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
            }
        }

        throw new SourceEventConcurrencyException(
            conformanceState.LastProcessedEventId,
            conformanceState.LastProcessedEventId);
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
