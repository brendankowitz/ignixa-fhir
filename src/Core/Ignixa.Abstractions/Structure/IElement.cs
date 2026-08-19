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
    /// <para>
    /// Canonical mapping, as produced by <c>SchemaAwareElement</c>:
    /// </para>
    /// <list type="bullet">
    /// <item><description>boolean → bool</description></item>
    /// <item><description>integer/unsignedInt/positiveInt → int</description></item>
    /// <item><description>decimal → decimal</description></item>
    /// <item><description>date/dateTime/instant/time → <see cref="FhirTemporal"/>, which carries the
    /// declared precision and timezone presence alongside the original wire text; falls back to the
    /// wire-format string when the literal is unparseable</description></item>
    /// <item><description>every other primitive, including integer64 and base64Binary → its FHIR
    /// wire-format string</description></item>
    /// </list>
    /// <para>
    /// Consumers must additionally tolerate these alternatives, which other implementations do
    /// produce: <c>string</c>, <c>DateTimeOffset</c> or <c>DateTime</c> for
    /// date/dateTime/instant/time, <c>byte[]</c> for base64Binary, and <c>long</c> for integer64.
    /// Note that <c>SchemaAwareElement</c> does not yet emit <c>long</c> for integer64 even though
    /// the FHIRPath evaluator treats it as a first-class numeric, so the two differ for that type.
    /// </para>
    /// <para>
    /// This is a real contract, not documentation: <c>Ignixa.Extensions.FirelySdk</c> translates
    /// against it when crossing into the Firely SDK, and the FHIRPath comparison helpers narrow
    /// their operands to exactly these types. Temporal operands are normalised back to
    /// <see cref="FhirTemporal"/> whichever of the tolerated forms they arrive in, so comparison
    /// semantics do not depend on which implementation produced the element. A value outside the
    /// list is not rejected - it falls through to an empty collection, which FHIRPath's
    /// empty-propagation then hides.
    /// </para>
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

    /// <summary>
    /// Indicates whether this element has a primitive value node in the source.
    /// </summary>
    /// <remarks>
    /// In FHIR JSON, a primitive element can have:
    /// - A value only: {"birthDate": "1974-12-25"}
    /// - Extensions only (shadow property): {"_birthDate": {"extension": [...]}}
    /// - Both: {"birthDate": "1974-12-25", "_birthDate": {"extension": [...]}}
    ///
    /// This property returns true only when there's an actual primitive value,
    /// not just a shadow property with extensions.
    /// </remarks>
    bool HasPrimitiveValue { get; }
}
