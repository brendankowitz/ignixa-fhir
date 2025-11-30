namespace Ignixa.PackageManagement.Models;

/// <summary>
/// Represents a FHIR resource extracted from a package.
/// </summary>
public record ExtractedResource
{
    /// <summary>
    /// Resource type (e.g., "StructureDefinition", "ValueSet").
    /// </summary>
    public required string ResourceType { get; init; }

    /// <summary>
    /// Canonical URL (e.g., "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient").
    /// </summary>
    public required string Canonical { get; init; }

    /// <summary>
    /// Resource business version.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// Logical resource ID.
    /// </summary>
    public required string ResourceId { get; init; }

    /// <summary>
    /// Full JSON representation of the resource.
    /// </summary>
    public required string ResourceJson { get; init; }

    /// <summary>
    /// FHIR version (e.g., "4.0.1").
    /// </summary>
    public required string FhirVersion { get; init; }
}
