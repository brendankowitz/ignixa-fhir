using Ignixa.Abstractions;

namespace Ignixa.Domain.Models;

/// <summary>
/// Result of a create or update operation.
/// Contains the resource identifier, raw bytes, and metadata needed for HTTP response.
/// Lighter-weight than SearchEntryResult - only deserialize if needed.
/// </summary>
public record UpdateResult(
    ResourceKey Key,
    ReadOnlyMemory<byte> ResourceBytes,
    DateTimeOffset LastModified)
{
    /// <summary>
    /// True when the write creates a live resource, including recreation after deletion.
    /// Null preserves version-one inference for storage providers not reporting this distinction.
    /// </summary>
    public bool? IsCreated { get; init; }

    /// <summary>
    /// Optional request context (HTTP method, URL) associated with this result.
    /// </summary>
    public ResourceRequest? Request { get; init; }
}
