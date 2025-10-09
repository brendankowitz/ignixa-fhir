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
}
