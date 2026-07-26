using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Search.Sql.Lowering.Composite;
using Ignixa.Search.Sql.Lowering.Leaf;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// The structural (tier-2) context: builds the CTE graph by dispatching leaves to the leaf rules and
/// combining their results with Intersect/Union/Except. Owns the plan's Ctes list, which the leaf-tier
/// <see cref="LeafContext"/> never sees.
/// </summary>
public sealed class StructuralContext
{
    private readonly List<CteDefinition> _ctes = [];
    private readonly List<CteOrigin> _origins = [];
    private readonly LeafContext _leafContext;
    private readonly AccessConstraintApplier _accessConstraints;
    private int _chainDepth;

    private const int MaxChainDepth = 10;

    public StructuralContext(SymbolTable symbols, DateTimeOffset? approximationReferenceTime = null)
        : this(symbols, approximationReferenceTime, accessConstraints: null)
    {
    }

    internal StructuralContext(SymbolTable symbols, DateTimeOffset? approximationReferenceTime, AccessConstraintApplier? accessConstraints)
    {
        _leafContext = new LeafContext(symbols, approximationReferenceTime);
        _accessConstraints = accessConstraints ?? new AccessConstraintApplier(null);
    }

    public IReadOnlyList<CteDefinition> Ctes => _ctes;

    public IReadOnlyList<CteOrigin> Origins => _origins;

    public LeafContext LeafContext => _leafContext;

    public CteRef Lower(SearchParameterPredicateExpression predicate, string? resourceType)
        => Lower(predicate, resourceType, provenanceNode: predicate);

    /// <summary>Lowers a leaf predicate, recording provenance against <paramref name="provenanceNode"/> rather
    /// than <paramref name="predicate"/> itself — needed at the :not clone site, where the predicate actually
    /// lowered is a synthesized positive-match clone with no place in any parameter's IR subtree.
    /// A null <paramref name="resourceType"/> is system-level search: the leaf lowers with no type scope.</summary>
    public CteRef Lower(SearchParameterPredicateExpression predicate, string? resourceType, Expression provenanceNode)
    {
        RejectResourceColumnCode(predicate.Parameter.Code);
        var resourceTypeId = ResolveTypeScope(resourceType);
        var cte = LeafLoweringDispatcher.Lower(predicate, _leafContext, resourceTypeId);
        _ctes.Add(cte);
        var index = _ctes.Count - 1;
        _origins.Add(new CteOrigin(index, provenanceNode));
        return new CteRef(index);
    }

    /// <summary>
    /// Lowers a <c>_not-referenced</c> search to a NotReferencedSource CTE: resources of the target type
    /// that no reference row points at. A named source type and reference path narrow the anti-join; a
    /// path that did not resolve to a reference parameter falls back to a source-type-only (path-agnostic)
    /// filter, matching the shipping engine.
    /// </summary>
    public CteRef LowerNotReferenced(NotReferencedExpression expression, string? resourceType)
    {
        if (resourceType is null)
        {
            throw new NotSupportedException(
                "_not-referenced is not supported in system-level search in this phase -- it anchors on a " +
                "single target-type dbo.Resource scan the same way :not does. Guarding at LowerNotReferenced, " +
                "its own choke point, rather than at each caller.");
        }

        var targetTypeId = _leafContext.ResourceTypeId(resourceType);

        // A source type the resolver could not find yields UnmatchableResourceTypeId (-1), which Emit
        // renders as `rsp.ResourceTypeId = -1` inside the anti-join subquery. No row has that id, so the
        // inner EXISTS is empty and NOT EXISTS is vacuously true -- every target passes. That is the
        // OPPOSITE of the sentinel's effect in a positive position (an empty match), yet it is the correct
        // answer here: a source type that does not exist has no reference rows, so no target is referenced
        // by it, so all targets are "not referenced by it". The unmatchable target type at the outer scan
        // still (correctly) matches nothing.
        short? sourceTypeId = expression.SourceResourceType is { } sourceType
            ? _leafContext.ResourceTypeId(sourceType)
            : null;

        short? referenceParamId =
            expression.SourceResourceType is { } src
            && expression.ReferencePath is { } path
            && _leafContext.NotReferencedPath(src, path) is { } parameter
                ? _leafContext.SearchParamId(parameter)
                : null;

        _ctes.Add(new CteDefinition.NotReferencedSource(targetTypeId, sourceTypeId, referenceParamId));
        var index = _ctes.Count - 1;
        _origins.Add(new CteOrigin(index, expression));
        return new CteRef(index);
    }

    /// <summary>Lowers a <c>:text</c> search, which reads dbo.TokenText rather than a search-param table.</summary>
    public CteRef LowerTokenText(SearchParameterInfo parameter, StringExpression expression, string? resourceType, Expression provenanceNode)
    {
        if (resourceType is null)
        {
            throw new NotSupportedException(
                ":text is not supported in system-level search in this phase -- TokenTextLoweringRule scopes " +
                "its match to a single ResourceTypeId. Guarding at LowerTokenText, its own choke point, rather " +
                "than at each caller.");
        }

        var resourceTypeId = _leafContext.ResourceTypeId(resourceType);
        _ctes.Add(TokenTextLoweringRule.Lower(parameter, expression, _leafContext, resourceTypeId));
        var index = _ctes.Count - 1;
        _origins.Add(new CteOrigin(index, provenanceNode));
        return new CteRef(index);
    }

    public CteRef LowerComposite(SearchParameterInfo compositeParameter, IReadOnlyList<CompositeComponentExpression> components, string? resourceType, Expression provenanceNode)
    {
        foreach (var component in components)
        {
            RejectResourceColumnCode(component.ComponentSearchParameter.Code);
        }

        var resourceTypeId = ResolveTypeScope(resourceType);
        var cte = CompositeLoweringDispatcher.Lower(compositeParameter, components, _leafContext, resourceTypeId);
        _ctes.Add(cte);
        var index = _ctes.Count - 1;
        _origins.Add(new CteOrigin(index, provenanceNode));
        return new CteRef(index);
    }

    public CteRef LowerParameterPresence(SearchParameterInfo parameter, string? resourceType)
    {
        RejectResourceColumnCode(parameter.Code);

        var table = ResolveMissingTable(parameter);
        var resourceTypeId = ResolveTypeScope(resourceType);
        var searchParamId = _leafContext.SearchParamId(parameter);

        var cte = new CteDefinition.ParamSource(table, resourceTypeId, searchParamId);
        _ctes.Add(cte);
        return new CteRef(_ctes.Count - 1);
    }

    /// <summary>
    /// Resolves a leaf/composite rule's resource-type scope: the type's id, or null for system-level
    /// (cross-type) search, where the rule emits no ResourceTypeId filter at all. Kept as one helper so
    /// the "null means every type, do not resolve it" convention is stated once rather than repeated at
    /// each dispatch site, where an accidental <c>ResourceTypeId(null!)</c> would throw instead.
    /// </summary>
    private short? ResolveTypeScope(string? resourceType)
        => resourceType is null ? null : _leafContext.ResourceTypeId(resourceType);

    private static TableDescriptor ResolveMissingTable(SearchParameterInfo parameter)
    {
        if (parameter.Type == SearchParamType.Composite)
        {
            return ResolveMissingCompositeTable(parameter);
        }

        var tableName = parameter.Type switch
        {
            SearchParamType.String => "StringSearchParam",
            SearchParamType.Token => "TokenSearchParam",
            SearchParamType.Reference => "ReferenceSearchParam",
            SearchParamType.Uri => "UriSearchParam",
            SearchParamType.Number => "NumberSearchParam",
            SearchParamType.Quantity => "QuantitySearchParam",
            SearchParamType.Date => "DateTimeSearchParam",
            _ => throw new NotSupportedException(
                $":missing is not supported for search parameter type '{parameter.Type}' on '{parameter.Code}'."),
        };

        return SqlCatalog.Default.Table(tableName);
    }

    private static TableDescriptor ResolveMissingCompositeTable(SearchParameterInfo parameter)
    {
        var componentTypes = parameter.Component.Select(c => c.ResolvedSearchParameter?.Type).ToArray();

        var tableName = componentTypes switch
        {
            [SearchParamType.Token, SearchParamType.Token] => "TokenTokenCompositeSearchParam",
            [SearchParamType.Token, SearchParamType.Number, SearchParamType.Number] => "TokenNumberNumberCompositeSearchParam",
            [SearchParamType.Token, SearchParamType.String] => "TokenStringCompositeSearchParam",
            [SearchParamType.Token, SearchParamType.Quantity] => "TokenQuantityCompositeSearchParam",
            [SearchParamType.Token, SearchParamType.Date] => "TokenDateTimeCompositeSearchParam",
            [SearchParamType.Reference, SearchParamType.Token] => "ReferenceTokenCompositeSearchParam",
            [SearchParamType.Token, SearchParamType.Reference] => "ReferenceTokenCompositeSearchParam",
            var types => throw new NotSupportedException(
                $":missing is not supported for composite search parameter '{parameter.Code}' with component types " +
                $"[{string.Join(", ", types.Select(t => t?.ToString() ?? "unresolved"))}] -- no matching composite table."),
        };

        return SqlCatalog.Default.Table(tableName);
    }

    private static void RejectResourceColumnCode(string parameterCode)
    {
        if (ResourceColumnLoweringRule.IsResourceColumnCode(parameterCode))
        {
            throw new NotSupportedException(
                $"A resource-column predicate ('{parameterCode}') reached the leaf/composite dispatch — only " +
                "Lower.Run's top-level extraction pass (via ResourceColumnLoweringRule) handles these. Guarding here, " +
                "at the dispatch choke point, covers every caller of Lower/LowerComposite structurally. Throwing " +
                "rather than routing a resource column into an unrelated table, which would silently produce a " +
                "wrong-scope or always-empty match.");
        }
    }

    public CteRef Intersect(CteRef left, CteRef right)
    {
        _ctes.Add(new CteDefinition.Intersect(left, right));
        return new CteRef(_ctes.Count - 1);
    }

    public CteRef Union(IReadOnlyList<CteRef> parts)
    {
        _ctes.Add(new CteDefinition.Union(parts));
        return new CteRef(_ctes.Count - 1);
    }

    public CteRef LowerResourceSource(string resourceType) => LowerResourceSourceWithPredicate(resourceType, predicate: null);

    public CteRef LowerResourceSourceWithPredicate(string resourceType, Predicate? predicate)
    {
        var resourceTypeId = _leafContext.ResourceTypeId(resourceType);
        _ctes.Add(new CteDefinition.ResourceSource(resourceTypeId, predicate));
        return new CteRef(_ctes.Count - 1);
    }

    /// <summary>
    /// Lowers a multi-type or system-wide base set. Each name is resolved through the symbol table; an
    /// unresolvable name yields the sentinel -1, which is kept in the list rather than dropped.
    /// <para>
    /// Dropping unresolvable ids would be dangerous: if every requested type is unknown the list would
    /// collapse to empty, and an empty <see cref="CteDefinition.MultiTypeResourceSource"/> means
    /// <em>every</em> resource type — a full-table scan instead of an empty match. The sentinel -1
    /// matches no row, so keeping it produces the correct empty result without widening the query.
    /// </para>
    /// <para>
    /// An empty <paramref name="resourceTypes"/> input is the explicit system-wide contract ("all types"):
    /// <see cref="CteDefinition.MultiTypeResourceSource.AllTypes"/> is called in that case so the intent
    /// is named rather than inferred from an empty list.
    /// </para>
    /// </summary>
    public CteRef LowerMultiTypeResourceSource(IReadOnlyList<string> resourceTypes)
    {
        // Use ResourceTypeIdOrSentinel rather than ResourceTypeId so that a type name not present in the
        // symbol table (never collected) maps to -1 rather than throwing. This matters for the fail-safe
        // contract: dropping unresolvable ids would collapse an all-unknown list to empty, which means
        // "every resource type" — a full-table scan instead of the correct empty result. Keeping -1
        // produces IN (-1), which matches no row. See also the comment at EmitMultiTypeResourceSource.
        //
        // An empty resourceTypes input is the explicit system-wide contract ("all types"): the caller at
        // LowerBaseSet deliberately passes an empty list for a bare GET /. Use AllTypes() in that case to
        // make the intent unambiguous; use ForTypes() for every non-empty list so the guard in ForTypes
        // enforces that no future caller can accidentally pass an empty list and silently widen.
        CteDefinition.MultiTypeResourceSource source = resourceTypes.Count == 0
            ? CteDefinition.MultiTypeResourceSource.AllTypes()
            : CteDefinition.MultiTypeResourceSource.ForTypes(
                resourceTypes.Select(t => _leafContext.ResourceTypeIdOrSentinel(t)).ToList());

        _ctes.Add(source);
        return new CteRef(_ctes.Count - 1);
    }

    public CteRef LowerNot(CteRef innerMatch, string? resourceType)
        => Except(LowerNegationAnchor(resourceType), innerMatch);

    /// <summary>
    /// The base set a negation subtracts from: every resource of <paramref name="resourceType"/>. Rejects a
    /// null (system-level) type — the single choke point every negation reaches, whether it arrives as
    /// <c>:not</c>, <c>:missing=true</c>, or the no-positive-sibling arm of <see cref="Lower"/>'s AND
    /// handling. Guarding here rather than at each caller is what keeps the three from diverging.
    /// </summary>
    public CteRef LowerNegationAnchor(string? resourceType)
    {
        if (resourceType is null)
        {
            throw new NotSupportedException(
                ":not (and :missing=true, which negates a presence set) is not supported in system-level " +
                "search in this phase -- the Except needs a single-type base set to subtract from, and " +
                "subtracting from every resource in the database is neither what the caller asked for nor " +
                "something the emitter can bound. Guarding at the negation anchor, the single choke point " +
                "every negation path reaches, rather than at each caller.");
        }

        return LowerResourceSource(resourceType);
    }

    /// <summary>
    /// Subtracts one match set from another. Callers that already hold a narrower left-hand set should
    /// use this directly rather than <see cref="LowerNot"/>, whose ResourceSource anchor reads every
    /// resource of the type.
    /// </summary>
    public CteRef Except(CteRef left, CteRef right)
    {
        _ctes.Add(new CteDefinition.Except(left, right));
        return new CteRef(_ctes.Count - 1);
    }

    public CteRef LowerChain(ChainedExpression chain, Func<Expression, StructuralContext, string, CteRef> lowerNode)
    {
        _chainDepth++;
        if (_chainDepth > MaxChainDepth)
        {
            throw new NotSupportedException(
                $"Chain nesting exceeds this compiler's 10-level depth guard — a robustness ceiling against SQL Server " +
                "optimizer degradation under deeply nested CTE chains, not a FHIR-spec limit. If a real query " +
                "legitimately needs more than 10 chain levels, raise this threshold deliberately.");
        }

        try
        {
            if (chain.Reversed)
            {
                var referencingResourceType = chain.ResourceTypes switch
                {
                    [var single] => single,
                    _ => throw new NotSupportedException(
                        $"Reverse chain's referencing side resolved to {chain.ResourceTypes.Length} types -- the real binder " +
                        "always binds a reverse chain's target expression against a single referencing type " +
                        "(SearchKeyBinder.BindReverse's syntax.SourceResourceType), so this is unexpected input."),
                };

                var innerMatch = lowerNode(chain.Expression, this, referencingResourceType);
                innerMatch = _accessConstraints.Apply(innerMatch, referencingResourceType, this, lowerNode);
                var referenceSearchParamId = _leafContext.SearchParamId(chain.ReferenceSearchParameter);
                var innerResourceTypeId = _leafContext.ResourceTypeId(referencingResourceType);
                var outputResourceTypeIds = chain.TargetResourceTypes.Select(_leafContext.ResourceTypeId).ToList();

                _ctes.Add(new CteDefinition.ChainJoin(innerMatch, referenceSearchParamId, innerResourceTypeId, outputResourceTypeIds, ChainDirection.Reverse));
                return new CteRef(_ctes.Count - 1);
            }

            var targetResourceType = chain.TargetResourceTypes switch
            {
                [var single] => single,
                _ => throw new NotSupportedException(
                    $"Forward chain resolved to {chain.TargetResourceTypes.Length} candidate target types -- the real binder " +
                    "always resolves forward chains to exactly one target type before this point (SearchKeyBinder.BindForward " +
                    "throws ChainedParameterSpecifyType on genuine ambiguity), so this is unexpected input."),
            };

            var forwardInnerMatch = lowerNode(chain.Expression, this, targetResourceType);
            forwardInnerMatch = _accessConstraints.Apply(forwardInnerMatch, targetResourceType, this, lowerNode);
            var forwardReferenceSearchParamId = _leafContext.SearchParamId(chain.ReferenceSearchParameter);
            var forwardInnerResourceTypeId = _leafContext.ResourceTypeId(targetResourceType);
            var forwardOutputResourceTypeIds = chain.ResourceTypes.Select(_leafContext.ResourceTypeId).ToList();

            _ctes.Add(new CteDefinition.ChainJoin(forwardInnerMatch, forwardReferenceSearchParamId, forwardInnerResourceTypeId, forwardOutputResourceTypeIds, ChainDirection.Forward));
            return new CteRef(_ctes.Count - 1);
        }
        finally
        {
            _chainDepth--;
        }
    }

    public CteRef LowerCompartment(CompartmentSearchExpression expression)
        => LowerCompartmentCore(expression.CompartmentType, expression.CompartmentId, expression.FilteredResourceTypes);

    /// <summary>
    /// Lowers a compartment membership set to a Union of one CompartmentSource per membership search
    /// parameter, narrowing member types to <paramref name="filteredResourceTypes"/> when non-empty.
    /// Shared by an ordinary compartment search and by <c>$everything</c> so both reach the identical
    /// CompartmentSource emitter rather than a parallel implementation.
    /// <para>
    /// A <paramref name="filteredResourceTypes"/> filter that narrows the membership to zero groups is the
    /// same situation for both callers -- an ordinary <c>GET /Patient/123/NotInCompartment</c> naming a type
    /// outside the compartment, or a <c>$everything?_type=foo</c> doing exactly the same -- namely
    /// caller-supplied input describing something this compartment cannot contain. Both lower to an empty
    /// match: a <see cref="Predicate.False"/> anchored on the compartment's own type, carrying the reason.
    /// This follows <see cref="ISymbolResolver"/>'s "not found is data, not an error" convention that the
    /// rest of the compiler already applies (<c>TokenColumnEquality</c> on an unknown system,
    /// <c>QuantityColumnPredicate</c> on an unknown unit, an unresolvable resource type); answering the
    /// compartment case the same way keeps it from being the lone path that turns a can-never-match filter
    /// into a thrown 500. There is no membership short-circuit ahead of this in the compiler --
    /// <c>Lower.Run</c> compiles <c>GET /Patient/{id}/{nonMemberType}</c> straight through here -- so a throw
    /// would be reachable directly from user input.
    /// </para>
    /// </summary>
    private CteRef LowerCompartmentCore(
        string compartmentType,
        string compartmentId,
        ISet<string> filteredResourceTypes)
    {
        var membership = _leafContext.CompartmentMembership(compartmentType);
        var groups = filteredResourceTypes.Count == 0
            ? membership
            : membership
                .Select(m => (m.Parameter, ResourceTypes: (IReadOnlyList<string>)m.ResourceTypes.Where(filteredResourceTypes.Contains).ToList()))
                .Where(m => m.ResourceTypes.Count > 0)
                .ToList();

        if (groups.Count == 0)
        {
            // The compartment/_type filter named only types outside this compartment, so the correct answer
            // is an empty member set, not an exception -- the same shape an unresolvable token system or
            // resource type lowers to. Anchor the false predicate on the compartment's own type so the CTE
            // still emits valid, well-typed SQL (WHERE ResourceTypeId = @p AND 1 = 0), and keep the reason so
            // the trace reports the known miss.
            var reason =
                $"Compartment search for '{compartmentType}/{compartmentId}' resolved to " +
                "zero membership search parameters for the requested resource type(s) -- this compartment/filter " +
                "combination can never match any row.";

            return LowerResourceSourceWithPredicate(compartmentType, new Predicate.False(reason));
        }

        var refs = groups.Select(g =>
        {
            var cte = CompartmentLoweringRule.Lower(g.Parameter, g.ResourceTypes, compartmentType, compartmentId, _leafContext);
            _ctes.Add(cte);
            return new CteRef(_ctes.Count - 1);
        }).ToList();

        return Union(refs);
    }

    /// <summary>
    /// The resource types $everything pulls in as "referenced resources" outside the patient compartment.
    /// Matches the legacy PatientEverythingQueryGenerator's own fixed list (Practitioner/Organization/
    /// Location/Medication) -- the FHIR spec's SHOULD-include set for the operation.
    /// </summary>
    public static readonly IReadOnlyList<string> PatientEverythingReferencedResourceTypes =
        ["Practitioner", "Organization", "Location", "Medication"];

    /// <summary>
    /// Orchestrates a Patient/Group $everything into the CTE graph, composing five pieces in legacy's own
    /// order: (1) the Patient resource(s) themselves, (2) the patient compartment, (3) an optional
    /// conditional clinical-date filter, (4) an optional _since incremental filter scoped to the compartment
    /// branch only, and (5) an optional referenced-type expansion seeded from the filtered compartment set.
    /// The result is a Union of the Patient-itself branch, the (filtered) compartment branch, and -- when
    /// requested -- the referenced-type expansion.
    /// </summary>
    /// <remarks>
    /// Paging model: one windowed query over the whole union, deliberately NOT the shipping engine's
    /// phased walk. Microsoft's $everything pages in four phases behind a continuation token -- phase 1
    /// the patient plus its generalPractitioner/managingOrganization, phases 2-3 the compartment, phase 4
    /// devices referencing the patient -- and the captured legacy corpus SQL is phase 1 alone. Phasing was
    /// considered and rejected: phase 1 union phases 2-3 union phase 4 is the same resource set this
    /// method's own union already produces, so the phases are how that engine assembles the result, not
    /// what the operation returns. Reproducing them would need a phase concept this compiler does not
    /// have, and four round trips where one suffices.
    /// <para>
    /// Consequently this node contributes no paging machinery of its own: the window is the ordinary
    /// keyset <c>PageSpec</c> or <c>OffsetSpec</c> the shape emitters already apply to any match set,
    /// which reach the union's output rather than any one arm. That is only safe because every structural
    /// Union here emits a de-duplicating UNION, so (T1, Sid1) is unique across the arms and the
    /// (T1 ASC, Sid1 ASC) ordering the keyset seek predicate mirrors is a total order over the whole
    /// result. A UNION ALL here would leave the page boundary undefined between two arms and silently
    /// duplicate or drop resources between pages -- no text-level test would see it.
    /// </para>
    /// <para>
    /// Two consequences accepted knowingly. Phased paging bounds memory per phase for a very large
    /// compartment; a single windowed query relies on the window to do that instead. And the legacy shape
    /// orders <c>IsMatch DESC</c> first, so its outbound expansion rows follow every match across page
    /// boundaries, where here the expansion is part of the match set and interleaves by (T1, Sid1). Both
    /// are reversible: nothing in this lowering forecloses adding phases later.
    /// </para>
    /// </remarks>
    public CteRef LowerPatientEverything(PatientEverythingExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        var patientItselfRef = LowerPatientItself(expression.PatientIds);
        var compartmentRef = LowerEverythingCompartment(expression.PatientIds, expression.FilteredResourceTypes);

        if (expression.StartDate is not null || expression.EndDate is not null)
        {
            compartmentRef = ApplyConditionalDateFilter(compartmentRef, expression.StartDate, expression.EndDate);
        }

        if (expression.SinceDate is { } since)
        {
            // _since is answered from dbo.Transactions.VisibleDate -- when the writing transaction became
            // visible -- not from a meta.lastUpdated floor expressed as a ResourceSurrogateId bound. The
            // two are not interchangeable: a resource written before the cutoff in a transaction that only
            // became visible after it is returned by the first and missed by the second, and a resource in
            // a transaction still awaiting visibility is returned by the second and correctly withheld by
            // the first. VisibleDate is what the legacy PatientEverythingQueryGenerator filters on and what
            // this compiler's output has been row-compared against, so it is the definition kept here.
            compartmentRef = Intersect(compartmentRef, VisibleSinceFilterRef(since));
        }

        var unionParts = new List<CteRef> { patientItselfRef, compartmentRef };
        if (expression.IncludeReferencedResources)
        {
            // The seed patient is not a member of its own compartment -- no ReferenceSearchParam row points
            // from the patient at itself -- so seeding the expansion from compartmentRef alone misses the
            // patient's own generalPractitioner/managingOrganization unless some compartment member happens
            // to reference them too. Union in patientItselfRef so those two are found even in isolation.
            var expansionSeed = Union([patientItselfRef, compartmentRef]);
            unionParts.Add(ReferencedTypeExpansionRef(expansionSeed, ResolveReferencedTypeIds()));
        }

        return Union(unionParts);
    }

    /// <summary>Lowers the Patient-itself branch: a typed dbo.Resource base set filtered by an _id equality (an Or of equalities for Group $everything's multiple patients). Never routed through CompartmentSource, and never touched by the date/_since filters.</summary>
    private CteRef LowerPatientItself(IReadOnlyList<string> patientIds)
    {
        var idColumn = new SqlColumnRef(SqlCatalog.Default.Table("Resource").TableName, "ResourceId");
        var predicate = patientIds
            .Select(id => (Predicate)new Predicate.Equal(idColumn, _leafContext.Parameter(id)))
            .Aggregate((left, right) => new Predicate.Or(left, right));
        return LowerResourceSourceWithPredicate("Patient", predicate);
    }

    /// <summary>Lowers the compartment branch: the existing compartment mechanism per patient, Unioned across patients for Group $everything.</summary>
    private CteRef LowerEverythingCompartment(IReadOnlyList<string> patientIds, ISet<string> filteredResourceTypes)
    {
        var refs = patientIds
            .Select(id => LowerCompartmentCore("Patient", id, filteredResourceTypes))
            .ToList();
        return refs.Count == 1 ? refs[0] : Union(refs);
    }

    /// <summary>
    /// Composes the conditional clinical-date filter: keep a compartment resource if it has a date-typed
    /// index row matching the range, OR if it has no date-typed index row at all. Both checks are
    /// table-wide over DateTimeSearchParam (no SearchParamId), so neither is expressible via ParamSource --
    /// hence TableExistsPredicate. Union(compartment ∩ hasMatchingDate, compartment − hasAnyDateRow).
    /// </summary>
    private CteRef ApplyConditionalDateFilter(CteRef compartmentRef, DateTimeOffset? startDate, DateTimeOffset? endDate)
    {
        var table = SqlCatalog.Default.Table("DateTimeSearchParam");
        var matchingDateRef = TableExistsPredicateRef(table, BuildDateRangePredicate(table, startDate, endDate));
        var noDateRef = TableExistsPredicateRef(table, predicate: null);
        return Union([Intersect(compartmentRef, matchingDateRef), Except(compartmentRef, noDateRef)]);
    }

    /// <summary>Builds the clinical-date range-overlap predicate over DateTimeSearchParam's [StartDateTime, EndDateTime], matching legacy's EndDateTime &gt;= start / StartDateTime &lt;= end. At least one bound is present (the caller guards).</summary>
    private Predicate BuildDateRangePredicate(TableDescriptor table, DateTimeOffset? startDate, DateTimeOffset? endDate)
    {
        var startColumn = new SqlColumnRef(table.TableName, "StartDateTime");
        var endColumn = new SqlColumnRef(table.TableName, "EndDateTime");

        Predicate? predicate = null;
        if (startDate is { } start)
        {
            predicate = new Predicate.GreaterThanOrEqual(endColumn, _leafContext.Parameter(start));
        }

        if (endDate is { } end)
        {
            var endClause = new Predicate.LessThanOrEqual(startColumn, _leafContext.Parameter(end));
            predicate = predicate is null ? endClause : new Predicate.And(predicate, endClause);
        }

        return predicate
            ?? throw new InvalidOperationException("BuildDateRangePredicate reached with neither startDate nor endDate -- ApplyConditionalDateFilter's own guard should have prevented this.");
    }

    private IReadOnlyList<short> ResolveReferencedTypeIds()
        => PatientEverythingReferencedResourceTypes.Select(_leafContext.ResourceTypeId).ToList();

    private CteRef TableExistsPredicateRef(TableDescriptor table, Predicate? predicate)
    {
        _ctes.Add(new CteDefinition.TableExistsPredicate(table, predicate));
        return new CteRef(_ctes.Count - 1);
    }

    private CteRef VisibleSinceFilterRef(DateTimeOffset since)
    {
        _ctes.Add(new CteDefinition.VisibleSinceFilter(new SqlParameterRef(since.DateTime)));
        return new CteRef(_ctes.Count - 1);
    }

    private CteRef ReferencedTypeExpansionRef(CteRef seed, IReadOnlyList<short> outputResourceTypeIds)
    {
        _ctes.Add(new CteDefinition.ReferencedTypeExpansion(seed, outputResourceTypeIds));
        return new CteRef(_ctes.Count - 1);
    }
}
