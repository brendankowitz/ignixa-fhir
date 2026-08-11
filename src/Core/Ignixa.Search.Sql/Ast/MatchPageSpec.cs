namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// Immutable configuration for the match page and its optional include-seed wrappers.
/// </summary>
public sealed record MatchPageSpec(
    CteRef Root,
    int? Top = null,
    Predicate? OuterPredicate = null,
    SortSpec? Sort = null,
    PageSpec? Page = null,
    ResultShape? Shape = null,
    SurrogateIdRange? SurrogateRange = null,
    SqlParameterRef? SearchParameterHash = null,
    OffsetSpec? OffsetPage = null)
{
    /// <summary>The result shape, defaulting to <see cref="ResultShape.Matches"/>.</summary>
    public ResultShape EffectiveShape => Shape ?? ResultShape.Default;

    /// <summary>True when the statement returns a count rather than rows.</summary>
    public bool CountOnly => EffectiveShape is ResultShape.Count;

    /// <summary>True when the statement omits the match page and returns include-stage rows only.</summary>
    public bool IncludesOnly => EffectiveShape is ResultShape.IncludesPage;

    /// <summary>
    /// The keyset boundary for later pages of an <see cref="ResultShape.IncludesPage"/> stream, or null for
    /// other result shapes.
    /// </summary>
    public IncludeBoundary? IncludeBoundary => (EffectiveShape as ResultShape.IncludesPage)?.Resume;
}
