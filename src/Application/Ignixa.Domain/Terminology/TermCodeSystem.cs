namespace Ignixa.Domain.Terminology;

/// <summary>
/// Metadata for a FHIR CodeSystem extracted from PackageResource for fast lookups.
/// References the full CodeSystem JSON stored in PackageResource.
/// </summary>
public class TermCodeSystem
{
    /// <summary>
    /// Surrogate primary key.
    /// </summary>
    public long TermCodeSystemId { get; set; }

    /// <summary>
    /// Foreign key to PackageResource table (where full CodeSystem JSON is stored).
    /// </summary>
    public required long PackageResourceId { get; set; }

    /// <summary>
    /// Foreign key to System table (canonical URL of CodeSystem).
    /// Reuses existing System table to avoid duplicate URL storage.
    /// </summary>
    public required int SystemId { get; set; }

    /// <summary>
    /// Business version of the CodeSystem (from CodeSystem.version).
    /// Example: "2.74" for LOINC.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Number of root concepts in this CodeSystem (from CodeSystem.count or concept.length).
    /// </summary>
    public int ConceptCount { get; set; }

    /// <summary>
    /// Content mode: complete, example, fragment, not-present, supplement.
    /// Determines whether concepts should be imported.
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// True if this CodeSystem is hierarchical (has parent/child relationships).
    /// Enables $subsumes operation.
    /// </summary>
    public bool IsHierarchical { get; set; }

    /// <summary>
    /// True if codes are case-sensitive (from CodeSystem.caseSensitive).
    /// Affects code matching in $validate-code operations.
    /// </summary>
    public bool CaseSensitive { get; set; }

    /// <summary>
    /// True if this CodeSystem is compositional (supports post-coordination).
    /// </summary>
    public bool Compositional { get; set; }

    /// <summary>
    /// When this CodeSystem metadata was imported to the terminology tables.
    /// </summary>
    public DateTimeOffset ImportedDate { get; set; } = DateTimeOffset.UtcNow;
}
