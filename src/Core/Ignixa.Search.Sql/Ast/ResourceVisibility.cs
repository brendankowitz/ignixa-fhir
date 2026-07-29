namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// Which rows of dbo.Resource a plan may see, expressed as an independent tri-state on each of the two
/// version columns. The default, <see cref="Current"/>, pins both to their current-row value
/// (<c>IsHistory = 0</c>, <c>IsDeleted = 0</c>) — the only shape an ordinary search wants. A caller reading
/// history (_history), exporting, or reindexing changes one or both axes, so the filter is a plan input
/// rather than something Emit assumes.
/// </summary>
/// <remarks>
/// This is the SQL-side counterpart of <c>Ignixa.Search.Models.ResourceVersionTypes</c>, which is what a
/// caller sets on its search options; that enum's remarks document the mapping. Each axis is a nullable
/// <see cref="bool"/> rather than a plain flag because the legacy generator this must match is genuinely
/// tri-state on each column independently — it can demand the current-row value (<c>= 0</c>), demand the
/// non-current value (<c>= 1</c>), or leave the column unconstrained so a union of both is returned. An
/// earlier two-<see cref="bool"/> "relaxation only" model (each flag either added a <c>= 0</c> filter or
/// omitted it) could express the first and third states but not the second, so a "history rows only" or
/// "soft-deleted rows only" search was inexpressible and its callers had to be turned away upstream. Making
/// each axis <c>null</c> / <c>false</c> / <c>true</c> closes that gap without collapsing the two columns
/// into one enum, because the two columns are filtered independently and at different emitter sites (a
/// search-param index table has an <c>IsHistory</c> column but no <c>IsDeleted</c> column, so a single
/// combined value could not be honored uniformly).
/// </remarks>
/// <param name="IsHistory">
/// The constraint on the <c>IsHistory</c> column: <c>null</c> emits no filter (superseded and current rows
/// both qualify), <c>false</c> emits <c>IsHistory = 0</c> (current rows only), <c>true</c> emits
/// <c>IsHistory = 1</c> (superseded rows only).
/// </param>
/// <param name="IsDeleted">
/// The constraint on the <c>IsDeleted</c> column: <c>null</c> emits no filter (deleted and live rows both
/// qualify), <c>false</c> emits <c>IsDeleted = 0</c> (live rows only), <c>true</c> emits <c>IsDeleted = 1</c>
/// (soft-deleted rows only).
/// </param>
public sealed record ResourceVisibility(bool? IsHistory, bool? IsDeleted)
{
    /// <summary>Current, non-deleted rows only — what an ordinary search means by "a resource".</summary>
    public static ResourceVisibility Current { get; } = new(IsHistory: false, IsDeleted: false);
}
