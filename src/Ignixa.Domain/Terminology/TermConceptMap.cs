namespace Ignixa.Domain.Terminology;

/// <summary>
/// Metadata for a FHIR ConceptMap extracted from PackageResource.
/// Supports $translate operations for code system mapping.
/// </summary>
public class TermConceptMap
{
    /// <summary>
    /// Surrogate primary key.
    /// </summary>
    public long TermConceptMapId { get; set; }

    /// <summary>
    /// Foreign key to PackageResource table (where full ConceptMap JSON is stored).
    /// </summary>
    public required long PackageResourceId { get; set; }

    /// <summary>
    /// Canonical URL of the ConceptMap (from ConceptMap.url).
    /// Example: "http://hl7.org/fhir/ConceptMap/cm-administrative-gender-v3"
    /// </summary>
    public required string Canonical { get; set; }

    /// <summary>
    /// Business version of the ConceptMap (from ConceptMap.version).
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Name of the ConceptMap (from ConceptMap.name).
    /// Example: "v3.AdministrativeGender"
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Default source ValueSet/CodeSystem canonical URL (from ConceptMap.sourceCanonical).
    /// May be null if varies by group.
    /// </summary>
    public string? SourceCanonical { get; set; }

    /// <summary>
    /// Default target ValueSet/CodeSystem canonical URL (from ConceptMap.targetCanonical).
    /// May be null if varies by group.
    /// </summary>
    public string? TargetCanonical { get; set; }

    /// <summary>
    /// When this ConceptMap metadata was imported to the terminology tables.
    /// </summary>
    public DateTimeOffset ImportedDate { get; set; } = DateTimeOffset.UtcNow;
}
