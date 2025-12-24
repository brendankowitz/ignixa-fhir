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
    IFhirVersionContext fhirVersionContext,
    IOptions<SearchParameterResolutionOptions> options,
    ILogger<PackageActivationPipeline> logger)
{
    private readonly IPackageResourceRepository _packageRepo = packageRepo ?? throw new ArgumentNullException(nameof(packageRepo));
    private readonly ISourceEventStore _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
    private readonly ConformanceState _state = state ?? throw new ArgumentNullException(nameof(state));
    private readonly IFhirVersionContext _fhirVersionContext = fhirVersionContext ?? throw new ArgumentNullException(nameof(fhirVersionContext));
    private readonly SearchParameterResolutionOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
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

        // Acquire lock for entire activation to ensure thread safety
        using var _ = await _state.AcquireActivationLockAsync(cancellationToken);

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

        // 1. Load package resources from repository
        var packageResources = await _packageRepo.GetResourcesForActivationAsync(packageId, version, cancellationToken);
        var resources = PackageResourceMapper.MapToPackageResources(packageResources);

        _logger.LogDebug(
            "Loaded {SearchParamCount} SearchParameters and {StructureDefCount} StructureDefinitions",
            resources.SearchParameters.Count,
            resources.StructureDefinitions.Count);

        // 2. Validate against current state
        var validation = ValidateActivation(resources, _state);
        if (!validation.Success)
        {
            _logger.LogWarning(
                "Package activation validation failed: {Issues}",
                string.Join(", ", validation.Issues.Select(i => i.Message)));
            return ActivationResult.Failed(validation.Issues);
        }

        // 3. Build activation events
        var events = BuildActivationEvents(packageId, version, resources);

        _logger.LogDebug("Built {EventCount} activation events", events.Count);

        // 4. Append events atomically to event store and get assigned EventIds
        var persistedEvents = await _eventStore.AppendAsync(events, cancellationToken);

        // 5. Apply events with correct EventIds to in-memory state
        foreach (var evt in persistedEvents)
        {
            _state.ApplyAndTrack(evt);
        }

        // 6. Invalidate search parameter caches so new parameters are visible
        _fhirVersionContext.InvalidateSearchParameterCaches();

        // 7. Detect reindex requirements
        var reindexNeeded = DetectReindexRequirements(resources);

        _logger.LogInformation(
            "Package {PackageId}@{Version} activated successfully. Pending reindex: {Count} resource types",
            packageId,
            version,
            reindexNeeded.Count);

        return ActivationResult.Succeeded(reindexNeeded);
    }

    private ValidationResult ValidateActivation(PackageResources resources, ConformanceState state)
    {
        var issues = new List<ValidationIssue>();

        // Phase 1: Validate non-composite SearchParameters
        foreach (var sp in resources.SearchParameters.Where(sp => sp.Type != SearchParamType.Composite))
        {
            foreach (var resourceType in sp.BaseResourceTypes)
            {
                var existing = state.GetSearchParameter(resourceType, sp.Code);
                if (existing is not null && !IsValidOverride(sp, existing))
                {
                    issues.Add(new ValidationIssue(
                        "SP_CONFLICT",
                        $"SearchParameter '{sp.Code}' on {resourceType} conflicts with existing from {existing.SourcePackage}",
                        resourceType,
                        sp.Code));
                }
            }
        }

        // Phase 2: Validate composite SearchParameters (components must exist)
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

    private List<NewSourceEvent> BuildActivationEvents(
        string packageId,
        string version,
        PackageResources resources)
    {
        var events = new List<NewSourceEvent>();
        var streamId = $"package:{packageId}@{version}";
        var packageKey = $"{packageId}@{version}";

        // Emit SearchParameter events (non-composite first, then composite)
        foreach (var sp in resources.SearchParameters.OrderBy(sp => sp.Type == SearchParamType.Composite ? 1 : 0))
        {
            foreach (var resourceType in sp.BaseResourceTypes)
            {
                var existing = _state.GetSearchParameter(resourceType, sp.Code);
                OverrideInfo? overrides = null;

                if (existing is not null && IsValidOverride(sp, existing))
                {
                    overrides = new OverrideInfo(existing.Canonical, existing.SearchParamId);
                }

                var searchParamId = _state.GetOrAllocateSearchParamId(sp.Canonical, existing);

                var componentData = sp.Components?.Select(c =>
                    new SearchParameterComponentData(c.DefinitionUrl, c.Expression)).ToList();

                events.Add(new NewSourceEvent(
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
                        sp.Description)));
            }
        }

        // Emit StructureDefinition events
        foreach (var sd in resources.StructureDefinitions)
        {
            events.Add(new NewSourceEvent(
                streamId,
                nameof(StructureDefinitionActivated),
                new StructureDefinitionActivated(
                    sd.Canonical,
                    sd.Type,
                    sd.Kind,
                    packageKey,
                    sd.SnapshotJson)));
        }

        // Emit package activated event
        var activatedResources = resources.SearchParameters
            .SelectMany(sp => sp.BaseResourceTypes.Select(rt => new ActivatedResource(rt, sp.Canonical)))
            .Concat(resources.StructureDefinitions.Select(sd => new ActivatedResource("StructureDefinition", sd.Canonical)))
            .ToList();

        events.Add(new NewSourceEvent(
            streamId,
            nameof(PackageActivated),
            new PackageActivated(packageId, version, activatedResources)));

        return events;
    }

    private List<string> DetectReindexRequirements(PackageResources resources)
    {
        // Non-base-FHIR SearchParameters need reindexing
        return resources.SearchParameters
            .Where(sp => !IsBaseFhirPackage(sp.SourcePackageId))
            .SelectMany(sp => sp.BaseResourceTypes)
            .Distinct()
            .ToList();
    }

    private static bool IsBaseFhirPackage(string packageId) =>
        packageId.StartsWith("hl7.fhir.r", StringComparison.OrdinalIgnoreCase) &&
        packageId.EndsWith(".core", StringComparison.OrdinalIgnoreCase);
}
