namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// Which rows of dbo.Resource a plan may see. The default, <see cref="Current"/>, excludes superseded
/// versions and soft-deleted rows — the only shape an ordinary search wants. A caller reading history
/// (_history), exporting, or reindexing relaxes one or both, so the filter is a plan input rather than
/// something Emit assumes.
/// </summary>
/// <remarks>
/// This is the SQL-side counterpart of <c>Ignixa.Search.Models.ResourceVersionTypes</c>, which is what a
/// caller sets on its search options; that enum's remarks document the mapping. Two booleans rather than
/// the flags enum because only these two relaxations reach the emitter, and "latest" is not a filter the
/// emitter can express — it is the absence of the other two.
/// </remarks>
/// <param name="IncludeHistory">When true, no <c>IsHistory = 0</c> filter is emitted.</param>
/// <param name="IncludeDeleted">When true, no <c>IsDeleted = 0</c> filter is emitted.</param>
public sealed record ResourceVisibility(bool IncludeHistory, bool IncludeDeleted)
{
    /// <summary>Current, non-deleted rows only — what an ordinary search means by "a resource".</summary>
    public static ResourceVisibility Current { get; } = new(IncludeHistory: false, IncludeDeleted: false);
}
