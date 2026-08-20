using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering.Composite;
using Ignixa.Search.Sql.Lowering.Leaf;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>The structural (tier-2) context: builds the CTE graph by dispatching leaves to the leaf rules and
/// combining their results with Intersect/Union/Except. Owns the plan's Ctes list, which the leaf-tier
/// <see cref="LeafContext"/> never sees.</summary>
internal sealed class StructuralContext
{
    /// <summary>The resource types $everything pulls in as "referenced resources" outside the patient compartment.
    /// Matches the legacy PatientEverythingQueryGenerator's own fixed list (Practitioner/Organization/
    /// Location/Medication) -- the FHIR spec's SHOULD-include set for the operation.</summary>
    public static readonly IReadOnlyList<string> PatientEverythingReferencedResourceTypes =
        ["Practitioner", "Organization", "Location", "Medication"];

    private const int MaxChainDepth = 10;

    private readonly CteGraphBuilder _graph = new();
    private readonly LeafContext _leafContext;
    private readonly AccessConstraintApplier _accessConstraints;
    private int _chainDepth;

    public StructuralContext(SymbolTable symbols, DateTimeOffset? approximationReferenceTime = null)
        : this(symbols, approximationReferenceTime, accessConstraints: null)
    {
    }

    internal StructuralContext(SymbolTable symbols, DateTimeOffset? approximationReferenceTime, AccessConstraintApplier? accessConstraints)
    {
        _leafContext = new LeafContext(symbols, approximationReferenceTime);
        _accessConstraints = accessConstraints ?? new AccessConstraintApplier(null);
    }

    public IReadOnlyList<CteDefinition> Ctes => _graph.Ctes;

    public IReadOnlyList<CteOrigin> Origins => _graph.Origins;

    public LeafContext LeafContext => _leafContext;

    /// <summary>The CTE accumulator, for the structural rules this facade delegates to. They append CTE kinds
    /// (ChainJoin, CompartmentSource, TableExistsPredicate, …) that no caller outside this namespace constructs,
    /// so those kinds have no facade method of their own.</summary>
    internal CteGraphBuilder Graph => _graph;

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
        return _graph.Add(cte, provenanceNode);
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

        return _graph.Add(new CteDefinition.NotReferencedSource(targetTypeId, sourceTypeId, referenceParamId), expression);
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
        return _graph.Add(TokenTextLoweringRule.Lower(parameter, expression, _leafContext, resourceTypeId), provenanceNode);
    }

    public CteRef LowerComposite(SearchParameterInfo compositeParameter, IReadOnlyList<CompositeComponentExpression> components, string? resourceType, Expression provenanceNode)
    {
        foreach (var component in components)
        {
            RejectResourceColumnCode(component.ComponentSearchParameter.Code);
        }

        var resourceTypeId = ResolveTypeScope(resourceType);
        var cte = CompositeLoweringDispatcher.Lower(compositeParameter, components, _leafContext, resourceTypeId);
        return _graph.Add(cte, provenanceNode);
    }

    public CteRef LowerParameterPresence(SearchParameterInfo parameter, string? resourceType, Expression provenanceNode)
    {
        RejectResourceColumnCode(parameter.Code);

        var table = MissingParameterLoweringRule.ResolveMissingTable(parameter);
        var resourceTypeId = ResolveTypeScope(resourceType);
        var searchParamId = _leafContext.SearchParamId(parameter);

        var cte = new CteDefinition.ParamSource(table, resourceTypeId, searchParamId);
        return _graph.Add(cte, provenanceNode);
    }

    public CteRef Intersect(CteRef left, CteRef right) => _graph.Intersect(left, right);

    public CteRef Union(IReadOnlyList<CteRef> parts) => _graph.Union(parts);

    public CteRef LowerResourceSource(string resourceType) => LowerResourceSourceWithPredicate(resourceType, predicate: null);

    public CteRef LowerResourceSourceWithPredicate(string resourceType, Predicate? predicate)
    {
        var resourceTypeId = _leafContext.ResourceTypeId(resourceType);
        return _graph.Add(new CteDefinition.ResourceSource(resourceTypeId, predicate));
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

        return _graph.Add(source);
    }

    /// <summary>Folds a resource-column predicate into a system-wide dbo.Resource scan -- the cross-type counterpart of
    /// <see cref="LowerResourceSourceWithPredicate"/>. Always <c>AllTypes</c>: the leg names no type list of its
    /// own (any type constraint lives inside <paramref name="predicate"/>), and the requested <c>_type</c> list
    /// is applied later by <see cref="Lower.NarrowToRequestedTypes"/>.</summary>
    public CteRef LowerMultiTypeResourceSourceWithPredicate(Predicate? predicate)
        => _graph.Add(CteDefinition.MultiTypeResourceSource.AllTypes(predicate));

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
    public CteRef Except(CteRef left, CteRef right) => _graph.Except(left, right);

    /// <summary>Lowers one chain level, guarding the nesting depth. The counter stays on this facade rather than
    /// moving to <see cref="ChainLoweringRule"/> because a chain recurses back through this same instance via
    /// <paramref name="lowerNode"/> — the depth being guarded is this context's, not one rule invocation's.</summary>
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
            return ChainLoweringRule.Lower(chain, this, _accessConstraints, lowerNode);
        }
        finally
        {
            _chainDepth--;
        }
    }

    public CteRef LowerCompartment(CompartmentSearchExpression expression)
        => CompartmentSetLoweringRule.Lower(expression.CompartmentType, expression.CompartmentId, expression.FilteredResourceTypes, this);

    public CteRef LowerPatientEverything(PatientEverythingExpression expression)
        => PatientEverythingLoweringRule.Lower(expression, this);

    /// <summary>Resolves a leaf/composite rule's resource-type scope: the type's id, or null for system-level
    /// (cross-type) search, where the rule emits no ResourceTypeId filter at all. Kept as one helper so
    /// the "null means every type, do not resolve it" convention is stated once rather than repeated at
    /// each dispatch site, where an accidental <c>ResourceTypeId(null!)</c> would throw instead.</summary>
    private short? ResolveTypeScope(string? resourceType)
        => resourceType is null ? null : _leafContext.ResourceTypeId(resourceType);

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
}
