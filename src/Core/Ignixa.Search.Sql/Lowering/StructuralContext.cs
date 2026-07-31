using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Search.Sql.Lowering.Composite;
using Ignixa.Search.Sql.Lowering.Leaf;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>The structural (tier-2) context: builds the CTE graph by dispatching leaves to the leaf rules and
/// combining their results with Intersect/Union/Except. Owns the plan's Ctes list, which the leaf-tier
/// <see cref="LeafContext"/> never sees.</summary>
internal sealed class StructuralContext
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

    /// <summary>Lowers a <c>_not-referenced</c> search to a NotReferencedSource CTE: resources of the target type
    /// that no reference row points at. A named source type and reference path narrow the anti-join; a
    /// path that did not resolve to a reference parameter falls back to a source-type-only (path-agnostic)
    /// filter, matching the shipping engine.</summary>
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

        // A source type the resolver could not find yields UnmatchableResourceTypeId (-1), so the anti-join's
        // inner EXISTS is empty and NOT EXISTS is vacuously true -- every target passes. Correct here (a source
        // type that does not exist references nothing, so nothing is "referenced by it"), the opposite of the
        // sentinel's empty-match effect in a positive position. The unmatchable target type still matches nothing.
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

    public CteRef LowerParameterPresence(SearchParameterInfo parameter, string? resourceType, Expression provenanceNode)
    {
        RejectResourceColumnCode(parameter.Code);

        var table = ResolveMissingTable(parameter);
        var resourceTypeId = ResolveTypeScope(resourceType);
        var searchParamId = _leafContext.SearchParamId(parameter);

        var cte = new CteDefinition.ParamSource(table, resourceTypeId, searchParamId);
        _ctes.Add(cte);
        var index = _ctes.Count - 1;
        _origins.Add(new CteOrigin(index, provenanceNode));
        return new CteRef(index);
    }

    /// <summary>Resolves a leaf/composite rule's resource-type scope: the type's id, or null for system-level
    /// (cross-type) search, where the rule emits no ResourceTypeId filter at all. Kept as one helper so
    /// the "null means every type, do not resolve it" convention is stated once rather than repeated at
    /// each dispatch site, where an accidental <c>ResourceTypeId(null!)</c> would throw instead.</summary>
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

    /// <summary>Lowers a multi-type or system-wide base set. An unresolvable name yields the sentinel -1 and is kept,
    /// not dropped: dropping every unknown id would collapse the list to empty, and an empty
    /// <see cref="CteDefinition.MultiTypeResourceSource"/> means <em>every</em> type (a full-table scan). An
    /// empty <paramref name="resourceTypes"/> input is instead the explicit AllTypes ("all types") contract.</summary>
    public CteRef LowerMultiTypeResourceSource(IReadOnlyList<string> resourceTypes)
    {
        // ResourceTypeIdOrSentinel (not ResourceTypeId) maps an uncollected type name to -1 rather than throwing;
        // keeping -1 yields IN (-1) which matches no row, avoiding the all-unknown-collapses-to-every-type scan.
        // Empty input is the explicit AllTypes() contract (a bare GET /); ForTypes() is used for every non-empty
        // list so its guard forbids a future caller silently passing an empty list.
        CteDefinition.MultiTypeResourceSource source = resourceTypes.Count == 0
            ? CteDefinition.MultiTypeResourceSource.AllTypes()
            : CteDefinition.MultiTypeResourceSource.ForTypes(
                resourceTypes.Select(t => _leafContext.ResourceTypeIdOrSentinel(t)).ToList());

        _ctes.Add(source);
        return new CteRef(_ctes.Count - 1);
    }

    /// <summary>Folds a resource-column predicate into a system-wide dbo.Resource scan -- the cross-type counterpart of
    /// <see cref="LowerResourceSourceWithPredicate"/>. Always <c>AllTypes</c>: the leg names no type list of its
    /// own (any type constraint lives inside <paramref name="predicate"/>), and the requested <c>_type</c> list
    /// is applied later by <see cref="Lower.NarrowToRequestedTypes"/>.</summary>
    public CteRef LowerMultiTypeResourceSourceWithPredicate(Predicate? predicate)
    {
        _ctes.Add(CteDefinition.MultiTypeResourceSource.AllTypes(predicate));
        return new CteRef(_ctes.Count - 1);
    }

    public CteRef LowerNot(CteRef innerMatch, string? resourceType)
        => Except(LowerNegationAnchor(resourceType), innerMatch);

    /// <summary>The base set a negation subtracts from: every resource of <paramref name="resourceType"/>. Rejects a
    /// null (system-level) type — the single choke point every negation reaches, whether it arrives as
    /// <c>:not</c>, <c>:missing=true</c>, or the no-positive-sibling arm of this class's AND handling.
    /// Guarding here rather than at each caller is what keeps the three from diverging.</summary>
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

    /// <summary>Subtracts one match set from another. Callers that already hold a narrower left-hand set should
    /// use this directly rather than <see cref="LowerNot"/>, whose ResourceSource anchor reads every
    /// resource of the type.</summary>
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
                        $"Reverse chain's referencing side resolved to {chain.ResourceTypes.Length} types -- a reverse chain " +
                        "scopes its inner expression against exactly one referencing type, and the real binder binds it " +
                        "that way (SearchKeyBinder.BindReverse's syntax.SourceResourceType). This is the only guard on " +
                        "that shape for IR built directly against the compiler API, so it refuses rather than guessing."),
                };

                var innerMatch = lowerNode(chain.Expression, this, referencingResourceType);
                innerMatch = _accessConstraints.Apply(innerMatch, referencingResourceType, this, lowerNode);
                var referenceSearchParamId = _leafContext.SearchParamId(chain.ReferenceSearchParameter);
                var innerResourceTypeId = _leafContext.ResourceTypeId(referencingResourceType);
                var outputResourceTypeIds = chain.TargetResourceTypes switch
                {
                    { Length: > 0 } targets => targets.Select(_leafContext.ResourceTypeId).ToList(),
                    _ => throw new NotSupportedException(EmptyOutputSideMessage("Reverse", "target", "SearchKeyBinder.BindReverse")),
                };

                _ctes.Add(new CteDefinition.ChainJoin(innerMatch, referenceSearchParamId, innerResourceTypeId, outputResourceTypeIds, ChainDirection.Reverse));
                return new CteRef(_ctes.Count - 1);
            }

            var targetResourceType = chain.TargetResourceTypes switch
            {
                [var single] => single,
                _ => throw new NotSupportedException(
                    $"Forward chain resolved to {chain.TargetResourceTypes.Length} candidate target types -- a forward chain " +
                    "scopes its inner expression against exactly one target type, and the real binder resolves it that way " +
                    "before this point (SearchKeyBinder.BindForward throws ChainedParameterSpecifyType on genuine " +
                    "ambiguity). This is the only guard on that shape for IR built directly against the compiler API, so " +
                    "it refuses rather than guessing."),
            };

            var forwardInnerMatch = lowerNode(chain.Expression, this, targetResourceType);
            forwardInnerMatch = _accessConstraints.Apply(forwardInnerMatch, targetResourceType, this, lowerNode);
            var forwardReferenceSearchParamId = _leafContext.SearchParamId(chain.ReferenceSearchParameter);
            var forwardInnerResourceTypeId = _leafContext.ResourceTypeId(targetResourceType);
            var forwardOutputResourceTypeIds = chain.ResourceTypes switch
            {
                { Length: > 0 } referencing => referencing.Select(_leafContext.ResourceTypeId).ToList(),
                _ => throw new NotSupportedException(EmptyOutputSideMessage("Forward", "referencing", "SearchKeyBinder.BindForward")),
            };

            _ctes.Add(new CteDefinition.ChainJoin(forwardInnerMatch, forwardReferenceSearchParamId, forwardInnerResourceTypeId, forwardOutputResourceTypeIds, ChainDirection.Forward));
            return new CteRef(_ctes.Count - 1);
        }
        finally
        {
            _chainDepth--;
        }
    }

    /// <summary>The refusal for a chain whose <em>output</em> side named no resource type. Unlike the
    /// must-be-single sides, an empty output list is not an ambiguity but a silent malformation: the emitter
    /// renders the output types as an OR of equalities, and joining zero of them yields an empty string
    /// interpolated into the WHERE clause, so the query fails at SQL Server as an opaque 500 rather than here
    /// with a diagnosis.</summary>
    private static string EmptyOutputSideMessage(string direction, string side, string binderMethod)
        => $"{direction} chain's {side} side resolved to 0 resource types -- a chain join filters its output rows to " +
           "those types, and an empty list emits no filter at all rather than matching nothing. The real binder " +
           $"never produces this shape ({binderMethod}), so this guard covers IR built directly against the " +
           "compiler API.";

    public CteRef LowerCompartment(CompartmentSearchExpression expression)
        => LowerCompartmentCore(expression.CompartmentType, expression.CompartmentId, expression.FilteredResourceTypes);

    /// <summary>Lowers a compartment membership set to a Union of one CompartmentSource per membership search parameter,
    /// narrowing member types to <paramref name="filteredResourceTypes"/> when non-empty. Shared by an ordinary
    /// compartment search and <c>$everything</c>. A filter that narrows membership to zero groups lowers to an
    /// empty match (a <see cref="Predicate.False"/>), following the "not found is data, not an error" convention.</summary>
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
            // Named only types outside this compartment, so the answer is an empty member set, not an exception.
            // Anchor the false predicate on the compartment's own type so the CTE still emits well-typed SQL.
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

    /// <summary>The resource types $everything pulls in as "referenced resources" outside the patient compartment.
    /// Matches the legacy PatientEverythingQueryGenerator's own fixed list (Practitioner/Organization/
    /// Location/Medication) -- the FHIR spec's SHOULD-include set for the operation.</summary>
    public static readonly IReadOnlyList<string> PatientEverythingReferencedResourceTypes =
        ["Practitioner", "Organization", "Location", "Medication"];

    /// <summary>Orchestrates a Patient/Group $everything into the CTE graph, composing (in legacy's order) the Patient
    /// resource(s), the patient compartment, an optional clinical-date filter, an optional _since filter scoped
    /// to the compartment branch, and an optional referenced-type expansion seeded from the filtered compartment
    /// set. Returns a Union of those branches.</summary>
    public CteRef LowerPatientEverything(PatientEverythingExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        // Known gap: STU3/R4/R4B/R5/R6 all list Device in the Patient compartment with an empty parameter list,
        // so no compartment traversal can return one and $everything silently omits it. Closing it needs a
        // version-conditional Device.patient symbol (absent in R5+) requested at the resolve stage, not here.
        // Tracked as #379.
        var patientItselfRef = LowerPatientItself(expression.PatientIds);
        var compartmentRef = LowerEverythingCompartment(expression.PatientIds, expression.FilteredResourceTypes);

        if (expression.StartDate is not null || expression.EndDate is not null)
        {
            compartmentRef = ApplyConditionalDateFilter(compartmentRef, expression.StartDate, expression.EndDate);
        }

        if (expression.SinceDate is { } since)
        {
            // _since is answered from dbo.Transactions.VisibleDate (when the writing transaction became visible),
            // not a meta.lastUpdated/ResourceSurrogateId floor: the two disagree for a resource written before the
            // cutoff but made visible after (or vice versa). VisibleDate is what legacy filters on and row-compares.
            compartmentRef = Intersect(compartmentRef, VisibleSinceFilterRef(since));
        }

        var unionParts = new List<CteRef> { patientItselfRef, compartmentRef };

        // Resolve the expansion's types only when requested: SymbolCollectingVisitor.VisitPatientEverything
        // collects them under the same condition, so hoisting this out of the guard makes the lowerer resolve
        // symbols the collector never gathered and ResourceTypeId throws. Not hypothetical -- includeReferenced
        // is false exactly when _type is present, so $everything?_type=X failed outright.
        if (expression.IncludeReferencedResources)
        {
            var expansionTypeIds = ResolveReferencedTypeIds(expression);
            if (expansionTypeIds.Count > 0)
            {
                // The seed patient is not a member of its own compartment, so seeding the expansion from
                // compartmentRef alone misses the patient's own generalPractitioner/managingOrganization. Union
                // in patientItselfRef so those are found even in isolation.
                var expansionSeed = Union([patientItselfRef, compartmentRef]);
                unionParts.Add(ReferencedTypeExpansionRef(expansionSeed, expansionTypeIds));
            }
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

    /// <summary>Composes the conditional clinical-date filter: keep a compartment resource if it has a date-typed
    /// index row matching the range, OR if it has no date-typed index row at all. Both checks are
    /// table-wide over DateTimeSearchParam (no SearchParamId), so neither is expressible via ParamSource --
    /// hence TableExistsPredicate. Union(compartment ∩ hasMatchingDate, compartment − hasAnyDateRow).</summary>
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

    /// <summary>The referenced types the expansion may output, intersected with the request's <c>_type</c> filter so
    /// <c>$everything?_type=Encounter</c> does not emit the expansion's fixed referenced-type rows. An empty
    /// intersection means the filter excluded every referenced type and the caller drops the expansion. Finer
    /// than <c>PatientEverythingHandler</c>'s flag-clearing, so a caller that sets the flag itself gets a correct plan.</summary>
    private IReadOnlyList<short> ResolveReferencedTypeIds(PatientEverythingExpression expression)
        => PatientEverythingReferencedResourceTypes
            .Where(type => expression.FilteredResourceTypes.Count == 0 || expression.FilteredResourceTypes.Contains(type))
            .Select(_leafContext.ResourceTypeId)
            .ToList();

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
