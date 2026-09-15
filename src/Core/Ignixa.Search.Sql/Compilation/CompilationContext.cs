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

    public LastNSearchOptions? LastNOptions { get; init; }

    private readonly bool? _systemLevelSearch;

    /// <summary>
    /// True for a system-level (cross-type) search. Defaults to <c>TargetResourceType is null</c> but is
    /// settable to tell the two null-target cases apart: system-level (<c>true</c>, leaves lower cross-type)
    /// versus wildcard compartment (<c>false</c>, whose stray typed predicates the guards in
    /// <see cref="Lowering.Lower.Run"/> refuse). No production caller sets it yet.
    /// </summary>
    public bool SystemLevelSearch
    {
        get => _systemLevelSearch ?? TargetResourceType is null;
        init => _systemLevelSearch = value;
    }

    /// <summary>
    /// Maps a built <see cref="SearchOptions"/> and the caller's <see cref="SearchPlanOptions"/> onto the one
    /// context both stages read; <see cref="CompilationContextMapping"/> is its enforced contract. Collections
    /// are coalesced to empty because <c>Ignixa.Search</c> compiles nullable-disabled and can hand us null.
    /// </summary>
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

    public static CompilationContext CreateLastN(
        LastNSearchOptions lastNOptions,
        SearchPlanOptions options,
        DateTimeOffset approximationReferenceTime)
    {
        ArgumentNullException.ThrowIfNull(lastNOptions);

        return Create(lastNOptions.Filters, "Observation", options, approximationReferenceTime) with
        {
            LastNOptions = lastNOptions,
        };
    }

    /// <summary>
    /// Maps <see cref="SearchOptions.ResourceVersionTypes"/> onto <see cref="ResourceVisibility"/>, resolving
    /// each column independently and tri-state so history-only or soft-deleted-only searches reach the emitter.
    /// <see cref="ResourceVersionTypes.Latest"/> alone returns null, a byte-for-byte no-op for
    /// <see cref="ResourceVisibility.Current"/> (the general arm would compute the same).
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
    /// Resolves one version column's tri-state: current only → <c>false</c>; non-current only → <c>true</c>;
    /// both or neither → <c>null</c> (no filter, the union). Expressed once so the IsHistory and IsDeleted
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
    /// The surrogate-id bound this compile applies: the explicit
    /// <see cref="SearchPlanOptions.SurrogateRange"/> when supplied, otherwise the <see cref="SearchOptions"/>
    /// pair. A half-open pair is a caller error, not a partial intent to honour.
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
