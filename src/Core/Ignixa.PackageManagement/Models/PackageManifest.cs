namespace Ignixa.PackageManagement.Models;

/// <summary>
/// Represents the metadata from a FHIR NPM package.json file.
/// </summary>
public record PackageManifest
{
    /// <summary>
    /// Package name (e.g., "hl7.fhir.us.core").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Package version (e.g., "5.0.1").
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// FHIR version this package is for (e.g., "4.0.1", "5.0.0").
    /// </summary>
    public required string FhirVersion { get; init; }

    /// <summary>
    /// Package title/description.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Package description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// License (e.g., "CC0-1.0").
    /// </summary>
    public string? License { get; init; }
}
