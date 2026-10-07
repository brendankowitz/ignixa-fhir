using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed class ReindexLifecycleEventWriter(
    ISourceEventStore eventStore,
    ConformanceState conformanceState)
{
    private const int MaxConcurrencyAttempts = 3;

    public Task<IReadOnlyList<string>> StartAsync(
        string jobId,
        IReadOnlyList<ReindexTarget> targets,
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
            cancellationToken);

    public Task<IReadOnlyList<string>> CompleteAsync(
        string jobId,
        IReadOnlyList<ReindexTargetCompletion> completions,
        CancellationToken cancellationToken) =>
        AppendAsync(
            completions.Select(completion => completion.Target).ToArray(),
            target =>
            {
                var completion = completions.Single(item => item.Target.Canonical == target.Canonical);
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
            cancellationToken);

    private async Task<IReadOnlyList<string>> AppendAsync(
        IReadOnlyList<ReindexTarget> targets,
        Func<ReindexTarget, object> createEvent,
        string jobId,
        CancellationToken cancellationToken)
    {
        if (targets.Count == 0)
        {
            return [];
        }

        using (await conformanceState.AcquireActivationLockAsync(cancellationToken))
        {
            for (var attempt = 0; attempt < MaxConcurrencyAttempts; attempt++)
            {
                await conformanceState.CatchUpWhileActivationLockHeldAsync(
                    eventStore,
                    cancellationToken);
                var expectedPosition = conformanceState.LastProcessedEventId;
                var events = targets.Select(target =>
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

                return events
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
                    .Select(item => item.Target.Canonical)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
            }
        }

        throw new SourceEventConcurrencyException(
            conformanceState.LastProcessedEventId,
            conformanceState.LastProcessedEventId);
    }
}

public sealed record ReindexTargetCompletion(
    ReindexTarget Target,
    bool Success,
    long ResourcesIndexed,
    TimeSpan Duration,
    string? ErrorMessage);
