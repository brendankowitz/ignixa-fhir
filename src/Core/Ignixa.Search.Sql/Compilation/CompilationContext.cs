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

    /// <summary>
    /// The global allow-list of resource types the caller is permitted to see; empty means unrestricted.
    /// Read by both stages: Resolve needs each name's id, and Lower enforces the list structurally.
    /// </summary>
    public required IReadOnlyList<string> AllowedResourceTypes { get; init; }

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
            AllowedResourceTypes = searchOptions.AllowedResourceTypes ?? [],
            ApproximationReferenceTime = approximationReferenceTime,
            Visibility = ToVisibility(searchOptions.ResourceVersionTypes),
            SurrogateRange = ToSurrogateRange(options.SurrogateRange, searchOptions),
            Options = options,
        };
    }

    /// <summary>
    /// Maps <see cref="SearchOptions.ResourceVersionTypes"/> onto <see cref="ResourceVisibility"/>. Each of
    /// the two columns is resolved independently and tri-state, reproducing the legacy generator's
    /// per-column truth table — which is what lets a history-only or soft-deleted-only search, the shapes an
    /// earlier relaxation-only visibility could not express, reach the emitter at all.
    /// <para>
    /// <see cref="ResourceVersionTypes.Latest"/> alone returns null rather than an explicit
    /// <see cref="ResourceVisibility.Current"/>. Both leave <see cref="QueryPlan.EffectiveVisibility"/>
    /// (which falls back to <see cref="ResourceVisibility.Current"/> on null) at the same value, and
    /// <see cref="ResourceVisibility.Current"/> is itself <c>new(IsHistory: false, IsDeleted: false)</c> —
    /// exactly what the general arm would compute for Latest alone — so the shortcut is a byte-for-byte
    /// no-op.
    /// </para>
    /// </summary>
    private static ResourceVisibility? ToVisibility(ResourceVersionTypes types) => types switch
    {
        ResourceVersionTypes.None => throw new NotSupportedException(
            "SearchOptions.ResourceVersionTypes.None is not a valid search input; a search must select at least Latest."),
        ResourceVersionTypes.Latest => null,
        _ => new ResourceVisibility(
            IsHistory: ColumnFilter(types.HasFlag(ResourceVersionTypes.Latest), types.HasFlag(ResourceVersionTypes.History)),
            IsDeleted: ColumnFilter(types.HasFlag(ResourceVersionTypes.Latest), types.HasFlag(ResourceVersionTypes.SoftDeleted))),
    };

    /// <summary>
    /// Resolves one version column's tri-state from whether the caller asked for its current partition
    /// (<paramref name="wantsCurrent"/>, i.e. Latest) and its non-current partition
    /// (<paramref name="wantsNonCurrent"/>, i.e. History for IsHistory or SoftDeleted for IsDeleted).
    /// Current only → pin to <c>0</c> (<c>false</c>); non-current only → pin to <c>1</c> (<c>true</c>);
    /// both or neither → no filter (<c>null</c>), the union. Expressed once so the IsHistory and IsDeleted
    /// axes cannot drift apart.
    /// </summary>
    private static bool? ColumnFilter(bool wantsCurrent, bool wantsNonCurrent)
    {
        if (wantsCurrent && !wantsNonCurrent)
        {
            return false;
        }

        if (wantsNonCurrent && !wantsCurrent)
        {
            return true;
        }

        return null;
    }

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
