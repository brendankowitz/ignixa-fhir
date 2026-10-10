// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Activates a stored package: appends its activation events durably, then starts the follow-up work
/// (phase-two transition, local refresh, reindex).
/// </summary>
/// <remarks>
/// Once the events are durable the activation is not undone. A follow-up step that fails operationally is
/// logged and metered here, once, and reported as a warning issue on the successful result; its own recovery
/// path (startup reconciliation, synchronization, reindex reconciliation) retries it.
/// </remarks>
public class PackageActivationPipeline(
    IPackageResourceRepository packageRepo,
    ISourceEventStore eventStore,
    ConformanceState state,
    PackageActivationPlanner planner,
    ISearchParameterTransitionScheduler transitionScheduler,
    IOptions<ConformanceTransitionOptions> transitionOptions,
    ConformanceRefresher conformanceRefresher,
    ConformanceLease conformanceLease,
    IReindexTrigger reindexTrigger,
    ILogger<PackageActivationPipeline> logger)
{
    public const string RefreshDeferredCode = "CONFORMANCE_REFRESH_DEFERRED";
    public const string TransitionScheduleDeferredCode = "TRANSITION_SCHEDULE_DEFERRED";
    public const string TransitionPendingCode = "SP_TRANSITION_PENDING";
    public const string ReindexTriggerDeferredCode = "REINDEX_TRIGGER_DEFERRED";
    public const string ReindexNotStartedCode = "REINDEX_NOT_STARTED";
    public const string ReindexQueuedCode = "REINDEX_QUEUED";

    private readonly TimeSpan _transitionGrace = transitionOptions.Value.TransitionGrace;

    /// <summary>
    /// Activates <paramref name="packageId"/>@<paramref name="version"/>. A failed result activated nothing; a
    /// successful result is durable and may carry warning issues.
    /// </summary>
    public async Task<ActivationResult> ActivateAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentNullException.ThrowIfNull(version);
        var leaseStart = conformanceLease.CaptureStart();
        var packageResources = await packageRepo.GetResourcesForActivationAsync(packageId, version, cancellationToken);
        var resources = PackageResourceMapper.MapToPackageResources(packageResources);
        var fhirVersion = packageResources.Length > 0 ? packageResources[0].FhirVersion : null;
        logger.LogInformation(
            "Activating package {PackageId}@{Version}: {SearchParamCount} SearchParameters, {StructureDefCount} StructureDefinitions",
            packageId,
            version,
            resources.SearchParameters.Count,
            resources.StructureDefinitions.Count);

        Activation activation;
        using (await state.AcquireActivationLockAsync(cancellationToken))
        {
            IReadOnlyList<ValidationIssue> rejection = [];
            IReadOnlyList<SourceEvent> persisted;
            try
            {
                // Each attempt plans against the caught-up projection: the process-local lock cannot keep
                // another host from activating first, so the append is conditioned on the projection position.
                persisted = await state.AppendWhileActivationLockHeldAsync(
                    eventStore,
                    () =>
                    {
                        (var events, rejection) = planner.Plan(packageId, version, fhirVersion, resources, state);
                        return events;
                    },
                    cancellationToken);
            }
            catch (SourceEventConcurrencyException exception)
            {
                rejection = [new ValidationIssue(PackageActivationRejectedException.ConformanceConflictCode, exception.Message)];
                persisted = [];
            }

            if (rejection.Count > 0)
            {
                logger.LogWarning(
                    "Package {PackageId}@{Version} was not activated: {Issues}",
                    packageId,
                    version,
                    string.Join("; ", rejection.Select(issue => $"{issue.Code}: {issue.Message}")));
                return ActivationResult.Failed(rejection);
            }

            activation = DescribeActivation($"{packageId}@{version}", persisted);
        }

        var issues = new List<ValidationIssue>();
        if (activation.TransitionId is { } transitionId)
        {
            issues.AddRange(await ScheduleTransitionAsync(transitionId, activation.HiddenCodes));
        }

        issues.AddRange(await RefreshAsync(leaseStart));
        var reindex = activation.PendingReindex.Count > 0
            ? await RequestReindexAsync(packageId, version)
            : new ReindexTriggerResult(null, false, null);
        issues.AddRange(DescribeReindex(reindex));

        logger.LogInformation(
            "Package {PackageId}@{Version} activated. Pending reindex: {PendingReindex}; hidden until transition: {HiddenCount}",
            packageId,
            version,
            activation.PendingReindex,
            activation.HiddenCodes.Count);
        return ActivationResult.Activated(activation.PendingReindex, reindex.JobId, issues);
    }

    // Every code the activation hid transitions together under its PackageActivated event id. No persisted
    // events means the package was already active: there is nothing new to transition or reindex.
    private Activation DescribeActivation(string packageKey, IReadOnlyList<SourceEvent> persisted)
    {
        if (persisted.Count == 0)
        {
            return new Activation(null, [], []);
        }

        var transitionId = persisted.Single(evt => evt.Data is PackageActivated).EventId;
        var hiddenCodes = state.GetTransitionParameters(transitionId)
            .Select(parameter => $"{parameter.ResourceType}.{parameter.Code}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var pendingReindex = state.AllSearchParameters.Values
            .Where(parameter => parameter.SourcePackage == packageKey && parameter.Status == SearchParameterStatus.Pending)
            .Select(parameter => parameter.ResourceType)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new Activation(hiddenCodes.Length > 0 ? transitionId : null, hiddenCodes, pendingReindex);
    }

    private async Task<IReadOnlyList<ValidationIssue>> ScheduleTransitionAsync(
        long transitionId,
        IReadOnlyList<string> hiddenCodes)
    {
        var pending = ValidationIssue.Warning(
            TransitionPendingCode,
            $"{hiddenCodes.Count} search parameter code(s) are hidden from search until the transition grace " +
            $"({_transitionGrace}) elapses and the reindex that follows completes: {string.Join(", ", hiddenCodes)}.");
        try
        {
            await transitionScheduler.ScheduleAsync(transitionId, _transitionGrace, CancellationToken.None);
            return [pending];
        }
        catch (Exception exception) when (!IsProgrammerError(exception))
        {
            ConformanceMetrics.RecordTransitionScheduleFailure();
            logger.LogError(
                exception,
                "Transition {TransitionId} is durable, but scheduling its phase two failed; startup reconciliation will schedule it",
                transitionId);
            return
            [
                pending,
                ValidationIssue.Warning(
                    TransitionScheduleDeferredCode,
                    "Scheduling the phase-two transition failed; the hidden codes stay hidden until startup reconciliation schedules it."),
            ];
        }
    }

    // A forced refresh also republishes package resources that changed without advancing the projection.
    // On failure this instance keeps its old lease start, and synchronization retries the refresh.
    private async Task<IReadOnlyList<ValidationIssue>> RefreshAsync(ConformanceLeaseStart leaseStart)
    {
        try
        {
            await conformanceRefresher.RefreshAsync(force: true, CancellationToken.None);
        }
        catch (ConformanceConsumerRefreshException exception)
        {
            ConformanceMetrics.RecordConsumerRefreshFailure("activation");
            logger.LogWarning(exception, "Activation is durable, but the local conformance refresh failed; synchronization will retry");
            return
            [
                ValidationIssue.Warning(
                    RefreshDeferredCode,
                    "The activation is durable, but this instance's conformance refresh failed; synchronization will retry it."),
            ];
        }

        conformanceLease.Renew(leaseStart);
        return [];
    }

    // The trigger owns its failure policy: it defers operational failures and lets programmer errors propagate.
    private Task<ReindexTriggerResult> RequestReindexAsync(string packageId, string version) =>
        reindexTrigger.RequestReindexAsync(
            $"Package {packageId}@{version} activation created Pending search parameters",
            CancellationToken.None);

    private static IReadOnlyList<ValidationIssue> DescribeReindex(ReindexTriggerResult reindex) =>
        reindex switch
        {
            { Deferred: true } => [ValidationIssue.Warning(ReindexTriggerDeferredCode, reindex.Message!)],
            { Queued: true } =>
            [
                ValidationIssue.Information(
                    ReindexQueuedCode,
                    $"Reindex job {reindex.JobId} is already active; a follow-up job reindexes these parameters."),
            ],
            { JobId: null, Message: { } message } => [ValidationIssue.Warning(ReindexNotStartedCode, message)],
            _ => [],
        };

    // Programmer errors fail fast; only operational failures of a follow-up step degrade the activation.
    private static bool IsProgrammerError(Exception exception) =>
        exception is ArgumentException or NullReferenceException or InvalidCastException;

    private sealed record Activation(
        long? TransitionId,
        IReadOnlyList<string> HiddenCodes,
        IReadOnlyList<string> PendingReindex);
}
