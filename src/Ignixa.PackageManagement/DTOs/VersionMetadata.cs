namespace Ignixa.PackageManagement.DTOs;

/// <summary>
/// JSON deserialization model for version metadata from NPM registry.
/// </summary>
internal class VersionMetadata
{
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? Description { get; set; }
    public string? FhirVersion { get; set; }
    public string? Url { get; set; }
    public Dist? Dist { get; set; }
}
