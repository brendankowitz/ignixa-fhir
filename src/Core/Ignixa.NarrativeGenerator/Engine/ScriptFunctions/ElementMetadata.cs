// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.NarrativeGenerator.Engine.ScriptFunctions;

/// <summary>
/// Metadata about a FHIR element for template rendering.
/// </summary>
internal sealed class ElementMetadata
{
    /// <summary>Element name (e.g., "name", "birthDate").</summary>
    public required string Name { get; init; }

    /// <summary>FHIRPath-compatible path (e.g., "Patient.name").</summary>
    public required string Path { get; init; }

    /// <summary>FHIR type code (e.g., "string", "HumanName", "CodeableConcept").</summary>
    public required string Type { get; init; }

    /// <summary>True if this is a primitive type (string, integer, boolean, etc.).</summary>
    public bool IsPrimitive { get; init; }

    /// <summary>True if this is a CodeableConcept.</summary>
    public bool IsCodeableConcept { get; init; }

    /// <summary>True if this is a Reference.</summary>
    public bool IsReference { get; init; }

    /// <summary>True if this is a Quantity.</summary>
    public bool IsQuantity { get; init; }

    /// <summary>True if this element can repeat (cardinality > 1).</summary>
    public bool IsArray { get; init; }

    /// <summary>Minimum cardinality (0 or 1).</summary>
    public int Min { get; init; }

    /// <summary>Maximum cardinality (1 or "*" represented as int.MaxValue).</summary>
    public int Max { get; init; }

    /// <summary>Element definition (short description).</summary>
    public string? Description { get; init; }
}
