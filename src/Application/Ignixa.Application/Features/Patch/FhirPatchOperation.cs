using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Ignixa.Application.Features.Patch;

/// <summary>
/// Represents a single FHIR Patch operation extracted from a Parameters resource.
/// </summary>
public record FhirPatchOperation
{
    /// <summary>
    /// Operation type: Add, Insert, Delete, Replace, Move
    /// </summary>
    public required FhirPatchOperationType Type { get; init; }

    /// <summary>
    /// FHIRPath expression to target element (required for Add, Insert, Delete, Replace).
    /// For an add with a 'name' part, this is the parent element.
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// Name of the element to add beneath <see cref="Path"/> (the FHIRPath Patch 'name' part).
    /// Null for Ignixa's path-only add shorthand, where <see cref="Path"/> already names the target element.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Value to add, insert or replace: a <see cref="JsonNode"/> parsed from the value[x] part, or a
    /// primitive for internally constructed operations. Null when the value was supplied as
    /// <see cref="ValueParts"/>, until <see cref="FhirPatchOperationResolver"/> builds it.
    /// </summary>
    public object? Value { get; init; }

    /// <summary>
    /// The value[x] type suffix the value was supplied as (e.g. "DateTime" for valueDateTime),
    /// which names the concrete property when the target is a choice element.
    /// </summary>
    public string? ValueType { get; init; }

    /// <summary>
    /// Nested parts that describe an anonymous-type value (e.g. a BackboneElement such as List.entry)
    /// instead of a value[x]. Resolved against the schema into <see cref="Value"/> before execution.
    /// </summary>
    public IReadOnlyList<FhirPatchValuePart>? ValueParts { get; init; }

    /// <summary>
    /// The parent object a 'name'-form add writes into, located by <see cref="FhirPatchOperationResolver"/>.
    /// Null for every other operation.
    /// </summary>
    internal JsonObject? TargetParent { get; init; }

    /// <summary>
    /// The JSON property a 'name'-form add writes (the concrete choice property for choice elements).
    /// </summary>
    internal string? TargetProperty { get; init; }

    /// <summary>
    /// Whether the 'name'-form add target repeats, as declared by the schema. Null otherwise.
    /// </summary>
    internal bool? TargetIsCollection { get; init; }

    /// <summary>
    /// Index for Insert operation (0-based position)
    /// </summary>
    public int? Index { get; init; }

    /// <summary>
    /// Source path for Move operation
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Destination path for Move operation
    /// </summary>
    public string? Destination { get; init; }
}

/// <summary>
/// FHIR Patch operation types per FHIR R4 Section 3.1.0.7.1
/// </summary>
public enum FhirPatchOperationType
{
    /// <summary>
    /// Add a named child beneath the path: appends to a repeating element, or sets an absent
    /// non-repeating element. The path-only shorthand appends to the element the path names.
    /// </summary>
    Add,

    /// <summary>
    /// Insert an element at a specific position in a collection
    /// </summary>
    Insert,

    /// <summary>
    /// Remove an element from the resource
    /// </summary>
    Delete,

    /// <summary>
    /// Replace the value of an existing element
    /// </summary>
    Replace,

    /// <summary>
    /// Move an element from one position to another within a collection
    /// </summary>
    Move,
}
