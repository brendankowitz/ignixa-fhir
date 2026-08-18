using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>Lowers a Patient/Group <c>$everything</c> into the CTE graph. Kept as one rule because its branches
/// (the patient row, the compartment, the clinical-date filter, <c>_since</c>, and the referenced-type
/// expansion) compose in an order that reproduces the legacy PatientEverythingQueryGenerator's output, and
/// that order is only readable when they sit together.</summary>
internal static class PatientEverythingLoweringRule
{
    /// <summary>Orchestrates a Patient/Group $everything into the CTE graph, composing (in legacy's order) the Patient
    /// resource(s), the patient compartment, an optional clinical-date filter, an optional _since filter scoped
    /// to the compartment branch, and an optional referenced-type expansion seeded from the filtered compartment
    /// set. Returns a Union of those branches.</summary>
    public static CteRef Lower(PatientEverythingExpression expression, StructuralContext context)
    {
        ArgumentNullException.ThrowIfNull(expression);

        // Known gap: STU3/R4/R4B/R5/R6 all list Device in the Patient compartment with an empty parameter list,
        // so no compartment traversal can return one and $everything silently omits it. Closing it needs a
        // version-conditional Device.patient symbol (absent in R5+) requested at the resolve stage, not here.
        // Tracked as #379.
        var patientItselfRef = LowerPatientItself(expression.PatientIds, context);
        var compartmentRef = LowerEverythingCompartment(expression.PatientIds, expression.FilteredResourceTypes, context);

        if (expression.StartDate is not null || expression.EndDate is not null)
        {
            compartmentRef = ApplyConditionalDateFilter(compartmentRef, expression.StartDate, expression.EndDate, context);
        }

        if (expression.SinceDate is { } since)
        {
            // _since is answered from dbo.Transactions.VisibleDate (when the writing transaction became visible),
            // not a meta.lastUpdated/ResourceSurrogateId floor: the two disagree for a resource written before the
            // cutoff but made visible after (or vice versa). VisibleDate is what legacy filters on and row-compares.
            compartmentRef = context.Intersect(compartmentRef, VisibleSinceFilterRef(since, context));
        }

        var unionParts = new List<CteRef> { patientItselfRef, compartmentRef };

        // Resolve the expansion's types only when requested: SymbolCollectingVisitor.VisitPatientEverything
        // collects them under the same condition, so hoisting this out of the guard makes the lowerer resolve
        // symbols the collector never gathered and ResourceTypeId throws. Not hypothetical -- includeReferenced
        // is false exactly when _type is present, so $everything?_type=X failed outright.
        if (expression.IncludeReferencedResources)
        {
            var expansionTypeIds = ResolveReferencedTypeIds(expression, context);
            if (expansionTypeIds.Count > 0)
            {
                // The seed patient is not a member of its own compartment, so seeding the expansion from
                // compartmentRef alone misses the patient's own generalPractitioner/managingOrganization. Union
                // in patientItselfRef so those are found even in isolation.
                var expansionSeed = context.Union([patientItselfRef, compartmentRef]);
                unionParts.Add(ReferencedTypeExpansionRef(expansionSeed, expansionTypeIds, context));
            }
        }

        return context.Union(unionParts);
    }

    /// <summary>Lowers the Patient-itself branch: a typed dbo.Resource base set filtered by an _id equality (an Or of equalities for Group $everything's multiple patients). Never routed through CompartmentSource, and never touched by the date/_since filters.</summary>
    private static CteRef LowerPatientItself(IReadOnlyList<string> patientIds, StructuralContext context)
    {
        var idColumn = new SqlColumnRef(SqlCatalog.Default.Table("Resource").TableName, "ResourceId");
        var predicate = patientIds
            .Select(id => (Predicate)new Predicate.Equal(idColumn, context.LeafContext.Parameter(id)))
            .Aggregate((left, right) => new Predicate.Or(left, right));
        return context.LowerResourceSourceWithPredicate("Patient", predicate);
    }

    /// <summary>Lowers the compartment branch: the existing compartment mechanism per patient, Unioned across patients for Group $everything.</summary>
    private static CteRef LowerEverythingCompartment(IReadOnlyList<string> patientIds, ISet<string> filteredResourceTypes, StructuralContext context)
    {
        var refs = patientIds
            .Select(id => CompartmentSetLoweringRule.Lower("Patient", id, filteredResourceTypes, context))
            .ToList();
        return refs.Count == 1 ? refs[0] : context.Union(refs);
    }

    /// <summary>Composes the conditional clinical-date filter: keep a compartment resource if it has a date-typed
    /// index row matching the range, OR if it has no date-typed index row at all. Both checks are
    /// table-wide over DateTimeSearchParam (no SearchParamId), so neither is expressible via ParamSource --
    /// hence TableExistsPredicate. Union(compartment ∩ hasMatchingDate, compartment − hasAnyDateRow).</summary>
    private static CteRef ApplyConditionalDateFilter(CteRef compartmentRef, DateTimeOffset? startDate, DateTimeOffset? endDate, StructuralContext context)
    {
        var table = SqlCatalog.Default.Table("DateTimeSearchParam");
        var matchingDateRef = TableExistsPredicateRef(table, BuildDateRangePredicate(table, startDate, endDate, context), context);
        var noDateRef = TableExistsPredicateRef(table, predicate: null, context);
        return context.Union([context.Intersect(compartmentRef, matchingDateRef), context.Except(compartmentRef, noDateRef)]);
    }

    /// <summary>Builds the clinical-date range-overlap predicate over DateTimeSearchParam's [StartDateTime, EndDateTime], matching legacy's EndDateTime &gt;= start / StartDateTime &lt;= end. At least one bound is present (the caller guards).</summary>
    private static Predicate BuildDateRangePredicate(TableDescriptor table, DateTimeOffset? startDate, DateTimeOffset? endDate, StructuralContext context)
    {
        var startColumn = new SqlColumnRef(table.TableName, "StartDateTime");
        var endColumn = new SqlColumnRef(table.TableName, "EndDateTime");

        Predicate? predicate = null;
        if (startDate is { } start)
        {
            predicate = new Predicate.GreaterThanOrEqual(endColumn, context.LeafContext.Parameter(start));
        }

        if (endDate is { } end)
        {
            var endClause = new Predicate.LessThanOrEqual(startColumn, context.LeafContext.Parameter(end));
            predicate = predicate is null ? endClause : new Predicate.And(predicate, endClause);
        }

        return predicate
            ?? throw new InvalidOperationException("BuildDateRangePredicate reached with neither startDate nor endDate -- ApplyConditionalDateFilter's own guard should have prevented this.");
    }

    /// <summary>The referenced types the expansion may output, intersected with the request's <c>_type</c> filter so
    /// <c>$everything?_type=Encounter</c> does not emit the expansion's fixed referenced-type rows. An empty
    /// intersection means the filter excluded every referenced type and the caller drops the expansion. Finer
    /// than <c>PatientEverythingHandler</c>'s flag-clearing, so a caller that sets the flag itself gets a correct plan.</summary>
    private static IReadOnlyList<short> ResolveReferencedTypeIds(PatientEverythingExpression expression, StructuralContext context)
        => StructuralContext.PatientEverythingReferencedResourceTypes
            .Where(type => expression.FilteredResourceTypes.Count == 0 || expression.FilteredResourceTypes.Contains(type))
            .Select(context.LeafContext.ResourceTypeId)
            .ToList();

    private static CteRef TableExistsPredicateRef(TableDescriptor table, Predicate? predicate, StructuralContext context)
        => context.Graph.Add(new CteDefinition.TableExistsPredicate(table, predicate));

    private static CteRef VisibleSinceFilterRef(DateTimeOffset since, StructuralContext context)
        => context.Graph.Add(new CteDefinition.VisibleSinceFilter(new SqlParameterRef(since.DateTime)));

    private static CteRef ReferencedTypeExpansionRef(CteRef seed, IReadOnlyList<short> outputResourceTypeIds, StructuralContext context)
        => context.Graph.Add(new CteDefinition.ReferencedTypeExpansion(seed, outputResourceTypeIds));
}
