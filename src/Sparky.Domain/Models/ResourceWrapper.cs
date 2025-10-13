using Hl7.Fhir.ElementModel;

namespace Sparky.Domain.Models;

/// <summary>
/// Wraps a FHIR resource with metadata (version, timestamps, request information).
/// Uses ISourceNode for memory-efficient resource representation.
/// </summary>
public record ResourceWrapper(
    string ResourceType,
    string ResourceId,
    string VersionId,
    DateTimeOffset LastModified,
    ISourceNode Resource,
    ResourceRequest Request,
    bool IsDeleted = false)
{
    /// <summary>
    /// Optional: Raw JSON representation for prototype simplicity.
    /// In production, serialize from Resource as needed.
    /// </summary>
    public string? RawJson { get; init; }

    /// <summary>
    /// Optional: Raw JSON bytes for zero-copy serialization.
    /// Enables streaming without parsing/re-serializing JSON.
    /// </summary>
    public ReadOnlyMemory<byte>? RawJsonBytes { get; init; }

    /// <summary>
    /// Optional: FHIR version of the resource (e.g., "4.0" for R4, "5.0" for R5).
    /// Defaults to "4.0" (R4) if not specified.
    /// </summary>
    public string FhirVersion { get; init; } = "4.0";

    /// <summary>
    /// Optional: Tenant identifier (0, 1, 2, ...) for multi-tenant isolation.
    /// Null indicates single-tenant/default mode.
    /// </summary>
    public int? TenantId { get; init; }

    /// <summary>
    /// Optional: Search index entries extracted from the resource.
    /// Used for search parameter indexing.
    /// </summary>
    public IReadOnlyList<object>? SearchIndices { get; init; }
}
