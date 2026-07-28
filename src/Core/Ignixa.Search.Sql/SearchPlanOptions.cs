using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql;

/// <summary>
/// Everything a caller controls about a compile that is not the query itself. Every property is optional;
/// the default instance is a plain, untraced, uncapped search.
/// </summary>
public sealed record SearchPlanOptions
{
    /// <summary>Emit a row count instead of the rows themselves.</summary>
    public bool CountOnly { get; init; }

    /// <summary>The per-stage cap on included resources; 0 means no cap.</summary>
    public int IncludeLimit { get; init; }

    /// <summary>Which phase of a two-phase sort this compile emits.</summary>
    public SortPhase SortPhase { get; init; } = SortPhase.Valued;

    /// <summary>
    /// Scopes a <see cref="CountOnly"/> count to the current sort phase's own join output. Requires
    /// <see cref="CountOnly"/> and at least one sort key.
    /// </summary>
    public bool CountPhaseScoped { get; init; }

    /// <summary>Return include-stage rows only, omitting the match page. The <c>$includes</c> second page.</summary>
    public bool IncludesOnly { get; init; }

    /// <summary>A SQL <c>TOP</c> cap; null means no cap. Mutually exclusive with <see cref="OffsetPage"/>.</summary>
    public int? Top { get; init; }

    /// <summary>
    /// A keyset continuation boundary. Mutually exclusive with <see cref="OffsetPage"/>. The compiler has
    /// always supported this; before this API existed no orchestrated entry point could ask for it.
    /// </summary>
    public PageSpec? Page { get; init; }

    /// <summary>An OFFSET/FETCH page. Mutually exclusive with <see cref="Page"/> and <see cref="Top"/>.</summary>
    public OffsetSpec? OffsetPage { get; init; }

    /// <summary>
    /// An inclusive surrogate-id bound. When set it wins over <c>SearchOptions.StartSurrogateId</c>/
    /// <c>EndSurrogateId</c>.
    /// </summary>
    public (long Start, long End)? SurrogateIdRange { get; init; }

    /// <summary>
    /// The expected search-parameter hash for reindex gating; null when unused. Typed as a string so a
    /// caller never has to construct an AST node to express it.
    /// </summary>
    public string? SearchParameterHash { get; init; }

    /// <summary>
    /// A FHIR operation root such as <c>PatientEverythingExpression</c>, which no query string can produce.
    /// When set it replaces the expression the options builder derived.
    /// </summary>
    public Expression? OperationExpression { get; init; }

    /// <summary>How much this compile records about its own work.</summary>
    public SearchDiagnosticsLevel DiagnosticsLevel { get; init; } = SearchDiagnosticsLevel.None;
}
