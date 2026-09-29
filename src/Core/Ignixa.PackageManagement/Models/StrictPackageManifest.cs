namespace Ignixa.PackageManagement.Models;

/// <summary>Declared metadata only; absent FHIR versions are not inferred.</summary>
/// <param name="Name">Declared nonblank package name; not a validated source identity.</param>
/// <param name="Version">Declared nonblank version; no SemVer normalization.</param>
/// <param name="FhirVersion">Singular declaration, or null when absent.</param>
/// <param name="FhirVersions">Plural declarations in order; empty when absent. Extraction returns a read-only collection.</param>
/// <param name="Json">Original manifest JSON, including fields not projected by this DTO.</param>
/// <remarks>Direct construction does not validate metadata. Record equality is not structural list equality or artifact identity.</remarks>
public sealed record StrictPackageManifest(
    string Name,
    string Version,
    string? FhirVersion,
    IReadOnlyList<string> FhirVersions,
    string Json);
