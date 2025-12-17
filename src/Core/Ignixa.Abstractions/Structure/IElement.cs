// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// Represents a single element in the FHIR element tree (runtime instance).
/// </summary>
/// <remarks>
/// This interface provides the minimal metadata required for:
/// - FHIRPath evaluation
/// - FHIR validation (Tier 1/2)
/// - Serialization (JSON)
/// - Error reporting
///
/// PERFORMANCE: Uses <see cref="IReadOnlyList{T}"/> for Children() instead of ReadOnlySpan
/// to provide a safe, efficient API that doesn't have span lifetime constraints.
/// </remarks>
public interface IElement
{
    /// <summary>
    /// Element name (e.g., "name", "birthDate", "valueQuantity").
    /// For choice elements, this is the typed name (e.g., "valueQuantity" not "value").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Primitive value for primitive types, null for complex types.
    /// </summary>
    /// <remarks>
    /// Type mapping:
    /// - boolean → bool
    /// - integer → int
    /// - string/code/id/markdown/url/canonical/uuid → string
    /// - decimal → decimal
    /// - dateTime/date/instant → DateTimeOffset or string
    /// - base64Binary → byte[] or string
    /// </remarks>
    object? Value { get; }

    /// <summary>
    /// Runtime type name (e.g., "HumanName", "string", "Patient").
    /// Used for FHIRPath type checking and validation.
    /// </summary>
    string InstanceType { get; }

    /// <summary>
    /// Dotted location for error reporting (e.g., "Patient.name[0].family").
    /// Format follows FHIR validation error location convention.
    /// </summary>
    string Location { get; }

    /// <summary>
    /// Type metadata from StructureDefinition (may be null for dynamic/unknown types).
    /// </summary>
    IType? Type { get; }

    /// <summary>
    /// Returns child elements with the specified name.
    /// </summary>
    /// <param name="name">
    /// Element name to filter by. If null, returns all children.
    /// For choice elements (e.g., "value"), matches ALL typed variants
    /// (valueString, valueQuantity, etc.) following FHIRPath semantics.
    /// </param>
    /// <returns>
    /// Read-only list of matching child elements (may be empty).
    /// The returned collection is safe to store and iterate multiple times.
    /// </returns>
    /// <remarks>
    /// Choice element semantics (FHIR spec compliant):
    /// - Children("value") → returns valueQuantity if present
    /// - Children("valueQuantity") → exact match only
    /// - Children(null) → all children
    /// </remarks>
    IReadOnlyList<IElement> Children(string? name = null);

    /// <summary>
    /// Retrieves metadata of the specified type.
    /// Used for attaching metadata (e.g., source JsonNode, validation state).
    /// </summary>
    /// <typeparam name="T">Metadata type to retrieve</typeparam>
    /// <returns>Metadata instance or null if not present</returns>
    T? Meta<T>() where T : class;
}
