using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql;

/// <summary>
/// Everything a caller controls about a compile that is not the query itself. Every property is optional; the
/// default instance is a plain, untraced search with no row cap and no continuation.
/// </summary>
public sealed record SearchPlanOptions
{
    /// <summary>
    /// What the statement returns, and — under <see cref="ResultShape.Matches"/> — how it is paged. Defaults
    /// to an unpaged <see cref="ResultShape.Matches"/>.
    /// </summary>
    public ResultShape Shape { get; init; } = ResultShape.Default;

    /// <summary>
    /// Which segment of a sorted result this compile reads. A sort on a nullable search parameter splits the
    /// result into the resources carrying the primary key and the resources missing it, because a keyset seek
    /// has to be sargable against the search-parameter index, which rules out ordering one statement by a
    /// nullable key. The segment filters the match set, so it applies under either paging mechanism and with
    /// no paging at all — which is why it sits here rather than on <see cref="SearchPaging"/>. It cannot be
    /// inferred: the first page of either segment has no boundary to infer it from. Requesting
    /// <see cref="Ast.SortPhase.MissingPrimary"/> without a <c>_sort</c> is rejected.
    /// </summary>
    public SortPhase SortPhase { get; init; } = SortPhase.Valued;

    /// <summary>
    /// The budget of included resources, applied as <c>TOP (IncludeLimit + 1)</c> — per include stage under
    /// <see cref="ResultShape.Matches"/>, once over the union of every stage under
    /// <see cref="ResultShape.IncludesPage"/>. The extra row is over-fetched deliberately: it is returned,
    /// flagged <c>IsPartial</c>, and the caller trims it. That is how truncation stays detectable, and why
    /// zero — the default — reports whether included resources exist without fetching any of them. There is
    /// no uncapped setting. Rejected when negative, and at <see cref="int.MaxValue"/> because the extra row
    /// overflows the cap.
    /// </summary>
    public int IncludeLimit { get; init; }

    /// <summary>
    /// An inclusive surrogate-id bound. When set it wins over <c>SearchOptions.StartSurrogateId</c>/
    /// <c>EndSurrogateId</c>. Named to match <c>QueryPlan.SurrogateRange</c>, but typed as raw longs so a
    /// caller never has to construct the AST's <c>SurrogateIdRange</c> node.
    /// </summary>
    public (long Start, long End)? SurrogateRange { get; init; }

    /// <summary>
    /// The expected search-parameter hash for reindex gating; null when unused. Typed as a string so a
    /// caller never has to construct an AST node to express it.
    /// </summary>
    public string? SearchParameterHash { get; init; }

    /// <summary>
    /// A FHIR operation root such as <c>PatientEverythingExpression</c>, which no query string can produce.
    /// When set it replaces the expression the options builder derived — the builder still runs, so
    /// <c>_sort</c>, <c>_include</c> and the parameter traces survive, but any filter the query string
    /// expressed is discarded. Pair it with <c>CreatePlanFromOptionsAsync</c> to avoid that.
    /// </summary>
    public Expression? OperationExpression { get; init; }

    /// <summary>How much this compile records about its own work.</summary>
    public SearchDiagnosticsLevel DiagnosticsLevel { get; init; } = SearchDiagnosticsLevel.None;
}
