using Ignixa.SourceNodeSerialization.SourceNodes;

namespace Ignixa.Domain.Models;

/// <summary>
/// Wraps a FHIR resource with metadata (version, timestamps, request information).
/// Uses ISourceNode for memory-efficient resource representation.
/// </summary>
public record ResourceWrapper(
    string ResourceType,
    string ResourceId,
    string VersionId,
    DateTimeOffset LastModified,
    ResourceJsonNode Resource,
    ResourceRequest Request,
    bool IsDeleted = false)
{
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
