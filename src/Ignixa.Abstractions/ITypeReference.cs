// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// Represents a type reference from ElementDefinition.type in FHIR StructureDefinition.
/// Used for FHIRPath type resolution, especially for choice elements (value[x]).
/// </summary>
/// <remarks>
/// Corresponds to ElementDefinition.type from FHIR StructureDefinition.
/// See: https://hl7.org/fhir/R4/elementdefinition-definitions.html#ElementDefinition.type
///
/// For choice elements like Observation.value[x], this represents each possible type:
/// - { Code: "Quantity", Profile: null }
/// - { Code: "CodeableConcept", Profile: null }
/// - { Code: "string", Profile: null }
/// etc.
/// </remarks>
public interface ITypeReference
{
    /// <summary>
    /// FHIR type code (e.g., "string", "Quantity", "Reference").
    /// </summary>
    string Code { get; }

    /// <summary>
    /// Profile URL for constrained types (e.g., "http://hl7.org/fhir/StructureDefinition/SimpleQuantity").
    /// Null for unconstrained types.
    /// </summary>
    string? Profile { get; }

    /// <summary>
    /// Target profile URL for Reference types (ElementDefinition.type.targetProfile).
    /// Specifies which resource types the reference can point to.
    /// Null for non-Reference types.
    /// </summary>
    string? TargetProfile { get; }

    /// <summary>
    /// Aggregation mode for Reference types: contained | referenced | bundled.
    /// Null for non-Reference types.
    /// </summary>
    IReadOnlyList<string>? Aggregation { get; }

    /// <summary>
    /// Versioning support for Reference types: either | independent | specific.
    /// Null for non-Reference types.
    /// </summary>
    string? Versioning { get; }
}
