using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex;

/// <summary>
/// Appends a job's guarded reindex lifecycle events under the activation lock. The projection applies each
/// event only if its guard still holds (§4.3); the identities of events it would not apply are returned as
/// the job's ignored lifecycle events.
/// </summary>
public sealed class ReindexLifecycleEventWriter(
    ISourceEventStore eventStore,
    ConformanceState conformanceState)
{
    /// <returns>The targets the projection did not move to Reindexing for this job.</returns>
    public async Task<IReadOnlyList<string>> StartAsync(
        string jobId,
        IReadOnlyList<ReindexParameterDefinition> targets,
        CancellationToken cancellationToken)
    {
        using (await conformanceState.AcquireActivationLockAsync(cancellationToken))
        {
            await conformanceState.AppendWhileActivationLockHeldAsync(
                eventStore,
                () => targets
                    .Select(target => ToEvent(target, new SearchParameterReindexStarted(
                        target.Canonical,
                        target.Code,
                        target.ResourceType,
                        jobId,
                        target.AffectedResourceTypes,
                        target.ActivationEventId)))
                    .ToArray(),
                cancellationToken);

            return targets
                .Where(target => !GetOwnedTargets(jobId).Any(owned => SameTarget(owned, target)))
                .Select(TargetIdentity)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
    }

    /// <summary>
    /// Finishes every parameter the job still owns: Completed or Failed by its entry in
    /// <paramref name="completions"/>, and Failed with <paramref name="fallbackReason"/> when it has none, so
    /// no parameter stays Reindexing after its job ends.
    /// </summary>
    /// <param name="duration">How long the job ran; recorded on every completed event.</param>
    public async Task<ReindexFinishResult> FinishAsync(
        string jobId,
        IReadOnlyList<ReindexTargetCompletion> completions,
        string fallbackReason,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        using (await conformanceState.AcquireActivationLockAsync(cancellationToken))
        {
            var result = new ReindexFinishResult([], []);
            await conformanceState.AppendWhileActivationLockHeldAsync(
                eventStore,
                () =>
                {
                    // Built inside the callback: it runs against the caught-up projection the events are appended to.
                    var owned = GetOwnedTargets(jobId).ToArray();
                    result = new ReindexFinishResult(
                        completions
                            .Where(completion => !owned.Any(target => SameTarget(target, completion.Target)))
                            .Select(completion => TargetIdentity(completion.Target))
                            .Distinct(StringComparer.Ordinal)
                            .ToArray(),
                        owned
                            .Where(target => !completions.Any(completion => SameTarget(completion.Target, target)))
                            .Select(TargetIdentity)
                            .ToArray());
                    return owned
                        .Select(target => ToEvent(target, CreateFinishedEvent(
                            target,
                            completions.FirstOrDefault(completion => SameTarget(completion.Target, target)),
                            jobId,
                            fallbackReason,
                            duration)))
                        .ToArray();
                },
                cancellationToken);
            return result;
        }
    }

    /// <summary>
    /// Whether the projection already shows the job's successful completion: every planned target is Enabled
    /// for the same activation and the job owns nothing, which is what its completion events leave behind.
    /// </summary>
    public async Task<bool> HasCompletedAsync(
        string jobId,
        IReadOnlyList<ReindexParameterDefinition> targets,
        CancellationToken cancellationToken)
    {
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

    private static object CreateFinishedEvent(
        ReindexParameterDefinition target,
        ReindexTargetCompletion? completion,
        string jobId,
        string fallbackReason,
        TimeSpan duration) =>
        completion is { Success: true }
            ? new SearchParameterReindexCompleted(
                target.Canonical,
                target.Code,
                target.ResourceType,
                jobId,
                completion.ResourcesIndexed,
                duration,
                target.ActivationEventId)
            : new SearchParameterReindexFailed(
                target.Canonical,
                target.Code,
                target.ResourceType,
                jobId,
                completion?.ErrorMessage ?? fallbackReason,
                target.ActivationEventId);

    private static NewSourceEvent ToEvent(ReindexParameterDefinition target, object data) =>
        new($"reindex:{target.Canonical}", data.GetType().Name, data);

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
            [parameter.ResourceType],
            parameter.OverridesCanonical,
            parameter.FhirVersion);

    /// <summary>
    /// Target identity: one activation of one canonical for one resource type and code.
    /// </summary>
    internal static bool SameTarget(ReindexParameterDefinition left, ReindexParameterDefinition right) =>
        left.Canonical == right.Canonical &&
        left.ResourceType == right.ResourceType &&
        left.Code == right.Code &&
        left.ActivationEventId == right.ActivationEventId;

    private static string TargetIdentity(ReindexParameterDefinition target) =>
        $"{target.Canonical}|{target.ResourceType}|{target.Code}";
}
