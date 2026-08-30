using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// One node in the compiler's CTE graph. Leaf sources scan a search-param or resource table; composing
/// nodes (Intersect/Union/Except, ChainJoin, ReferencedTypeExpansion) combine child CTEs into the match
/// set. Each concrete record documents the SQL it becomes.
/// </summary>
public abstract record CteDefinition
{
    /// <summary>
    /// One search-param table filtered by SearchParamId and optional Predicate. ResourceTypeId is required
    /// because a SearchParamId spans every type sharing its definition URL; null means system-level
    /// (cross-type) search — no type filter emitted, narrowed instead by a <see cref="MultiTypeResourceSource"/>.
    /// </summary>
    public sealed record ParamSource(TableDescriptor Table, short? ResourceTypeId, short SearchParamId, Predicate? Predicate = null) : CteDefinition;

    /// <summary>Set intersection (AND) of two CTEs.</summary>
    public sealed record Intersect(CteRef Left, CteRef Right) : CteDefinition;

    /// <summary>Set union (OR) of several CTEs.</summary>
    public sealed record Union(IReadOnlyList<CteRef> Parts) : CteDefinition;

    /// <summary>
    /// All current, non-deleted resources of a type — the base set for :not. Predicate applies only in a
    /// nested scope (e.g. a chain target) with no outer WHERE; at top level QueryPlan.OuterPredicate carries
    /// resource-column filters instead.
    /// </summary>
    public sealed record ResourceSource(short ResourceTypeId, Predicate? Predicate = null) : CteDefinition;

    /// <summary>Set subtraction — the :not operation itself.</summary>
    public sealed record Except(CteRef Left, CteRef Right) : CteDefinition;

    /// <summary>A forward or reverse chain, joined through dbo.ReferenceSearchParam and dbo.Resource.</summary>
    public sealed record ChainJoin(
        CteRef InnerMatch,
        short ReferenceSearchParamId,
        short InnerResourceTypeId,
        IReadOnlyList<short> OutputResourceTypeIds,
        ChainDirection Direction) : CteDefinition;

    /// <summary>
    /// Compartment membership: dbo.ReferenceSearchParam rows for one membership SearchParamId, any of a set
    /// of ResourceTypeIds, and a fixed compartment reference.
    /// </summary>
    public sealed record CompartmentSource(IReadOnlyList<short> ResourceTypeIds, short SearchParamId, Predicate Predicate) : CteDefinition;

    /// <summary>
    /// Resources of TargetResourceTypeId that no dbo.ReferenceSearchParam row points at — the
    /// <c>_not-referenced</c> orphan search. SourceResourceTypeId narrows to references from one type,
    /// ReferenceSearchParamId to one path; both null is the full wildcard. Invariant:
    /// ReferenceSearchParamId implies SourceResourceTypeId (no <c>*:path</c> form exists).
    /// </summary>
    public sealed record NotReferencedSource(
        short TargetResourceTypeId,
        short? SourceResourceTypeId,
        short? ReferenceSearchParamId) : CteDefinition;

    /// <summary>
    /// A raw table row-existence check, scoped only by ResourceSurrogateId (via the outer join) plus an
    /// optional Predicate. Unlike ParamSource, carries no SearchParamId or ResourceTypeId — for table-wide
    /// checks, e.g. $everything's "has ANY date-typed search-index row" or "...matching this date range".
    /// </summary>
    public sealed record TableExistsPredicate(TableDescriptor Table, Predicate? Predicate = null) : CteDefinition;

    /// <summary>
    /// $everything's _since filter — resources visible in a transaction on or after Since, joined through
    /// dbo.Resource and dbo.Transactions on VisibleDate (Transactions' incremental-visibility column, NULL
    /// until visible; distinct from CreateDate). Intersect-composed onto the compartment branch only.
    /// </summary>
    public sealed record VisibleSinceFilter(SqlParameterRef Since) : CteDefinition;

    /// <summary>
    /// $everything's referenced-type expansion — resources referenced from the filtered patient-compartment
    /// Seed, restricted to fixed types, following all outbound references. Its own node, not a ChainJoin,
    /// because ChainJoin can't express the wildcard reference parameter and wildcard source type this needs.
    /// </summary>
    public sealed record ReferencedTypeExpansion(CteRef Seed, IReadOnlyList<short> OutputResourceTypeIds) : CteDefinition;

    /// <summary>
    /// Current rows of dbo.Resource across several resource types, or every type — the system-wide base set.
    /// Separate from <see cref="ResourceSource"/> so a chain's target scope stays a scalar. Construct via
    /// <see cref="AllTypes"/> or <see cref="ForTypes"/>: "scan everything" must be deliberate, not an empty list.
    /// </summary>
    public sealed record MultiTypeResourceSource : CteDefinition
    {
        private MultiTypeResourceSource(IReadOnlyList<short> resourceTypeIds, Predicate? predicate)
        {
            ResourceTypeIds = resourceTypeIds;
            Predicate = predicate;
        }

        /// <summary>Type ids the scan is narrowed to; empty means every type (set only via <see cref="AllTypes"/>).</summary>
        public IReadOnlyList<short> ResourceTypeIds { get; }

        public Predicate? Predicate { get; }

        /// <summary>
        /// Every resource type — the system-wide base set. A separate factory rather than "pass an empty list"
        /// so a caller that narrowed a type list to nothing gets nothing, not a silent full scan of dbo.Resource.
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

    /// <summary>
    /// Materializes the canonical match page for include stages. The specification is the exact instance owned
    /// by the containing <see cref="QueryPlan"/>.
    /// </summary>
    public sealed record MatchPage(MatchPageSpec Spec) : CteDefinition;

    /// <summary>
    /// Removes a has-more probe row from a materialized <see cref="MatchPage"/> before it seeds include stages.
    /// The specification is the exact instance owned by the containing <see cref="QueryPlan"/>.
    /// </summary>
    public sealed record MatchSeed(CteRef Page, MatchPageSpec Spec) : CteDefinition;
}
