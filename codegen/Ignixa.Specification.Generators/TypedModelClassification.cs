// <copyright file="TypedModelClassification.cs" company="Ignixa Contributors">
//     Copyright (c) Ignixa Contributors. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

namespace Ignixa.Specification.Generators;

/// <summary>How a type or element is shared across the targeted FHIR versions.</summary>
internal enum SharingBucket
{
    /// <summary>Present in every targeted version with an identical signature.</summary>
    Identical,

    /// <summary>Shared elements are identical; one or more versions add extra elements/types.</summary>
    Additive,

    /// <summary>A shared element differs (retype / recardinality / rebinding) between versions.</summary>
    Incompatible,
}

/// <summary>The structural kind of a generated facade.</summary>
internal enum FacadeKind
{
    DomainResource,
    Resource,
    Datatype,
    Backbone,
}

/// <summary>
/// A version-independent signature for a single element, used to decide whether two versions agree on
/// it. Two elements are compatible iff their signatures are equal.
/// </summary>
internal sealed record ElementSignature(
    string TypeCode,
    bool IsArray,
    bool IsChoice,
    string? ValueSetUrl,
    string? VariantTypeCodes);

/// <summary>Per-version facts about one element of one type.</summary>
internal sealed record ElementFacts(
    string JsonName,
    ElementSignature Signature,
    Hl7.Fhir.Model.ElementDefinition Element,
    Hl7.Fhir.Model.StructureDefinition StructureDefinition);

/// <summary>
/// The merged, cross-version view of a single element: its name, the per-version signatures, and the
/// bucket it falls into. <see cref="EmittedInVersions"/> lists the versions whose subclass must carry
/// the element (empty when it lives on the base type).
/// </summary>
internal sealed class ElementClassification
{
    public required string JsonName { get; init; }

    public required SharingBucket Bucket { get; init; }

    /// <summary>The signature used when the element is emitted on the base type (Identical only).</summary>
    public ElementSignature? BaseSignature { get; init; }

    /// <summary>Per-version facts (signature + raw ElementDefinition), keyed by version.</summary>
    public required IReadOnlyDictionary<string, ElementFacts> PerVersion { get; init; }

    /// <summary>Versions that must emit this element on their subclass (empty when base-only).</summary>
    public required IReadOnlyList<string> EmittedInVersions { get; init; }
}

/// <summary>
/// The merged, cross-version view of a single type (resource, datatype, or backbone).
/// </summary>
internal sealed class TypeClassification
{
    public required string TypeName { get; init; }

    public required FacadeKind Kind { get; init; }

    public required SharingBucket Bucket { get; init; }

    /// <summary>Versions in which this type exists at all.</summary>
    public required IReadOnlyList<string> PresentInVersions { get; init; }

    /// <summary>
    /// Versions that must emit a subclass for this type. Empty for an Identical type (base used directly).
    /// </summary>
    public required IReadOnlyList<string> SubclassVersions { get; init; }

    /// <summary>Elements that belong on the base type (Identical across all present versions).</summary>
    public required IReadOnlyList<ElementClassification> BaseElements { get; init; }

    /// <summary>Elements that belong on version subclasses (Additive or Incompatible).</summary>
    public required IReadOnlyList<ElementClassification> SubclassElements { get; init; }

    /// <summary>True when the base type should be emitted into the shared Ignixa.Models layer.</summary>
    public bool HasBaseType => PresentInVersions.Count > 0;
}
