namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// The rules a keyset page and a sort must jointly satisfy for the emitted seek to agree with the emitted
/// ORDER BY. Each rule is a predicate only: <see cref="Lowering.Lower"/> and
/// <see cref="Builders.SqlBuilder"/> both enforce them but phrase their failures for their own caller — Lower
/// names options-level types (SearchPaging, _sort), the emitter names plan-level ones (PageSpec, SortSpec).
/// Sharing the predicate keeps the two from drifting apart on what is legal while each keeps a diagnostic
/// that points at the surface its caller actually used.
/// </summary>
/// <remarks>
/// Both layers check independently because <see cref="QueryPlan"/> is a public construction surface: a plan
/// can be built, or rewritten via <c>plan with { … }</c>, without going through Lower, so the emitter cannot
/// assume Lower already ran.
/// </remarks>
internal static class KeysetPageInvariants
{
    /// <summary>
    /// A typeless boundary breaks its final tie on Sid1 alone and omits the type column, which agrees with the
    /// emitted ORDER BY only for a custom sort — every other sort keeps m.T1 as a tiebreak, so a type-free
    /// seek would disagree with the ordering and drop tied rows.
    /// </summary>
    public static bool TypelessPageNeedsCustomSort(PageSpec? page, SortSpec? sort)
        => page is { BoundaryResourceTypeId: null } && sort?.HasCustomKey is not true;

    /// <summary>
    /// A custom sort orders by (sort keys…, Sid1) with no type component, so a typed boundary seeks type-major.
    /// Within a run of tied sort values a row of a lower type id but higher surrogate id then sorts after the
    /// boundary yet is excluded by the seek, and is silently dropped at the page seam.
    /// </summary>
    public static bool TypedPageConflictsWithCustomSort(PageSpec? page, SortSpec? sort)
        => page is { BoundaryResourceTypeId: not null } && sort?.HasCustomKey is true;

    /// <summary>
    /// A boundary decoded under one phase carries values for that phase's active keys, so carrying it across a
    /// Valued/MissingPrimary transition seeks on the wrong key set.
    /// </summary>
    public static bool BoundaryCountDisagreesWithPhase(PageSpec? page, SortSpec? sort)
        => page is not null && page.Boundary.Count != ActiveKeyCount(sort);

    /// <summary>The active key count a boundary must supply, treating an absent sort as zero keys.</summary>
    public static int ActiveKeyCount(SortSpec? sort) => sort?.ActiveKeyCount ?? 0;

    /// <summary>
    /// A probe flag states that a row cap is the page size plus one has-more lookahead row, so it says
    /// nothing about an uncapped page. Shared for the same reason as the page rules above: Lower rejects it
    /// naming <c>SearchPaging.Keyset</c>, the plan layer naming <c>MatchPageSpec</c>, and only the predicate
    /// is common. <c>MatchPageSpec.TrimmedPageSize</c> reports null for this state, so both layers must agree
    /// on it or a read of that member changes meaning between them.
    /// </summary>
    public static bool ProbeRowNeedsCap(int? top, bool topIncludesProbeRow)
        => topIncludesProbeRow && top is null;

    /// <summary>
    /// The cap covers the page and its probe row, so a cap below 1 leaves no page once the probe row is
    /// subtracted — <c>TrimmedPageSize</c> would otherwise compute a negative row count.
    /// </summary>
    public static bool ProbeRowCapTooSmall(int? top, bool topIncludesProbeRow)
        => topIncludesProbeRow && top is { } cap && cap < 1;
}
