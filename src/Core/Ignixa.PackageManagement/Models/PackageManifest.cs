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

    /// <summary>
    /// Transitive package dependencies declared in <c>package.json</c> as a map of
    /// package id → version (e.g. <c>{"hl7.fhir.r4.core":"4.0.1"}</c>). Null when the
    /// source manifest carried no <c>dependencies</c> object.
    /// </summary>
    /// <remarks>
    /// Consumers performing transitive IG resolution walk this map recursively, loading
    /// each (id, version) and the dependencies of the resulting manifests, until the
    /// closure stabilises.
    /// </remarks>
    public IReadOnlyDictionary<string, string> Dependencies { get; init; } = System.Collections.Frozen.FrozenDictionary<string, string>.Empty;
}
