namespace Ignixa.PackageManagement.Models;

/// <summary>
/// A non-manifest JSON entry. Null ResourceType identifies metadata JSON, not a FHIR resource.
/// Canonical, id and version are deliberately left in the original JSON for downstream validation.
/// </summary>
/// <param name="Path">Validated effective archive path, without lossy reader name trimming.</param>
/// <param name="Json">Original UTF-8-decoded JSON, without reserialization.</param>
/// <param name="ResourceType">Declared nonblank string, including unknown types, or null for metadata.</param>
/// <remarks>This is an output DTO; direct construction does not validate an archive or FHIR identity.</remarks>
public sealed record StrictPackageEntry(string Path, string Json, string? ResourceType);
