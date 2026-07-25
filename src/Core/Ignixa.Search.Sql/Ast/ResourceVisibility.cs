namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// Which rows of dbo.Resource a plan may see. The default, <see cref="Current"/>, excludes superseded
/// versions and soft-deleted rows — the only shape an ordinary search wants. A caller reading history
/// (_history), exporting, or reindexing relaxes one or both, so the filter is a plan input rather than
/// something Emit assumes.
/// </summary>
/// <param name="IncludeHistory">When true, no <c>IsHistory = 0</c> filter is emitted.</param>
/// <param name="IncludeDeleted">When true, no <c>IsDeleted = 0</c> filter is emitted.</param>
public sealed record ResourceVisibility(bool IncludeHistory, bool IncludeDeleted)
{
    /// <summary>Current, non-deleted rows only — what an ordinary search means by "a resource".</summary>
    public static ResourceVisibility Current { get; } = new(IncludeHistory: false, IncludeDeleted: false);
}
