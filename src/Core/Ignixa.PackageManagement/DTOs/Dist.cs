namespace Ignixa.PackageManagement.DTOs;

/// <summary>
/// JSON deserialization model for distribution metadata from NPM registry.
/// </summary>
internal class Dist
{
    public string? Shasum { get; set; }
    public string? Tarball { get; set; }
}
