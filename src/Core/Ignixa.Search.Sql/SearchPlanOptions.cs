using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql;

/// <summary>
/// Everything a caller controls about a compile that is not the query itself. Every property is optional; the
/// default instance is a plain, untraced search with no row cap and no continuation.
/// </summary>
public sealed record SearchPlanOptions
{
    /// <summary>What the statement returns. Defaults to <see cref="ResultShape.Matches"/>.</summary>
    public ResultShape Shape { get; init; } = ResultShape.Default;

    /// <summary>
    /// How the statement is bounded and positioned. Null means no row cap, no offset and no keyset boundary.
    /// </summary>
    public SearchPaging? Paging { get; init; }

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
    /// The budget of included resources, applied as <c>TOP (IncludeLimit + 1)</c> — once per include stage
    /// under <see cref="ResultShape.Matches"/>, once over the union of every stage under
    /// <see cref="ResultShape.IncludesPage"/>. The extra row is a truncation probe, reported back as
    /// <c>IsPartial</c> rather than returned. Zero — the default — therefore returns one sentinel row the
    /// caller trims, which is how it detects that included resources exist without fetching them. There is no
    /// uncapped setting. Rejected when negative, and at <see cref="int.MaxValue"/> because the probe overflows.
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
