// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Pipeline for activating FHIR packages using event-sourced conformance management.
/// Validates resources, builds activation events, and updates in-memory state atomically.
/// </summary>
public class PackageActivationPipeline(
    IPackageResourceRepository packageRepo,
    ISourceEventStore eventStore,
    ConformanceState state,
    IOptions<SearchParameterResolutionOptions> options,
    ISearchParameterTransitionScheduler transitionScheduler,
    IOptions<ConformanceTransitionOptions> transitionOptions,
    ConformanceRefreshPublisher refreshPublisher,
    IConformanceLease conformanceLease,
    IReindexTrigger reindexTrigger,
    ILogger<PackageActivationPipeline> logger)
{
    private const int TransitionSchedulingAttempts = 3;
    private static readonly TimeSpan TransitionSchedulingRetryDelay = TimeSpan.FromMilliseconds(100);

    private readonly IPackageResourceRepository _packageRepo = packageRepo ?? throw new ArgumentNullException(nameof(packageRepo));
    private readonly ISourceEventStore _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
    private readonly ConformanceState _state = state ?? throw new ArgumentNullException(nameof(state));
    private readonly SearchParameterResolutionOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly ISearchParameterTransitionScheduler _transitionScheduler = transitionScheduler ?? throw new ArgumentNullException(nameof(transitionScheduler));
    private readonly ConformanceTransitionOptions _transitionOptions = transitionOptions?.Value ?? throw new ArgumentNullException(nameof(transitionOptions));
    private readonly ConformanceRefreshPublisher _refreshPublisher = refreshPublisher ?? throw new ArgumentNullException(nameof(refreshPublisher));
    private readonly IConformanceLease _conformanceLease = conformanceLease ?? throw new ArgumentNullException(nameof(conformanceLease));
    private readonly IReindexTrigger _reindexTrigger = reindexTrigger ?? throw new ArgumentNullException(nameof(reindexTrigger));
    private readonly ILogger<PackageActivationPipeline> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Activates a package by validating resources, emitting events, and updating state.
    /// Returns success result with pending reindex list or failure result with validation issues.
    /// </summary>
    public async Task<ActivationResult> ActivateAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentNullException.ThrowIfNull(version);
        var leaseStart = _conformanceLease.CaptureStart();

        var packageResources = await _packageRepo.GetResourcesForActivationAsync(packageId, version, cancellationToken);
        var resources = PackageResourceMapper.MapToPackageResources(packageResources);

        _logger.LogDebug(
            "Loaded {SearchParamCount} SearchParameters and {StructureDefCount} StructureDefinitions",
            resources.SearchParameters.Count,
            resources.StructureDefinitions.Count);

        IReadOnlyList<long> transitionEventIds;
        List<string> reindexNeeded;
        using (await _state.AcquireActivationLockAsync(cancellationToken))
        {
            // Check if package is already activated (idempotency)
            var packageKey = $"{packageId}@{version}";
            if (_state.Packages.ContainsKey(packageKey))
            {
                _logger.LogDebug(
                    "Package {PackageId}@{Version} already activated, skipping",
                    packageId,
                    version);
                return ActivationResult.Succeeded([]);
            }

            _logger.LogInformation("Activating package {PackageId}@{Version}", packageId, version);

            // 2. Validate against current state
            var validation = ValidateCompositeComponents(resources, _state);
            if (!validation.Success)
            {
                return RejectActivation(validation.Issues);
            }

            // Build and apply every proposed event to detached state before anything is durable.
            var expectedLastEventId = _state.LastProcessedEventId;
            using var staged = _state.CreateStagingCopy();
            var (events, issue) = BuildAndValidateActivationEvents(packageId, version, resources, staged);
            if (issue is not null)
            {
                return RejectActivation([issue]);
            }

            _logger.LogDebug("Built {EventCount} activation events", events.Count);

            // The process-local lock cannot protect this snapshot from another host's activation.
            // Compare its durable event position under the store's existing append lock.
            IReadOnlyList<SourceEvent> persistedEvents;
            try
            {
                persistedEvents = await _eventStore.AppendAsync(events, expectedLastEventId, cancellationToken);
            }
            catch (SourceEventConcurrencyException exception)
            {
                return RejectActivation([new ValidationIssue("CONFORMANCE_CONFLICT", exception.Message)]);
            }

            // 5. Apply events with correct EventIds to in-memory state
            foreach (var evt in persistedEvents)
            {
                _state.ApplyAndTrack(evt);
            }

            transitionEventIds = persistedEvents
                .Where(evt => evt.Data is SearchParameterActivated)
                .Select(evt => evt.EventId)
                .Where(eventId => _state.GetTransitionCandidates(eventId).Count > 0)
                .ToArray();
            reindexNeeded = DetectReindexRequirements(packageKey);
        }

        // 7. Schedule phase two only after the phase-one event is durable.
        var transitionSchedulingDeferred = false;
        foreach (var eventId in transitionEventIds)
        {
            if (!await TryScheduleTransitionAsync(eventId))
            {
                transitionSchedulingDeferred = true;
            }
        }

        // Refresh failures do not undo durable activation or phase-two scheduling. The sync loop retries
        // local consumers, and this instance does not renew its search lease until a refresh succeeds.
        var refreshed = true;
        try
        {
            await _refreshPublisher.RefreshUntilCurrentAsync(CancellationToken.None);
        }
        catch (ConformanceConsumerRefreshException exception)
        {
            refreshed = false;
            ConformanceConsumerRefreshMetrics.RecordFailure("activation");
            _logger.LogWarning(
                exception,
                "Package {PackageId}@{Version} activated durably, but local conformance consumer refresh failed; synchronization will retry",
                packageId,
                version);
        }

        _logger.LogInformation(
            "Package {PackageId}@{Version} activated successfully. Pending reindex: {Count} resource types",
            packageId,
            version,
            reindexNeeded.Count);

        if (refreshed)
        {
            _conformanceLease.Renew(leaseStart);
        }

        ReindexTriggerResult? reindex = null;
        if (reindexNeeded.Count > 0)
        {
            try
            {
                reindex = await _reindexTrigger.RequestReindexAsync(
                    $"Package {packageId}@{version} activation created Pending search parameters",
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                ReindexTriggerMetrics.RecordFailure("Activation");
                _logger.LogError(
                    exception,
                    "Package {PackageId}@{Version} activated durably, but the automatic reindex trigger failed; periodic reconciliation will retry",
                    packageId,
                    version);
                reindex = new ReindexTriggerResult(
                    null,
                    false,
                    "Automatic reindex trigger failed; periodic reconciliation will retry.",
                    Deferred: true);
            }
        }

        return ActivationResult.Succeeded(
            reindexNeeded,
            localRefreshDeferred: !refreshed,
            transitionSchedulingDeferred: transitionSchedulingDeferred,
            reindex: reindex);
    }

    private async Task<bool> TryScheduleTransitionAsync(long eventId)
    {
        Exception? lastException = null;
        for (var attempt = 1; attempt <= TransitionSchedulingAttempts; attempt++)
        {
            try
            {
                await _transitionScheduler.ScheduleAsync(
                    eventId,
                    _transitionOptions.TransitionGrace,
                    CancellationToken.None);
                return true;
            }
            catch (Exception exception)
            {
                lastException = exception;
                if (attempt < TransitionSchedulingAttempts)
                {
                    _logger.LogWarning(
                        exception,
                        "Scheduling transition for hide EventId {EventId} failed on attempt {Attempt}; retrying",
                        eventId,
                        attempt);
                    await Task.Delay(TransitionSchedulingRetryDelay, CancellationToken.None);
                }
            }
        }

        // The activation event is already durable. Do not report a false activation failure, but make
        // the degraded condition explicit; the sync watchdog will make a fresh, full-grace attempt.
        ConformanceTransitionMetrics.RecordScheduleFailure();
        _logger.LogError(
            lastException,
            "Package activation persisted hide EventId {EventId}, but transition scheduling failed after {AttemptCount} attempts; the watchdog will retry",
            eventId,
            TransitionSchedulingAttempts);
        return false;
    }

    private static ValidationResult ValidateCompositeComponents(PackageResources resources, ConformanceState state)
    {
        var issues = new List<ValidationIssue>();

        var allCanonicals = new HashSet<string>(
            state.AllSearchParameters.Values.Select(sp => sp.Canonical)
                .Concat(resources.SearchParameters.Select(sp => sp.Canonical)));

        foreach (var composite in resources.SearchParameters.Where(sp => sp.Type == SearchParamType.Composite))
        {
            if (composite.Components is null)
            {
                issues.Add(new ValidationIssue(
                    "COMPOSITE_MISSING_COMPONENTS",
                    $"Composite SP '{composite.Code}': Components array is null or empty"));
                continue;
            }

            foreach (var component in composite.Components)
            {
                if (!allCanonicals.Contains(component.DefinitionUrl))
                {
                    issues.Add(new ValidationIssue(
                        "COMPOSITE_MISSING_COMPONENT",
                        $"Composite SP '{composite.Code}': Component '{component.DefinitionUrl}' not found"));
                }
            }
        }

        return issues.Count == 0 ? ValidationResult.Valid() : ValidationResult.Invalid(issues);
    }

    private ActivationResult RejectActivation(IReadOnlyList<ValidationIssue> issues)
    {
        _logger.LogWarning("Package activation validation failed: {Issues}",
            string.Join(", ", issues.Select(issue => issue.Message)));
        return ActivationResult.Failed(issues);
    }

    private bool IsValidOverride(SearchParameterInfo newSp, ActiveSearchParameter existing)
    {
        // Explicit derivedFrom relationship
        if (newSp.DerivedFrom == existing.Canonical)
        {
            return true;
        }

        // Same canonical URL (version update)
        if (newSp.Canonical == existing.Canonical)
        {
            return true;
        }

        // Priority-based override
        if (HasHigherPriority(newSp.SourcePackageId, existing.SourcePackage.Split('@')[0]))
        {
            return true;
        }

        return false;
    }

    private bool HasHigherPriority(string newPackageId, string existingPackageId)
    {
        var newRank = _options.GetPriorityRank(newPackageId);
        var existingRank = _options.GetPriorityRank(existingPackageId);
        return newRank < existingRank;
    }

    private (List<NewSourceEvent> Events, ValidationIssue? Issue) BuildAndValidateActivationEvents(
        string packageId,
        string version,
        PackageResources resources,
        ConformanceState staged)
    {
        var events = new List<NewSourceEvent>();
        var streamId = $"package:{packageId}@{version}";
        var packageKey = $"{packageId}@{version}";

        // Emit SearchParameter events (non-composite first, then composite)
        foreach (var sp in resources.SearchParameters.OrderBy(sp => sp.Type == SearchParamType.Composite ? 1 : 0))
        {
            foreach (var resourceType in sp.BaseResourceTypes)
            {
                var existing = staged.GetSearchParameter(resourceType, sp.Code);
                OverrideInfo? overrides = null;

                if (existing is not null)
                {
                    if (!IsValidOverride(sp, existing))
                    {
                        return (events, new ValidationIssue(
                            "SP_CONFLICT",
                            $"SearchParameter '{sp.Code}' on {resourceType} conflicts with existing from {existing.SourcePackage}",
                            resourceType, sp.Code));
                    }
                    overrides = new OverrideInfo(existing.OverridesCanonical ?? existing.Canonical, existing.SearchParamId);
                }

                var searchParamId = staged.GetSearchParamIdForActivation(sp.Canonical, existing);

                var componentData = sp.Components?.Select(c =>
                    new SearchParameterComponentData(c.DefinitionUrl, c.Expression)).ToList();

                var proposed = new NewSourceEvent(
                    streamId,
                    nameof(SearchParameterActivated),
                    new SearchParameterActivated(
                        sp.Canonical,
                        sp.Code,
                        resourceType,
                        sp.Expression,
                        sp.Type,
                        packageKey,
                        overrides,
                        searchParamId,
                        sp.TargetResourceTypes,
                        componentData,
                        sp.Name,
                        sp.Description));
                if (staged.ApplyProposedEvent(proposed) is { } issue)
                {
                    return (events, issue);
                }
                events.Add(proposed);
            }
        }

        // Emit StructureDefinition events
        foreach (var sd in resources.StructureDefinitions)
        {
            var proposed = new NewSourceEvent(
                streamId,
                nameof(StructureDefinitionActivated),
                new StructureDefinitionActivated(
                    sd.Canonical,
                    sd.Type,
                    sd.Kind,
                    packageKey,
                    sd.SnapshotJson));
            if (staged.ApplyProposedEvent(proposed) is { } issue)
            {
                return (events, issue);
            }
            events.Add(proposed);
        }

        // Emit package activated event
        var activatedResources = resources.SearchParameters
            .SelectMany(sp => sp.BaseResourceTypes.Select(rt => new ActivatedResource(rt, sp.Canonical)))
            .Concat(resources.StructureDefinitions.Select(sd => new ActivatedResource("StructureDefinition", sd.Canonical)))
            .ToList();

        var packageEvent = new NewSourceEvent(
            streamId,
            nameof(PackageActivated),
            new PackageActivated(packageId, version, activatedResources));
        if (staged.ApplyProposedEvent(packageEvent) is { } packageIssue)
        {
            return (events, packageIssue);
        }
        events.Add(packageEvent);

        return (events, null);
    }

    private List<string> DetectReindexRequirements(string packageKey)
    {
        return _state.AllSearchParameters.Values
            .Where(sp => sp.SourcePackage == packageKey && sp.Status == SearchParameterStatus.Pending)
            .Select(sp => sp.ResourceType)
            .Distinct()
            .ToList();
    }

    private static bool IsBaseFhirPackage(string packageId) =>
        packageId.StartsWith("hl7.fhir.r", StringComparison.OrdinalIgnoreCase) &&
        packageId.EndsWith(".core", StringComparison.OrdinalIgnoreCase);
}
