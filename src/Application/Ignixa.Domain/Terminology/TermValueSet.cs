namespace Ignixa.Domain.Terminology;

/// <summary>
/// Metadata for a FHIR ValueSet extracted from PackageResource.
/// Tracks expansion state for $expand operations.
/// </summary>
public class TermValueSet
{
    /// <summary>
    /// Surrogate primary key.
    /// </summary>
    public long TermValueSetId { get; set; }

    /// <summary>
    /// Foreign key to PackageResource table (where full ValueSet JSON is stored).
    /// </summary>
    public required long PackageResourceId { get; set; }

    /// <summary>
    /// Canonical URL of the ValueSet (from ValueSet.url).
    /// Example: "http://hl7.org/fhir/ValueSet/administrative-gender"
    /// </summary>
    public required string Canonical { get; set; }

    /// <summary>
    /// Business version of the ValueSet (from ValueSet.version).
    /// Example: "4.0.1"
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Name of the ValueSet (from ValueSet.name).
    /// Example: "AdministrativeGender"
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// True if this ValueSet is immutable (cannot be expanded dynamically).
    /// From ValueSet.immutable.
    /// </summary>
    public bool Immutable { get; set; }

    /// <summary>
    /// True if expansion has been pre-computed and stored in TermValueSetExpansion table.
    /// Enables fast $expand queries.
    /// </summary>
    public bool IsExpanded { get; set; }

    /// <summary>
    /// When the expansion was last computed (null if not expanded).
    /// Used to determine if re-expansion is needed.
    /// </summary>
    public DateTimeOffset? LastExpansionDate { get; set; }

    /// <summary>
    /// Number of codes in expansion (null if not expanded).
    /// From ValueSet.expansion.total.
    /// </summary>
    public int? ExpansionCodeCount { get; set; }

    /// <summary>
    /// When this ValueSet metadata was imported to the terminology tables.
    /// </summary>
    public DateTimeOffset ImportedDate { get; set; } = DateTimeOffset.UtcNow;
}
