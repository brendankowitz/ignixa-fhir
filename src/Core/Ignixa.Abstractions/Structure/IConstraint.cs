// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// Represents a FHIR constraint (invariant) from StructureDefinition.
/// Used for FHIRPath-based validation (Validation Tier 2).
/// </summary>
/// <remarks>
/// Corresponds to ElementDefinition.constraint from FHIR StructureDefinition.
/// See: https://hl7.org/fhir/R4/elementdefinition-definitions.html#ElementDefinition.constraint
/// </remarks>
public interface IConstraint
{
    /// <summary>
    /// Unique identifier for the constraint (e.g., "dom-2", "pat-1").
    /// </summary>
    string Key { get; }

    /// <summary>
    /// FHIRPath expression that must evaluate to true.
    /// </summary>
    string Expression { get; }

    /// <summary>
    /// Human-readable description of the constraint.
    /// </summary>
    string? Human { get; }

    /// <summary>
    /// Severity: error | warning.
    /// </summary>
    string Severity { get; }

    /// <summary>
    /// XPath expression (legacy, FHIR R4 and earlier).
    /// Optional - may be null for R5+ constraints.
    /// </summary>
    string? Xpath { get; }
}
