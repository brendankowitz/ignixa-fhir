namespace Ignixa.PackageManagement.DTOs;

/// <summary>
/// JSON deserialization model for package metadata from NPM registry.
/// </summary>
internal class PackageMetadata
{
    [System.Text.Json.Serialization.JsonPropertyName("_id")]
    public string? Id { get; set; }

    public string? Name { get; set; }
    public string? Description { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("dist-tags")]
    public DistTags? DistTags { get; set; }

    public Dictionary<string, VersionMetadata>? Versions { get; set; }
}
