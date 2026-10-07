using Ignixa.Serialization.SourceNodes;

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
    /// Client's required current version. Storage must compare this at the atomic write boundary,
    /// independently of the server-assigned version stored in the resource payload.
    /// </summary>
    public string? ExpectedVersionId { get; init; }

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

    /// <summary>
    /// Optional: TTL expiration timestamp set via X-TTL header.
    /// Null means resource lives forever, non-null means resource expires at this timestamp.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// Per-semantic-search-parameter vector indices computed on the write path. <c>null</c> means this
    /// resource's write is not evaluated at all: semantic search is disabled, this write path does not
    /// run it (e.g. import), or -- the common case for most resource types -- this resource type carries
    /// no active semantic search parameter in the tenant's definition manager, so there is nothing to
    /// embed and no point asking the merge repository to even check. Any vectors already persisted for
    /// this resource must be left untouched when <c>null</c>. An empty list means this resource type IS
    /// evaluated (it has at least one active semantic search parameter) and this write's extraction found
    /// no semantic text to embed, either because no value matched or because a prior version's semantic
    /// text was removed -- any vectors already persisted for this resource must be deleted in that case.
    /// Always <c>[]</c> rather than <c>null</c> for a deleted wrapper (<see cref="IsDeleted"/>) of an
    /// evaluated type, since a tombstone has no indexable text but still needs its prior vectors deleted;
    /// <c>null</c> for a deleted wrapper of a type with no semantic search parameter at all, since there
    /// is nothing for the type to ever have had.
    /// </summary>
    public IReadOnlyList<VectorIndexEntry>? VectorIndices { get; init; }
}
