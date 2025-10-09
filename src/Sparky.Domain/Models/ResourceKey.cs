namespace Sparky.Domain.Models;

/// <summary>
/// Identifies a FHIR resource by type, ID, and optional version.
/// </summary>
public record ResourceKey(
    string ResourceType,
    string Id,
    string? VersionId = null)
{
    /// <summary>
    /// Returns a string representation suitable for logging.
    /// </summary>
    public override string ToString() =>
        VersionId == null
            ? $"{ResourceType}/{Id}"
            : $"{ResourceType}/{Id}/_history/{VersionId}";
}
