using System.Text.Json;

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
    /// FHIRPath expression to target element (required for Add, Insert, Delete, Replace)
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// Value to set (required for Add, Insert, Replace; omit for Delete, Move)
    /// Can be a primitive (string, int, bool) or a JsonElement for complex types
    /// </summary>
    public object? Value { get; init; }

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
    /// Add a new element to a collection (0..* cardinality)
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
