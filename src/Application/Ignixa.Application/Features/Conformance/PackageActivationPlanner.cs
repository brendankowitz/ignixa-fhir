// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Serialization;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Options;

namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Decides which events activate a package against the current conformance projection.
/// </summary>
/// <remarks>
/// Every proposed event is applied to a staging copy first, so a rejected package leaves the published
/// projection untouched. The caller holds the activation lock and has caught the projection up.
/// </remarks>
public sealed class PackageActivationPlanner(
    IOptions<SearchParameterResolutionOptions> options,
    IFhirVersionContext fhirVersionContext)
{
    /// <summary>
    /// Builds the activation events, or the issues that reject the package. No events and no issues means the
    /// package is already active.
    /// </summary>
    public (IReadOnlyList<NewSourceEvent> Events, IReadOnlyList<ValidationIssue> Issues) Plan(
        string packageId,
        string version,
        string? fhirVersion,
        PackageResources resources,
        ConformanceState state)
    {
        if (state.Packages.ContainsKey($"{packageId}@{version}"))
        {
            return ([], []);
        }

        var validation = ValidateCompositeComponents(resources, state);
        if (!validation.Success)
        {
            return ([], validation.Issues);
        }

        using var staged = state.CreateStagingCopy();
        var (events, issue) = BuildAndValidateActivationEvents(packageId, version, fhirVersion, resources, staged);
        return issue is null ? (events, []) : ([], [issue]);
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

    private bool IsValidOverride(SearchParameterInfo newSp, ActiveSearchParameter existing)
    {
        var existingPackageId = existing.SourcePackage.Split('@')[0];
        return newSp.DerivedFrom == existing.Canonical ||
            newSp.Canonical == existing.Canonical ||
            ConformanceState.IsBaseFhirPackage(existingPackageId) ||
            HasHigherPriority(newSp.SourcePackageId, existingPackageId);
    }

    private bool HasHigherPriority(string newPackageId, string existingPackageId)
    {
        var newRank = options.Value.GetPriorityRank(newPackageId);
        var existingRank = options.Value.GetPriorityRank(existingPackageId);
        return newRank < existingRank;
    }

    private (List<NewSourceEvent> Events, ValidationIssue? Issue) BuildAndValidateActivationEvents(
        string packageId,
        string version,
        string? fhirVersionString,
        PackageResources resources,
        ConformanceState staged)
    {
        var events = new List<NewSourceEvent>();
        var streamId = $"package:{packageId}@{version}";
        var packageKey = $"{packageId}@{version}";
        var proposedOwners = new Dictionary<(string ResourceType, string Code), SearchParameterInfo>();
        if (resources.SearchParameters.Count > 0 &&
            (string.IsNullOrWhiteSpace(fhirVersionString) ||
             FhirSpecificationExtensions.FromVersionString(fhirVersionString) == FhirVersion.Unspecified))
        {
            return (events, new ValidationIssue(
                "SP_UNKNOWN_FHIR_VERSION",
                $"Package {packageKey} declares SearchParameters for unknown FHIR version '{fhirVersionString ?? "(missing)"}'."));
        }

        // Every definition this package activates, the base owners it materialises included, belongs to the
        // package's FHIR version: tenants on other versions keep their own base definitions.
        FhirVersion? packageFhirVersion = resources.SearchParameters.Count > 0
            ? FhirSpecificationExtensions.FromVersionString(fhirVersionString!)
            : null;
        var activationVersion = packageFhirVersion?.ToVersionString();

        // Emit SearchParameter events (non-composite first, then composite)
        foreach (var sp in resources.SearchParameters.OrderBy(sp => sp.Type == SearchParamType.Composite ? 1 : 0))
        {
            var existingOwners = sp.BaseResourceTypes.ToDictionary(
                resourceType => resourceType,
                resourceType => staged.GetSearchParameter(resourceType, sp.Code) is { Status: not SearchParameterStatus.Disabled } owner
                    ? owner
                    : null);
            var baseParameters = ResolveBaseParameters(packageId, packageFhirVersion!.Value, sp);
            var storageRoots = sp.BaseResourceTypes
                .Select(resourceType =>
                    existingOwners[resourceType]?.OverridesCanonical ??
                    existingOwners[resourceType]?.Canonical ??
                    baseParameters.GetValueOrDefault(resourceType)?.Url.ToString())
                .Where(root => root is not null)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (storageRoots.Length > 1)
            {
                return (events, new ValidationIssue(
                    "SP_MIXED_BASE_SHADOW",
                    $"SearchParameter '{sp.Code}' resolves to different storage roots across its base resource types: {string.Join(", ", storageRoots)}",
                    string.Join(",", sp.BaseResourceTypes),
                    sp.Code));
            }

            var storageRoot = storageRoots.SingleOrDefault() ?? sp.Canonical;

            // Owned and base-backed types go first: activating sp on one binds sp.Canonical to the root's
            // SearchParamId, so the unowned types that follow resolve the same identity from the canonical.
            var orderedResourceTypes = sp.BaseResourceTypes
                .OrderByDescending(resourceType =>
                    existingOwners[resourceType] is not null || baseParameters.ContainsKey(resourceType))
                .ToArray();

            foreach (var resourceType in orderedResourceTypes)
            {
                var ownerKey = (resourceType, sp.Code);
                if (proposedOwners.TryGetValue(ownerKey, out var proposedOwner) &&
                    sp.DerivedFrom != proposedOwner.Canonical &&
                    sp.Canonical != proposedOwner.Canonical)
                {
                    return (events, new ValidationIssue(
                        "SP_CONFLICT",
                        $"SearchParameter '{sp.Code}' on {resourceType} conflicts with another definition in the package",
                        resourceType,
                        sp.Code));
                }
                proposedOwners[ownerKey] = sp;

                var existing = staged.GetSearchParameter(resourceType, sp.Code) is { Status: not SearchParameterStatus.Disabled } owner
                    ? owner
                    : null;
                if (existing is null &&
                    baseParameters.TryGetValue(resourceType, out var baseParameter))
                {
                    var baseCanonical = baseParameter.Url.ToString();
                    var baseSearchParamId = staged.GetSearchParamIdForActivation(baseCanonical, null);
                    var basePackage = GetBasePackageKey(packageFhirVersion!.Value);
                    var baseActivation = new NewSourceEvent(
                        streamId,
                        nameof(SearchParameterActivated),
                        new SearchParameterActivated(
                            baseCanonical,
                            baseParameter.Code,
                            resourceType,
                            baseParameter.Expression,
                            baseParameter.Type,
                            basePackage,
                            null,
                            baseSearchParamId,
                            baseParameter.TargetResourceTypes,
                            baseParameter.Component.Select(component =>
                                new SearchParameterComponentData(
                                    component.DefinitionUrl?.ToString() ?? throw new InvalidOperationException(
                                        $"Base composite {baseCanonical} has a component without a definition."),
                                    component.Expression)).ToList(),
                            baseParameter.Name,
                            baseParameter.Description,
                            activationVersion));
                    // The slot is unowned (or Disabled) and the base carries no override, which is all
                    // activation validation can reject, so a rejection here is a planner defect.
                    if (staged.ApplyProposedEvent(baseActivation) is { } baseIssue)
                    {
                        throw new InvalidOperationException(
                            $"Materialising base {baseCanonical} on {resourceType} was rejected: {baseIssue.Message}");
                    }
                    events.Add(baseActivation);
                    existing = staged.GetSearchParameter(resourceType, sp.Code);
                }

                OverrideInfo? overrides = null;

                if (existing is not null)
                {
                    // One (resourceType, code) has one owner across versions, so a definition for another
                    // version cannot take the code over without silently removing it from the owner's tenants.
                    if (!existing.SharesVersionWith(activationVersion))
                    {
                        return (events, new ValidationIssue(
                            "SP_FHIR_VERSION_CONFLICT",
                            $"SearchParameter '{sp.Code}' on {resourceType} is owned by {existing.Canonical} from {existing.SourcePackage} for FHIR {existing.FhirVersion}; a package for FHIR {activationVersion} cannot replace it",
                            resourceType,
                            sp.Code));
                    }

                    var latest = staged.GetLatestNonDisabledActivation(resourceType, sp.Code);
                    if (latest is not null && !IsValidOverride(sp, latest))
                    {
                        return (events, new ValidationIssue(
                            "SP_CONFLICT",
                            $"SearchParameter '{sp.Code}' on {resourceType} conflicts with existing from {latest.SourcePackage}",
                            resourceType, sp.Code));
                    }
                }

                // ConformanceState rejects an override whose id differs from its root's, so a wrong id fails
                // the activation (SP_STORAGE_IDENTITY) rather than splitting storage.
                var searchParamId = staged.GetSearchParamIdForActivation(sp.Canonical, existing);
                if (!string.Equals(storageRoot, sp.Canonical, StringComparison.Ordinal))
                {
                    overrides = new OverrideInfo(storageRoot, searchParamId);
                }
                else if (existing is not null)
                {
                    overrides = new OverrideInfo(
                        existing.OverridesCanonical ?? existing.Canonical,
                        existing.SearchParamId);
                }

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
                        sp.Description,
                        activationVersion));
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

    // Only reached for a package whose SearchParameters passed the known-FHIR-version check.
    private IReadOnlyDictionary<string, Ignixa.Search.Models.SearchParameterInfo> ResolveBaseParameters(
        string packageId,
        FhirVersion fhirVersion,
        SearchParameterInfo parameter)
    {
        if (ConformanceState.IsBaseFhirPackage(packageId) ||
            IntrinsicSearchParameters.IsIntrinsicCode(parameter.Code))
        {
            return new Dictionary<string, Ignixa.Search.Models.SearchParameterInfo>();
        }

        var baseManager = fhirVersionContext.GetSearchParameterDefinitionManager(fhirVersion);
        var baseParameters = new Dictionary<string, Ignixa.Search.Models.SearchParameterInfo>();
        foreach (var resourceType in parameter.BaseResourceTypes)
        {
            if (baseManager.TryGetSearchParameter(resourceType, parameter.Code, out var baseParameter))
            {
                baseParameters[resourceType] = baseParameter;
            }
        }

        return baseParameters;
    }

    private string GetBasePackageKey(FhirVersion fhirVersion)
    {
        var release = fhirVersion switch
        {
            FhirVersion.Stu3 => "r3",
            FhirVersion.R4 => "r4",
            FhirVersion.R4B => "r4b",
            FhirVersion.R5 => "r5",
            FhirVersion.R6 => "r6",
            _ => throw new ArgumentOutOfRangeException(nameof(fhirVersion), fhirVersion, "Unsupported FHIR version"),
        };
        return $"hl7.fhir.{release}.core@{fhirVersionContext.GetBaseSchemaProvider(fhirVersion).FullVersion}";
    }
}
