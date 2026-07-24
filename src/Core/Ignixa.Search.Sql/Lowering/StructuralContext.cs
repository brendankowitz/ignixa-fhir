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
    private int _chainDepth;

    private const int MaxChainDepth = 10;

    public StructuralContext(SymbolTable symbols, DateTimeOffset? approximationReferenceTime = null)
    {
        _leafContext = new LeafContext(symbols, approximationReferenceTime);
    }

    public IReadOnlyList<CteDefinition> Ctes => _ctes;

    public IReadOnlyList<CteOrigin> Origins => _origins;

    public LeafContext LeafContext => _leafContext;

    public CteRef Lower(SearchParameterPredicateExpression predicate, string? resourceType)
        => Lower(predicate, resourceType, provenanceNode: predicate);

    /// <summary>Lowers a leaf predicate, recording provenance against <paramref name="provenanceNode"/> rather
    /// than <paramref name="predicate"/> itself — needed at the :not clone site, where the predicate actually
    /// lowered is a synthesized positive-match clone with no place in any parameter's IR subtree.</summary>
    public CteRef Lower(SearchParameterPredicateExpression predicate, string? resourceType, Expression provenanceNode)
    {
        RejectResourceColumnCode(predicate.Parameter.Code);
        short? resourceTypeId = resourceType is null ? null : _leafContext.ResourceTypeId(resourceType);
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
                "single target-type ResourceSource the same way :not does. Guarding at LowerNotReferenced, its " +
                "own choke point, rather than at each caller.");
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

        short? resourceTypeId = resourceType is null ? null : _leafContext.ResourceTypeId(resourceType);
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
        short? resourceTypeId = resourceType is null ? null : _leafContext.ResourceTypeId(resourceType);
        var searchParamId = _leafContext.SearchParamId(parameter);

        var cte = new CteDefinition.ParamSource(table, resourceTypeId, searchParamId);
        _ctes.Add(cte);
        return new CteRef(_ctes.Count - 1);
    }

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
                "wrong-scope or always-empty match. This commonly happens when a resource-column predicate arrives " +
                "nested inside an And/Or that wasn't flattened before reaching Lower.Run -- e.g. a caller composing " +
                "And(otherExpression, existingAnd) instead of splicing into existingAnd's own children. Flatten the " +
                "composed expression before calling Lower.");
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

    public CteRef LowerResourceSource(string? resourceType) => LowerResourceSourceWithPredicate(resourceType, predicate: null);

    public CteRef LowerResourceSourceWithPredicate(string? resourceType, Predicate? predicate)
    {
        short? resourceTypeId = resourceType is null ? null : _leafContext.ResourceTypeId(resourceType);
        _ctes.Add(new CteDefinition.ResourceSource(resourceTypeId, predicate));
        return new CteRef(_ctes.Count - 1);
    }

    public CteRef LowerNot(CteRef innerMatch, string? resourceType)
    {
        if (resourceType is null)
        {
            throw new NotSupportedException(
                ":not (and :missing=true, which negates a presence set) is not supported in system-level " +
                "search in this phase -- the Except needs a single-type ResourceSource base set to subtract " +
                "from. Guarding at LowerNot, the single choke point both the :not and :missing=true paths " +
                "reach, rather than at each caller.");
        }

        return Except(LowerResourceSource(resourceType), innerMatch);
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
    {
        var membership = _leafContext.CompartmentMembership(expression.CompartmentType);
        var groups = expression.FilteredResourceTypes.Count == 0
            ? membership
            : membership
                .Select(m => (m.Parameter, ResourceTypes: (IReadOnlyList<string>)m.ResourceTypes.Where(expression.FilteredResourceTypes.Contains).ToList()))
                .Where(m => m.ResourceTypes.Count > 0)
                .ToList();

        if (groups.Count == 0)
        {
            throw new NotSupportedException(
                $"Compartment search for '{expression.CompartmentType}/{expression.CompartmentId}' resolved to " +
                "zero membership search parameters for the requested resource type(s) -- this compartment/filter " +
                "combination can never match any row. Callers should short-circuit this case before calling " +
                "Lower (matching CompartmentSearchQueryGenerator's own empty-result short-circuit today), not " +
                "rely on this throw.");
        }

        var refs = groups.Select(g =>
        {
            var cte = CompartmentLoweringRule.Lower(g.Parameter, g.ResourceTypes, expression.CompartmentType, expression.CompartmentId, _leafContext);
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
            compartmentRef = Intersect(compartmentRef, VisibleSinceFilterRef(since));
        }

        var unionParts = new List<CteRef> { patientItselfRef, compartmentRef };
        if (expression.IncludeReferencedResources)
        {
            unionParts.Add(ReferencedTypeExpansionRef(compartmentRef, ResolveReferencedTypeIds()));
        }

        return Union(unionParts);
    }

    /// <summary>Lowers the Patient-itself branch: a typed dbo.Resource base set filtered by an _id equality (an Or of equalities for Group $everything's multiple patients). Never routed through CompartmentSource, and never touched by the date/_since filters.</summary>
    private CteRef LowerPatientItself(IReadOnlyList<string> patientIds)
    {
        var idColumn = new SqlColumnRef("Resource", "ResourceId");
        var predicate = patientIds
            .Select(id => (Predicate)new Predicate.Equal(idColumn, _leafContext.Parameter(id)))
            .Aggregate((left, right) => new Predicate.Or(left, right));
        return LowerResourceSourceWithPredicate("Patient", predicate);
    }

    /// <summary>Lowers the compartment branch: the existing LowerCompartment mechanism per patient, Unioned across patients for Group $everything.</summary>
    private CteRef LowerEverythingCompartment(IReadOnlyList<string> patientIds, ISet<string> filteredResourceTypes)
    {
        var refs = patientIds
            .Select(id => LowerCompartment(new CompartmentSearchExpression("Patient", id, filteredResourceTypes)))
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
