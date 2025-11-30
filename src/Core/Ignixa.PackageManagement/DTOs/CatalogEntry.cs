namespace Ignixa.PackageManagement.DTOs;

/// <summary>
/// JSON deserialization model for catalog entries from NPM registry.
/// </summary>
internal class CatalogEntry
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? FhirVersion { get; set; }
}
