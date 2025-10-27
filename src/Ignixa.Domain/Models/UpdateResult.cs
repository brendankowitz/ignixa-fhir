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
    /// Optional request context (HTTP method, URL) associated with this result.
    /// </summary>
    public ResourceRequest? Request { get; init; }
}
