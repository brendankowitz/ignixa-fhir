using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql.Compilation;

/// <summary>
/// The single set of inputs Resolve and Lower both read. Built once per compile so the two stages cannot
/// observe different values — the shape of every forwarding defect this compiler has shipped.
/// </summary>
internal sealed record CompilationContext
{
    public required Expression? Expression { get; init; }

    /// <summary>Null means a system-level or wildcard-compartment search. Normalized exactly once, in <see cref="Create"/>.</summary>
    public required string? TargetResourceType { get; init; }

    public required IReadOnlyList<IncludeExpression> Includes { get; init; }

    public required IReadOnlyList<IncludeExpression> RevIncludes { get; init; }

    public required IReadOnlyList<SortExpression> Sort { get; init; }

    public required IReadOnlyList<AccessConstraint> AccessConstraints { get; init; }

    public required IReadOnlyList<string> ResourceTypes { get; init; }

    public required DateTimeOffset ApproximationReferenceTime { get; init; }

    public required ResourceVisibility? Visibility { get; init; }

    public required SurrogateIdRange? SurrogateRange { get; init; }

    public required SearchPlanOptions Options { get; init; }

    private readonly bool? _systemLevelSearch;

    /// <summary>
    /// True for a system-level (cross-type) search. Defaults to <c>TargetResourceType is null</c> — the value
    /// every production caller derives — but is settable so the two distinct null-target cases can be told
    /// apart: a system-level search (<c>true</c>, leaves lower cross-type) versus a wildcard compartment
    /// search (<c>false</c>, an ordinary typed predicate or _sort alongside it has no single type to scope
    /// against and is refused by the guards in <see cref="Lowering.Lower.Run"/>). No production caller sets it
    /// today, so the derivation stands for every compile the facade drives; it is settable because wildcard
    /// compartment search is a real mode this compiler lowers, and once the public API can express it the
    /// derivation alone would silently admit the predicates those guards exist to refuse.
    /// </summary>
    public bool SystemLevelSearch
    {
        get => _systemLevelSearch ?? TargetResourceType is null;
        init => _systemLevelSearch = value;
    }

    /// <summary>
    /// Maps a built <see cref="SearchOptions"/> and the caller's <see cref="SearchPlanOptions"/> onto the
    /// one context both stages read. This is the only place that mapping happens;
    /// <see cref="CompilationContextMapping"/> is its enforced contract.
    /// </summary>
    /// <remarks>
    /// The collection properties are coalesced because <c>Ignixa.Search</c> compiles with nullable disabled,
    /// so a caller can assign null to them without warning. Coalescing here turns that into an empty list at
    /// the boundary rather than a null-reference deep inside lowering.
    /// </remarks>
    public static CompilationContext Create(
        SearchOptions searchOptions,
        string? targetResourceType,
        SearchPlanOptions options,
        DateTimeOffset approximationReferenceTime)
    {
        ArgumentNullException.ThrowIfNull(searchOptions);
        ArgumentNullException.ThrowIfNull(options);

        return new CompilationContext
        {
            Expression = options.OperationExpression ?? searchOptions.Expression,
            TargetResourceType = string.IsNullOrEmpty(targetResourceType) ? null : targetResourceType,
            Includes = searchOptions.Include ?? [],
            RevIncludes = searchOptions.RevInclude ?? [],
            Sort = searchOptions.Sort ?? [],
            AccessConstraints = searchOptions.AccessConstraints ?? [],
            ResourceTypes = searchOptions.ResourceTypes ?? [],
            ApproximationReferenceTime = approximationReferenceTime,
            Visibility = ToVisibility(searchOptions.ResourceVersionTypes),
            SurrogateRange = ToSurrogateRange(options.SurrogateRange, searchOptions),
            Options = options,
        };
    }

    /// <summary>
    /// Maps <see cref="SearchOptions.ResourceVersionTypes"/> onto <see cref="ResourceVisibility"/>.
    /// <see cref="ResourceVersionTypes.Latest"/> alone returns null, which
    /// <see cref="QueryPlan.EffectiveVisibility"/> already treats as <see cref="ResourceVisibility.Current"/>.
    /// </summary>
    private static ResourceVisibility? ToVisibility(ResourceVersionTypes types) => types switch
    {
        ResourceVersionTypes.None => throw new NotSupportedException(
            "SearchOptions.ResourceVersionTypes.None is not a valid search input; a search must select at least Latest."),
        ResourceVersionTypes.Latest => null,
        _ => new ResourceVisibility(
            IncludeHistory: types.HasFlag(ResourceVersionTypes.History),
            IncludeDeleted: types.HasFlag(ResourceVersionTypes.SoftDeleted)),
    };

    /// <summary>
    /// The surrogate-id bound this compile applies: the explicit <see cref="SearchPlanOptions.SurrogateRange"/>
    /// when supplied, otherwise the <see cref="SearchOptions"/> pair. A half-open pair is a caller error,
    /// not a partial intent to honour.
    /// </summary>
    private static SurrogateIdRange? ToSurrogateRange((long Start, long End)? explicitRange, SearchOptions searchOptions)
    {
        if (explicitRange is { } range)
        {
            return new SurrogateIdRange(new SqlParameterRef(range.Start), new SqlParameterRef(range.End));
        }

        return (searchOptions.StartSurrogateId, searchOptions.EndSurrogateId) switch
        {
            (null, null) => null,
            ({ } start, { } end) => new SurrogateIdRange(new SqlParameterRef(start), new SqlParameterRef(end)),
            _ => throw new NotSupportedException(
                "SearchOptions.StartSurrogateId and EndSurrogateId must both be set or both be null."),
        };
    }
}
