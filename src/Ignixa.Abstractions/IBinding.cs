// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// Represents a FHIR terminology binding from StructureDefinition.
/// Used for terminology validation (Validation Tier 3).
/// </summary>
/// <remarks>
/// Corresponds to ElementDefinition.binding from FHIR StructureDefinition.
/// See: https://hl7.org/fhir/R4/elementdefinition-definitions.html#ElementDefinition.binding
/// </remarks>
public interface IBinding
{
    /// <summary>
    /// Binding strength: required | extensible | preferred | example.
    /// </summary>
    string Strength { get; }

    /// <summary>
    /// ValueSet canonical URL (e.g., "http://hl7.org/fhir/ValueSet/administrative-gender").
    /// </summary>
    string? ValueSet { get; }

    /// <summary>
    /// Human-readable description of the binding.
    /// </summary>
    string? Description { get; }
}
