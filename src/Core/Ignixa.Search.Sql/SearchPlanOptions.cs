using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql;

/// <summary>
/// Everything a caller controls about a compile that is not the query itself. Every property is optional; the
/// default instance is a plain, untraced, uncapped search returning the first page of matches.
/// </summary>
public sealed record SearchPlanOptions
{
    /// <summary>What the statement returns. Defaults to <see cref="ResultShape.Matches"/>.</summary>
    public ResultShape Shape { get; init; } = ResultShape.Default;

    /// <summary>
    /// How the statement is bounded and positioned. Null means no cap and the first page, which for a sorted
    /// search is the first page of the <see cref="SortPhase.Valued"/> segment.
    /// </summary>
    public SearchPaging? Paging { get; init; }

    /// <summary>The per-stage cap on included resources; null means no cap. Rejected when negative.</summary>
    public int? IncludeLimit { get; init; }

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
