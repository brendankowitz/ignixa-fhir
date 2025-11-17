namespace Ignixa.Domain.Terminology;

/// <summary>
/// Pre-computed expansion of a ValueSet (all codes that are members).
/// Enables fast $expand and $validate-code operations without re-computing compose rules.
/// </summary>
public class TermValueSetExpansion
{
    /// <summary>
    /// Surrogate primary key.
    /// </summary>
    public long TermValueSetExpansionId { get; set; }

    /// <summary>
    /// Foreign key to TermValueSet table.
    /// </summary>
    public required long TermValueSetId { get; set; }

    /// <summary>
    /// Foreign key to System table (code system of this code).
    /// Reuses existing System table for canonical URLs.
    /// </summary>
    public required int SystemId { get; set; }

    /// <summary>
    /// The code value (from ValueSet.expansion.contains.code).
    /// Example: "male", "8310-5"
    /// </summary>
    public required string Code { get; set; }

    /// <summary>
    /// Display name for the code (from ValueSet.expansion.contains.display).
    /// Example: "Male", "Body temperature"
    /// </summary>
    public string? Display { get; set; }

    /// <summary>
    /// Version of the code system this code came from.
    /// From ValueSet.expansion.contains.version.
    /// </summary>
    public string? SystemVersion { get; set; }

    /// <summary>
    /// True if this code is currently active in the expansion.
    /// Set to false when expansion is invalidated/re-computed.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Ordinal position in expansion (for stable ordering).
    /// Preserves order from ValueSet.expansion.contains array.
    /// </summary>
    public int Ordinal { get; set; }
}
