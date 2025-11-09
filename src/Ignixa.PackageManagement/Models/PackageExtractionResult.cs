namespace Ignixa.PackageManagement.Models;

/// <summary>
/// Result of extracting resources from a FHIR package.
/// </summary>
public record PackageExtractionResult
{
    /// <summary>
    /// Package metadata from package.json.
    /// </summary>
    public required PackageManifest Manifest { get; init; }

    /// <summary>
    /// All extracted conformance resources.
    /// </summary>
    public required IReadOnlyList<ExtractedResource> Resources { get; init; }
}
