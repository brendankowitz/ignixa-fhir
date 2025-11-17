namespace Ignixa.Domain.Terminology;

/// <summary>
/// Individual concept/code from a CodeSystem, extracted for fast queries.
/// Supports hierarchy, properties, and designations for terminology operations.
/// </summary>
public class TermConcept
{
    /// <summary>
    /// Surrogate primary key.
    /// </summary>
    public long TermConceptId { get; set; }

    /// <summary>
    /// Foreign key to TermCodeSystem table.
    /// </summary>
    public required long TermCodeSystemId { get; set; }

    /// <summary>
    /// The concept code (from CodeSystem.concept.code).
    /// Example: "male", "8310-5", "276885007"
    /// </summary>
    public required string Code { get; set; }

    /// <summary>
    /// Display name for the concept (from CodeSystem.concept.display).
    /// Example: "Male", "Body temperature", "Acute bronchitis"
    /// </summary>
    public string? Display { get; set; }

    /// <summary>
    /// Formal definition of the concept (from CodeSystem.concept.definition).
    /// </summary>
    public string? Definition { get; set; }

    /// <summary>
    /// Parent concept ID for hierarchical CodeSystems (enables $subsumes operation).
    /// Null for root concepts.
    /// </summary>
    public long? ParentConceptId { get; set; }

    /// <summary>
    /// Nesting level in hierarchy (0 = root, 1 = child of root, etc.).
    /// Enables efficient hierarchy traversal.
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// True if this concept is marked as active (not deprecated/retired).
    /// Extracted from property with code="inactive" (false means active).
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// JSON array of additional properties and designations.
    /// Structure: { "properties": [...], "designations": [...] }
    /// Phase 1: Store as JSON. Phase 2: Consider separate tables for better querying.
    /// </summary>
    public string? PropertiesJson { get; set; }
}
