using Ignixa.Search.Sql.Ast;
using SearchSortOrder = Ignixa.Search.Expressions.SortOrder;

namespace Ignixa.Search.Sql.Tests.TestSupport;

/// <summary>
/// Hand-built plans for the two <c>$includes</c> shapes that seed from a match set they do not return:
/// <see cref="ResultShape.IncludesCount"/> and an <see cref="ResultShape.IncludesPage"/> carrying a
/// <see cref="MatchPageReplay"/>. Shared by the emission, explain and grammar tests so they pin one plan each.
/// </summary>
internal static class IncludesShapePlans
{
    public const short PatientTypeId = 103;
    public const short OrganizationTypeId = 105;
    public const short ObservationTypeId = 107;

    /// <summary><c>_sort=-date</c>, Valued phase.</summary>
    public static readonly SortSpec DateDescending = new([new SortKey(203, SortKeyKind.Date, SearchSortOrder.Descending)], SortPhase.Valued);

    /// <summary>A later-page boundary for <see cref="DateDescending"/>: typeless, as a custom sort requires.</summary>
    public static readonly PageSpec TypelessDateBoundary = new([new SqlParameterRef("2024-03-01")], BoundaryResourceTypeId: null, new SqlParameterRef(4200L));

    /// <summary>Patient:organization — the ordinary forward include.</summary>
    public static IncludeStage ForwardStage(int? limit = 10)
        => new(IncludeDirection.Forward, ReferenceSearchParamId: 55, SeedTypeIds: [PatientTypeId], OutputTypeIds: [OrganizationTypeId],
               SeedStages: [], SeedFromMatch: true, Iterate: false, Limit: limit);

    /// <summary>Observation:subject — a reverse include seeded from the matches.</summary>
    public static IncludeStage ReverseStage(int? limit = 10)
        => new(IncludeDirection.Reverse, ReferenceSearchParamId: 88, SeedTypeIds: [PatientTypeId], OutputTypeIds: [ObservationTypeId],
               SeedStages: [], SeedFromMatch: true, Iterate: false, Limit: limit);

    /// <summary>Organization:partof:iterate — seeded from stage 0's body, not from the matches.</summary>
    public static IncludeStage IterateStage(int seedStage, int? limit = 10)
        => new(IncludeDirection.Forward, ReferenceSearchParamId: 77, SeedTypeIds: [OrganizationTypeId], OutputTypeIds: [OrganizationTypeId],
               SeedStages: [seedStage], SeedFromMatch: false, Iterate: true, Limit: limit);

    /// <summary><c>_revinclude=*</c>: any reference parameter, any source type.</summary>
    public static IncludeStage WildcardReverseStage(int? limit = 10)
        => new(IncludeDirection.Reverse, ReferenceSearchParamId: null, SeedTypeIds: [PatientTypeId], OutputTypeIds: null,
               SeedStages: [], SeedFromMatch: true, Iterate: false, Limit: limit);

    /// <summary>An includes count over every resource of the match type.</summary>
    public static QueryPlan IncludesCount(params IncludeStage[] stages)
        => PatientPlan(new MatchPageSpec(new CteRef(0), Shape: new ResultShape.IncludesCount()), stages);

    /// <summary>An include plan whose match set is every Patient, shaped and bounded by <paramref name="spec"/>.</summary>
    public static QueryPlan PatientPlan(MatchPageSpec spec, params IncludeStage[] stages)
        => IncludePlanFactory.Create([new CteDefinition.ResourceSource(PatientTypeId)], spec, stages);

    /// <summary>
    /// The match-page spec a <see cref="ResultShape.IncludesPage"/> must carry to replay
    /// <paramref name="replay"/>: Top and Page copied from it, no probe row.
    /// </summary>
    public static MatchPageSpec ReplaySpec(MatchPageReplay replay, SortSpec? sort, IncludeBoundary? resume = null)
        => new(new CteRef(0), Top: replay.PageSize, Sort: sort, Page: replay.Boundary, Shape: new ResultShape.IncludesPage(resume, replay));

    /// <summary>An includes page replaying <paramref name="replay"/> under <paramref name="sort"/>.</summary>
    public static QueryPlan Replay(MatchPageReplay replay, SortSpec? sort, IncludeBoundary? resume = null, params IncludeStage[] stages)
        => PatientPlan(ReplaySpec(replay, sort, resume), stages.Length == 0 ? [ForwardStage()] : stages);

    /// <summary>
    /// The ordinary search page the replay re-reads: the same sort and boundary, capped at the page size plus a
    /// has-more probe row, with the same include stage. Its match page is what the replay must reproduce.
    /// </summary>
    public static QueryPlan OriginalMatchesPage(MatchPageReplay replay, SortSpec? sort)
        => PatientPlan(
            new MatchPageSpec(new CteRef(0), Top: replay.PageSize + 1, Sort: sort, Page: replay.Boundary, TopIncludesProbeRow: true),
            ForwardStage());
}
