namespace Ignixa.Domain.Terminology;

/// <summary>
/// Individual mapping element from a ConceptMap (source code → target code).
/// Enables $translate operations.
/// </summary>
public class TermConceptMapElement
{
    /// <summary>
    /// Surrogate primary key.
    /// </summary>
    public long TermConceptMapElementId { get; set; }

    /// <summary>
    /// Foreign key to TermConceptMap table.
    /// </summary>
    public required long TermConceptMapId { get; set; }

    /// <summary>
    /// Foreign key to System table (source code system).
    /// </summary>
    public required int SourceSystemId { get; set; }

    /// <summary>
    /// Source code value (from ConceptMap.group.element.code).
    /// Example: "male" in FHIR administrative-gender
    /// </summary>
    public required string SourceCode { get; set; }

    /// <summary>
    /// Display name for source code (from ConceptMap.group.element.display).
    /// </summary>
    public string? SourceDisplay { get; set; }

    /// <summary>
    /// Foreign key to System table (target code system).
    /// Null for "unmatched" equivalence.
    /// </summary>
    public int? TargetSystemId { get; set; }

    /// <summary>
    /// Target code value (from ConceptMap.group.element.target.code).
    /// Example: "M" in v3 AdministrativeGender
    /// Null for "unmatched" equivalence.
    /// </summary>
    public string? TargetCode { get; set; }

    /// <summary>
    /// Display name for target code (from ConceptMap.group.element.target.display).
    /// </summary>
    public string? TargetDisplay { get; set; }

    /// <summary>
    /// Equivalence relationship: equivalent, wider, narrower, inexact, unmatched, disjoint.
    /// From ConceptMap.group.element.target.equivalence.
    /// </summary>
    public required string Equivalence { get; set; }

    /// <summary>
    /// Optional comment about the mapping (from ConceptMap.group.element.target.comment).
    /// </summary>
    public string? Comment { get; set; }

    /// <summary>
    /// Index of the group[] this element belongs to (for reconstructing ConceptMap structure).
    /// </summary>
    public int GroupIndex { get; set; }
}
