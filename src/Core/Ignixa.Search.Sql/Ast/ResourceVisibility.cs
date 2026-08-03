namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// Which rows of dbo.Resource a plan may see — an independent tri-state on each version column. The default
/// <see cref="Current"/> pins both to the current-row value (<c>IsHistory = 0</c>, <c>IsDeleted = 0</c>);
/// _history, export or reindex change one or both axes, so the filter is a plan input, not an Emit assumption.
/// </summary>
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
