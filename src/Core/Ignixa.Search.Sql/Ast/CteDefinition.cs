using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// One node in the compiler's CTE graph:
/// <list type="bullet">
/// <item><b>ParamSource</b> — one search-param table filtered by SearchParamId and an optional Predicate.</item>
/// <item><b>Intersect</b> — set intersection (AND).</item>
/// <item><b>Union</b> — set union (OR).</item>
/// <item><b>ResourceSource</b> — all current, non-deleted resources of a type (the base set for :not).</item>
/// <item><b>Except</b> — set subtraction (the :not operation itself).</item>
/// <item><b>ChainJoin</b> — a forward or reverse chain, joined through dbo.ReferenceSearchParam and dbo.Resource.</item>
/// <item><b>CompartmentSource</b> — compartment membership: rows of dbo.ReferenceSearchParam for one
/// membership SearchParamId, any of a set of ResourceTypeIds, and a fixed compartment reference.</item>
/// <item><b>NotReferencedSource</b> — resources of a type that no reference row points at (the
/// <c>_not-referenced</c> search): a dbo.Resource scan anti-joined to dbo.ReferenceSearchParam by
/// reference-target identity.</item>
/// </list>
/// ParamSource carries ResourceTypeId because a SearchParamId is assigned per parameter-definition URL,
/// not per resource type, so a shared definition (e.g. one spanning Patient and Practitioner) would
/// otherwise return rows of the wrong type. ResourceSource's Predicate is used only in a nested scope
/// (e.g. a chain's target), which has no outer WHERE to attach to; at the top level QueryPlan.OuterPredicate
/// carries resource-column filters instead.
/// </summary>
public abstract record CteDefinition
{
    public sealed record ParamSource(TableDescriptor Table, short ResourceTypeId, short SearchParamId, Predicate? Predicate = null) : CteDefinition;

    public sealed record Intersect(CteRef Left, CteRef Right) : CteDefinition;

    public sealed record Union(IReadOnlyList<CteRef> Parts) : CteDefinition;

    public sealed record ResourceSource(short ResourceTypeId, Predicate? Predicate = null) : CteDefinition;

    public sealed record Except(CteRef Left, CteRef Right) : CteDefinition;

    public sealed record ChainJoin(
        CteRef InnerMatch,
        short ReferenceSearchParamId,
        short InnerResourceTypeId,
        IReadOnlyList<short> OutputResourceTypeIds,
        ChainDirection Direction) : CteDefinition;

    public sealed record CompartmentSource(IReadOnlyList<short> ResourceTypeIds, short SearchParamId, Predicate Predicate) : CteDefinition;

    /// <summary>
    /// Resources of <paramref name="TargetResourceTypeId"/> that no dbo.ReferenceSearchParam row points at
    /// — the <c>_not-referenced</c> search for orphans. <paramref name="SourceResourceTypeId"/> narrows the
    /// anti-join to references originating from one resource type (<c>_not-referenced=Type:*</c>), and
    /// <paramref name="ReferenceSearchParamId"/> further to one reference path (<c>Type:path</c>); both null
    /// is the full wildcard (<c>*:*</c>), matching a resource referenced by nothing at all.
    /// <para>
    /// Invariant: <paramref name="ReferenceSearchParamId"/> implies <paramref name="SourceResourceTypeId"/>
    /// — the three valid forms are <c>*:*</c>, <c>Type:*</c>, and <c>Type:path</c>; there is no
    /// <c>*:path</c>. The sole producer (<c>StructuralContext.LowerNotReferenced</c>) upholds it structurally
    /// by deriving the reference-path id only when the source type is present, matching the sibling records'
    /// convention of trusting Lower rather than self-validating.
    /// </para>
    /// </summary>
    public sealed record NotReferencedSource(
        short TargetResourceTypeId,
        short? SourceResourceTypeId,
        short? ReferenceSearchParamId) : CteDefinition;

    /// <summary>
    /// Current rows of dbo.Resource across several resource types, or across every type — the system-wide
    /// search base set. Kept separate from <see cref="ResourceSource"/> rather than widening it to a list,
    /// because ResourceSource's single short is what lets a chain's target scope stay a scalar; conflating
    /// them would push an "exactly one" assertion into every consumer of that scope.
    /// <para>
    /// Construct via <see cref="AllTypes"/> or <see cref="ForTypes"/> rather than directly, so the
    /// "scan the whole database" state is always an explicit choice rather than an accident that falls
    /// through silently when a type list is filtered down to nothing.
    /// </para>
    /// </summary>
    public sealed record MultiTypeResourceSource : CteDefinition
    {
        private MultiTypeResourceSource(IReadOnlyList<short> resourceTypeIds, Predicate? predicate)
        {
            ResourceTypeIds = resourceTypeIds;
            Predicate = predicate;
        }

        /// <summary>The resource type ids the scan is narrowed to. Empty means every type; construct that
        /// state only through <see cref="AllTypes"/>, never by passing an empty list.</summary>
        public IReadOnlyList<short> ResourceTypeIds { get; }

        public Predicate? Predicate { get; }

        /// <summary>
        /// Every resource type — the system-wide search base set. A separate factory rather than "pass an
        /// empty list" because the two states differ by a full scan of dbo.Resource: a caller that narrowed
        /// a type list down to nothing would otherwise silently widen its query to the whole database
        /// instead of matching nothing, and the result is more rows rather than an error.
        /// </summary>
        public static MultiTypeResourceSource AllTypes(Predicate? predicate = null) => new([], predicate);

        /// <summary>
        /// A scan narrowed to the given resource type ids. Rejects an empty list: use <see cref="AllTypes"/>
        /// to ask for every type deliberately.
        /// </summary>
        public static MultiTypeResourceSource ForTypes(IReadOnlyList<short> resourceTypeIds, Predicate? predicate = null)
        {
            ArgumentNullException.ThrowIfNull(resourceTypeIds);
            if (resourceTypeIds.Count == 0)
            {
                throw new ArgumentException(
                    "An empty type list would scan every resource type. Call AllTypes() to ask for that deliberately.",
                    nameof(resourceTypeIds));
            }

            return new(resourceTypeIds, predicate);
        }
    }
}
