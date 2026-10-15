// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Diagnostics.CodeAnalysis;
using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.InMemory;

/// <summary>
/// Membership of indexed resources in FHIR compartments (<c>Patient/123</c>, <c>Encounter/e1</c>), decided from the FHIR
/// version's CompartmentDefinitions (<see cref="ICompartmentDefinitionManager"/>): a resource is in compartment
/// <c>Type/id</c> when one of the parameters the definition lists for its resource type references <c>Type/id</c>, or, for
/// <c>{def}</c>, when it is that resource itself. A resource type the definition lists without parameters is never in it.
/// </summary>
/// <remarks>
/// <para>
/// A reference counts when its index entry is a local <c>Type/id</c>, as <see cref="InMemorySearchEngine"/> follows chains:
/// one with a base URI points at another server, and a canonical (<see cref="CanonicalReferenceSearchIndexer"/>) never names
/// a compartment's resource, so neither makes a resource a member.
/// </para>
/// <para>
/// A listed code that is not a reference search parameter of the type (the STU3 Patient compartment names
/// <c>AuditEvent</c> <c>agent.patient</c> and <c>entity.patient</c>, <c>Provenance</c> <c>target.patient</c> and
/// <c>target.subject</c>) cannot be evaluated; membership of such a type is refused with
/// <see cref="SearchOperationNotSupportedException"/> rather than decided by the other codes alone, which could only
/// answer "not a member" wrongly. Decided from the definitions, so the refusal never depends on the data.
/// </para>
/// <para>Immutable after construction, so safe for concurrent use. Experimental (<c>IGNIXAEXP001</c>).</para>
/// </remarks>
[CLSCompliant(false)]
[Experimental(InMemoryExperimental.DiagnosticId)]
public sealed class InMemoryCompartmentMembership
{
    /// <summary>The CompartmentDefinition code for "the compartment's own resource" (R4 <c>Encounter</c>, R5 <c>Patient</c>).</summary>
    public const string FocalResourceCode = "{def}";

    private readonly Dictionary<string, Dictionary<string, (HashSet<string> References, bool Focal, IReadOnlyList<string> Unevaluable)>> _rules =
        new(StringComparer.Ordinal);

    /// <param name="compartments">The version's compartment definitions.</param>
    /// <param name="definitions">The search parameter definitions the index was extracted with, which the listed codes are resolved against.</param>
    public InMemoryCompartmentMembership(ICompartmentDefinitionManager compartments, ISearchParameterDefinitionManager definitions)
    {
        ArgumentNullException.ThrowIfNull(compartments);
        ArgumentNullException.ThrowIfNull(definitions);

        foreach (CompartmentType compartmentType in Enum.GetValues<CompartmentType>())
        {
            if (compartments.TryGetResourceTypes(compartmentType, out HashSet<string> resourceTypes))
            {
                _rules.Add(compartmentType.ToString(), resourceTypes.ToDictionary(type => type, type => Rule(compartments, definitions, compartmentType, type), StringComparer.Ordinal));
            }
        }

        CompartmentTypes = [.. _rules.Keys.Order(StringComparer.Ordinal)];
    }

    /// <summary>The compartment types the version defines, in ordinal order.</summary>
    public IReadOnlyList<string> CompartmentTypes { get; }

    /// <summary>The resource types that can be in a <paramref name="compartmentType"/> compartment, in ordinal order.</summary>
    /// <exception cref="ArgumentException"><paramref name="compartmentType"/> is not one of <see cref="CompartmentTypes"/>.</exception>
    public IReadOnlyList<string> ResourceTypes(string compartmentType) =>
        [.. RulesOf(compartmentType).Keys.Order(StringComparer.Ordinal)];

    /// <summary>
    /// The reference search parameter codes through which a <paramref name="resourceType"/> resource can be in a
    /// <paramref name="compartmentType"/> compartment, in ordinal order; empty when the type is never a member that way.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="compartmentType"/> is not one of <see cref="CompartmentTypes"/>.</exception>
    public IReadOnlyList<string> Parameters(string compartmentType, string resourceType)
    {
        ArgumentNullException.ThrowIfNull(resourceType);

        return RulesOf(compartmentType).TryGetValue(resourceType, out var rule) ? [.. rule.References.Order(StringComparer.Ordinal)] : [];
    }

    /// <summary>
    /// Whether a compartment's own resource (<c>Patient/123</c> in <c>Patient/123</c>'s compartment) is a member, which the
    /// definition says with <see cref="FocalResourceCode"/>: true for R4 <c>Encounter</c> and R5 <c>Patient</c>, false for R4 <c>Patient</c>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="compartmentType"/> is not one of <see cref="CompartmentTypes"/>.</exception>
    public bool IncludesCompartmentResource(string compartmentType) =>
        RulesOf(compartmentType).TryGetValue(compartmentType, out var rule) && rule.Focal;

    /// <summary>
    /// The codes the <paramref name="compartmentType"/> definition lists for <paramref name="resourceType"/> that are not
    /// reference search parameters of it, in ordinal order; membership of that type is refused when any exist.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="compartmentType"/> is not one of <see cref="CompartmentTypes"/>.</exception>
    public IReadOnlyList<string> UnevaluableParameters(string compartmentType, string resourceType)
    {
        ArgumentNullException.ThrowIfNull(resourceType);

        return RulesOf(compartmentType).TryGetValue(resourceType, out var rule) ? rule.Unevaluable : [];
    }

    /// <summary>Whether the indexed resource at <paramref name="location"/> is in compartment <paramref name="compartmentType"/>/<paramref name="compartmentId"/>.</summary>
    /// <param name="location">The resource's type and id.</param>
    /// <param name="index">The resource's index entries.</param>
    /// <param name="compartmentType">The compartment type, for example <c>Patient</c>.</param>
    /// <param name="compartmentId">The id of the compartment's resource.</param>
    /// <exception cref="ArgumentException"><paramref name="compartmentType"/> is not one of <see cref="CompartmentTypes"/>, or <paramref name="compartmentId"/> is empty.</exception>
    /// <exception cref="SearchOperationNotSupportedException">The definition lists a code for the resource's type that cannot be evaluated (see <see cref="UnevaluableParameters"/>).</exception>
    public bool IsInCompartment(ResourceKey location, IReadOnlyCollection<SearchIndexEntry> index, string compartmentType, string compartmentId)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentException.ThrowIfNullOrEmpty(compartmentId);

        if (!RulesOf(compartmentType).TryGetValue(location.ResourceType, out var rule))
        {
            return false;
        }

        ThrowIfUnevaluable(compartmentType, location.ResourceType, rule.Unevaluable);
        if (rule.Focal && location.ResourceType == compartmentType && location.Id == compartmentId)
        {
            return true;
        }

        return index.Any(entry =>
            rule.References.Contains(entry.SearchParameter.Code)
            && entry.Value is ReferenceSearchValue { BaseUri: null } reference
            && string.Equals(reference.ResourceType, compartmentType, StringComparison.Ordinal)
            && string.Equals(reference.ResourceId, compartmentId, StringComparison.Ordinal));
    }

    /// <summary>Refuses membership of <paramref name="resourceType"/> in a <paramref name="compartmentType"/> compartment when it cannot be evaluated.</summary>
    /// <exception cref="ArgumentException"><paramref name="compartmentType"/> is not one of <see cref="CompartmentTypes"/>.</exception>
    /// <exception cref="SearchOperationNotSupportedException">See <see cref="UnevaluableParameters"/>.</exception>
    public void ThrowIfUnevaluable(string compartmentType, string resourceType) =>
        ThrowIfUnevaluable(compartmentType, resourceType, UnevaluableParameters(compartmentType, resourceType));

    private static void ThrowIfUnevaluable(string compartmentType, string resourceType, IReadOnlyList<string> unevaluable)
    {
        if (unevaluable.Count > 0)
        {
            throw new SearchOperationNotSupportedException(
                $"Membership of {resourceType} in a {compartmentType} compartment cannot be evaluated: its CompartmentDefinition lists {string.Join(", ", unevaluable)}, which {(unevaluable.Count == 1 ? "is not a reference search parameter" : "are not reference search parameters")} of {resourceType}.");
        }
    }

    private Dictionary<string, (HashSet<string> References, bool Focal, IReadOnlyList<string> Unevaluable)> RulesOf(string compartmentType)
    {
        ArgumentNullException.ThrowIfNull(compartmentType);

        return _rules.TryGetValue(compartmentType, out var rules)
            ? rules
            : throw new ArgumentException($"'{compartmentType}' is not a compartment type of this FHIR version; the compartment types are {string.Join(", ", CompartmentTypes)}.", nameof(compartmentType));
    }

    private static (HashSet<string> References, bool Focal, IReadOnlyList<string> Unevaluable) Rule(
        ICompartmentDefinitionManager compartments,
        ISearchParameterDefinitionManager definitions,
        CompartmentType compartmentType,
        string resourceType)
    {
        compartments.TryGetSearchParams(resourceType, compartmentType, out HashSet<string> codes);
        HashSet<string> references = new(StringComparer.Ordinal);
        List<string> unevaluable = [];
        foreach (string code in codes.Where(code => code != FocalResourceCode))
        {
            if (definitions.TryGetSearchParameter(resourceType, code, out SearchParameterInfo parameter) && parameter.Type == SearchParamType.Reference)
            {
                references.Add(code);
            }
            else
            {
                unevaluable.Add(code);
            }
        }

        return (references, codes.Contains(FocalResourceCode), [.. unevaluable.Order(StringComparer.Ordinal)]);
    }
}
