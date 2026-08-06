using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Lowering;

namespace Ignixa.Search.Sql.Symbols;

/// <summary>
/// Walks a typed predicate tree collecting every search parameter and resource type it references, doing no
/// I/O — <see cref="Resolve"/> batches the results into <see cref="ISymbolResolver"/> calls afterward.
/// Includes and sort keys live outside the tree; Resolve feeds them via <see cref="CollectInclude"/>/<see cref="CollectSort"/>.
/// </summary>
internal sealed class SymbolCollectingVisitor : ExpressionRewriter<object?>
{
    public HashSet<SearchParameterInfo> Parameters { get; } = [];

    public HashSet<string> ResourceTypes { get; } = [];

    /// <summary>Non-empty <see cref="TokenSearchValue.System"/> values found in the tree.</summary>
    public HashSet<string> TokenSystems { get; } = new(StringComparer.Ordinal);

    /// <summary>Non-empty <see cref="QuantitySearchValue.System"/> values found in the tree.</summary>
    public HashSet<string> QuantitySystems { get; } = new(StringComparer.Ordinal);

    /// <summary>Non-empty <see cref="QuantitySearchValue.Code"/> values found in the tree.</summary>
    public HashSet<string> QuantityCodes { get; } = new(StringComparer.Ordinal);

    public override Expression VisitSearchParameterPredicate(SearchParameterPredicateExpression expression, object? context)
    {
        AddParameter(expression.Parameter);
        if (expression.Value is ReferenceSearchValue referenceValue)
        {
            if (referenceValue.ResourceType is { Length: > 0 } resourceType)
            {
                ResourceTypes.Add(resourceType);
            }
            else
            {
                // Untyped reference: collect the parameter's declared target types so Lower can narrow to
                // them (matching the shipping engine). Harmless if the value later carries an explicit type.
                foreach (var targetType in expression.Parameter.TargetResourceTypes)
                {
                    AddResourceType(targetType);
                }
            }
        }

        if (expression.Parameter.Code == "_type" && expression.Value is TokenSearchValue { Code: { Length: > 0 } typeCode })
        {
            ResourceTypes.Add(typeCode);
        }

        if (expression.Value is TokenSearchValue { System: { Length: > 0 } tokenSystem })
        {
            TokenSystems.Add(tokenSystem);
        }

        // :of-type's identifier type system is a System-table string too (the writers resolve it through the
        // same map), but it hangs off OfTypeTokenSearchValue, which is not a TokenSearchValue -- so the arm
        // above never sees it and lowering would throw KeyNotFoundException on the id lookup.
        if (expression.Value is OfTypeTokenSearchValue { TypeSystem: { Length: > 0 } identifierTypeSystem })
        {
            TokenSystems.Add(identifierTypeSystem);
        }

        if (expression.Value is QuantitySearchValue quantityValue)
        {
            if (quantityValue.System is { Length: > 0 } qSystem)
            {
                QuantitySystems.Add(qSystem);
            }

            if (quantityValue.Code is { Length: > 0 } qCode)
            {
                QuantityCodes.Add(qCode);
            }
        }

        return expression;
    }

    public override Expression VisitCompositeComponent(CompositeComponentExpression expression, object? context)
    {
        AddParameter(expression.ComponentSearchParameter);
        return base.VisitCompositeComponent(expression, context);
    }

    public override Expression VisitSearchParameter(SearchParameterExpression expression, object? context)
    {
        AddParameter(expression.Parameter);
        return base.VisitSearchParameter(expression, context);
    }

    public override Expression VisitMissingSearchParameter(MissingSearchParameterExpression expression, object? context)
    {
        AddParameter(expression.Parameter);
        return base.VisitMissingSearchParameter(expression, context);
    }

    public override Expression VisitChained(ChainedExpression expression, object? context)
    {
        AddParameter(expression.ReferenceSearchParameter);
        foreach (var resourceType in expression.ResourceTypes)
        {
            ResourceTypes.Add(resourceType);
        }

        foreach (var resourceType in expression.TargetResourceTypes)
        {
            ResourceTypes.Add(resourceType);
        }

        return base.VisitChained(expression, context);
    }

    public List<(string CompartmentType, ISet<string> FilteredResourceTypes)> Compartments { get; } = [];

    /// <summary>
    /// Records a compartment search's type and filter for Resolve to expand; does no recursion or I/O
    /// (Resolve runs the definition-manager expansion).
    /// </summary>
    public override Expression VisitCompartment(CompartmentSearchExpression expression, object? context)
    {
        AddResourceType(expression.CompartmentType);
        Compartments.Add((expression.CompartmentType, expression.FilteredResourceTypes));
        return expression;
    }

    /// <summary>
    /// Records the symbols a Patient/Group <c>$everything</c> references — the Patient type, its compartment
    /// (expanded like an ordinary compartment search with the <c>_type</c> filter), and referenced types when
    /// requested. Over-collects referenced types (superset costs one id; subset fails at lowering). No I/O.
    /// </summary>
    public override Expression VisitPatientEverything(PatientEverythingExpression expression, object? context)
    {
        ArgumentNullException.ThrowIfNull(expression);
        AddResourceType("Patient");
        Compartments.Add(("Patient", expression.FilteredResourceTypes));

        if (expression.IncludeReferencedResources)
        {
            foreach (var referencedType in Lowering.StructuralContext.PatientEverythingReferencedResourceTypes)
            {
                AddResourceType(referencedType);
            }
        }

        return expression;
    }

    /// <summary>
    /// The (source resource type, reference path) pairs of every <c>_not-referenced=Type:path</c>, for
    /// Resolve to resolve to a reference parameter. Wildcard forms contribute no pair.
    /// </summary>
    public List<(string SourceResourceType, string ReferencePath)> NotReferencedPaths { get; } = [];

    /// <summary>Records a <c>_not-referenced</c> search's source type and reference path; no I/O (Resolve resolves the path).</summary>
    public override Expression VisitNotReferenced(NotReferencedExpression expression, object? context)
    {
        ArgumentNullException.ThrowIfNull(expression);

        if (expression.SourceResourceType is { } sourceType)
        {
            AddResourceType(sourceType);

            if (expression.ReferencePath is { } path)
            {
                NotReferencedPaths.Add((sourceType, path));
            }
        }

        return expression;
    }

    /// <summary>
    /// Collects the symbols an <see cref="IncludeExpression"/> references — its reference parameter (unless
    /// wildcard) and every source/target/referenced type. Over-collects a superset rather than deriving which
    /// field each include direction uses. Called by Resolve per include, since includes are not in the tree.
    /// </summary>
    public void CollectInclude(IncludeExpression include)
    {
        if (include.ReferenceSearchParameter is not null)
        {
            AddParameter(include.ReferenceSearchParameter);
            foreach (var targetType in include.ReferenceSearchParameter.TargetResourceTypes)
            {
                AddResourceType(targetType);
            }
        }

        AddResourceType(include.SourceResourceType);
        AddResourceType(include.TargetResourceType);
        foreach (var referencedType in include.ReferencedTypes ?? [])
        {
            AddResourceType(referencedType);
        }
    }

    /// <summary>
    /// Collects a sort key's search parameter, skipping resource-column codes (_lastUpdated/_id/_type never
    /// reach a lookup). Called by Resolve per sort key, since sorts are not in the tree.
    /// </summary>
    public void CollectSort(SortExpression sort)
    {
        AddParameter(sort.Parameter);
    }

    /// <summary>
    /// Collects an access constraint's resource type and the symbols its predicate references (constraints
    /// live on SearchOptions, not the tree). AccessConstraintApplier lowers the predicate through the same
    /// dispatcher, so it needs the same symbols — without this a constraint only resolved when the user's
    /// query happened to name the same parameter.
    /// </summary>
    public void CollectConstraint(AccessConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(constraint);

        AddResourceType(constraint.ResourceType);
        constraint.Predicate.AcceptVisitor(this, context: null);
    }

    /// <summary>
    /// Records a parameter, skipping resource-column codes — those target dbo.Resource's own columns and
    /// never reach a SearchParamId lookup, so collecting them would make a resolver with no _id row report
    /// the query unresolvable when it compiles fine.
    /// </summary>
    private void AddParameter(SearchParameterInfo parameter)
    {
        if (!ResourceColumnLoweringRule.IsResourceColumnCode(parameter.Code))
        {
            Parameters.Add(parameter);
        }
    }

    private void AddResourceType(string? resourceType)
    {
        if (resourceType is { Length: > 0 } and not "*")
        {
            ResourceTypes.Add(resourceType);
        }
    }
}
