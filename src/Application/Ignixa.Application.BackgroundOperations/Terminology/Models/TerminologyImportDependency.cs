namespace Ignixa.Application.BackgroundOperations.Terminology.Models;

/// <summary>
/// Persisted dependency information for one resource in a terminology import job.
/// References only resources in the same job; external dependencies retain importer partial semantics.
/// </summary>
public sealed record TerminologyImportDependency(
    long PackageResourceId,
    string Canonical,
    string ResourceType,
    IReadOnlyList<long> DependsOn);
