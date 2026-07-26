using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class PatientEverythingLoweringTests
{
    private const short PatientTypeId = 103;
    private const short ObservationTypeId = 104;
    private const short EncounterTypeId = 105;
    private const short PractitionerTypeId = 201;
    private const short OrganizationTypeId = 202;
    private const short LocationTypeId = 203;
    private const short MedicationTypeId = 204;
    private const short SubjectParamId = 77;
    private const short StatusParamId = 220;

    private static readonly SearchParameterInfo SubjectParam = new(
        "subject",
        "subject",
        SearchParamType.Reference,
        new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));

    private static readonly SearchParameterInfo StatusParam = new(
        "status",
        "status",
        SearchParamType.Token,
        new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));

    /// <summary>
    /// Builds a symbol table whose Patient compartment reaches its member types through the "subject"
    /// reference parameter, mirroring what Resolve produces from an ICompartmentDefinitionManager for an
    /// ordinary compartment search. An Observation.status token parameter is registered so an
    /// Observation access constraint can be lowered, and the four referenced resource types are registered
    /// because <see cref="SymbolCollectingVisitor.VisitPatientEverything"/> collects them whenever
    /// referenced-resource expansion is on.
    /// </summary>
    private static SymbolTable BuildSymbols(IReadOnlyList<string>? memberTypes = null)
    {
        var membership = new Dictionary<string, IReadOnlyList<(SearchParameterInfo Parameter, IReadOnlyList<string> ResourceTypes)>>
        {
            ["Patient"] = new List<(SearchParameterInfo, IReadOnlyList<string>)>
            {
                (SubjectParam, memberTypes ?? ["Observation"]),
            },
        };

        return new SymbolTable(
            new Dictionary<string, short>
            {
                [SubjectParam.Url!.ToString()] = SubjectParamId,
                [StatusParam.Url!.ToString()] = StatusParamId,
            },
            new Dictionary<string, short>
            {
                ["Patient"] = PatientTypeId,
                ["Observation"] = ObservationTypeId,
                ["Encounter"] = EncounterTypeId,
                ["Practitioner"] = PractitionerTypeId,
                ["Organization"] = OrganizationTypeId,
                ["Location"] = LocationTypeId,
                ["Medication"] = MedicationTypeId,
            },
            compartmentMembership: membership);
    }

    private static QueryPlan Lowered(PatientEverythingExpression expression, SymbolTable symbols, LowerOptions? options = null)
        => Lower.Run(
            expression,
            symbols,
            "Patient",
            includes: [],
            revIncludes: [],
            includeLimit: 100,
            sort: [],
            SortPhase.Valued,
            page: null,
            options).Plan;

    [Fact]
    public void GivenAPatientEverythingSearch_WhenLowered_ThenTheCompartmentIsTraversedNotJustThePatient()
    {
        var symbols = BuildSymbols();

        var expression = new PatientEverythingExpression("pat-1");

        var plan = Lowered(expression, symbols);

        var sql = SqlBuilder.Run(plan).Sql;

        // The compartment traversal is the whole point of $everything; a plan that reads only
        // dbo.Resource has silently dropped it.
        sql.ShouldContain("dbo.ReferenceSearchParam");
        plan.Ctes.Count.ShouldBeGreaterThan(1);
    }

    [Fact]
    public void GivenAPatientEverythingSearchIncludingReferencedResources_WhenLowered_ThenTheReferencedTypesAreExpandedFromTheCompartmentSet()
    {
        var symbols = BuildSymbols();
        var expression = new PatientEverythingExpression("pat-1", includeReferencedResources: true);

        var plan = Lowered(expression, symbols);
        var sql = SqlBuilder.Run(plan).Sql;

        plan.Explain().ShouldContain("ReferencedTypeExpansion(");
        sql.ShouldContain("rsp.ReferenceResourceTypeId = 201 OR rsp.ReferenceResourceTypeId = 202 OR rsp.ReferenceResourceTypeId = 203 OR rsp.ReferenceResourceTypeId = 204");
    }

    [Fact]
    public void GivenAPatientEverythingSearchWithoutReferencedResources_WhenLowered_ThenNoExpansionIsAdded()
    {
        var symbols = BuildSymbols();
        var expression = new PatientEverythingExpression("pat-1", includeReferencedResources: false);

        var plan = Lowered(expression, symbols);

        plan.Ctes.OfType<CteDefinition.ReferencedTypeExpansion>().ShouldBeEmpty();
    }

    [Fact]
    public void GivenAPatientEverythingSearchWithSinceAndReferencedResources_WhenLowered_ThenTheExpansionSeedsFromTheFilteredCompartmentSet()
    {
        // Legacy runs its referenced-resource expansion after the _since/date narrowing, so the expansion
        // must follow the filtered compartment set. Seeding from the raw compartment union instead would
        // return referenced resources reachable only from members _since had already excluded.
        var symbols = BuildSymbols();
        var expression = new PatientEverythingExpression(
            "pat-1",
            sinceDate: new DateTimeOffset(2021, 6, 1, 0, 0, 0, TimeSpan.Zero),
            includeReferencedResources: true);

        var plan = Lowered(expression, symbols);

        var expansion = plan.Ctes.OfType<CteDefinition.ReferencedTypeExpansion>().ShouldHaveSingleItem();
        plan.Ctes[expansion.Seed.Index].ShouldBeOfType<CteDefinition.Intersect>();
    }

    [Fact]
    public void GivenAPatientEverythingSearchWithAClinicalDateRange_WhenLowered_ThenMembersWithNoDateRowSurviveAlongsideMatchingOnes()
    {
        var symbols = BuildSymbols();
        var expression = new PatientEverythingExpression(
            "pat-1",
            startDate: new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero),
            endDate: new DateTimeOffset(2023, 12, 31, 0, 0, 0, TimeSpan.Zero),
            includeReferencedResources: false);

        var plan = Lowered(expression, symbols);
        var sql = SqlBuilder.Run(plan).Sql;

        plan.Explain().ShouldContain("TableExistsPredicate[DateTimeSearchParam]");
        sql.ShouldContain("dbo.DateTimeSearchParam");
        sql.ShouldContain("EndDateTime >=");
        sql.ShouldContain("StartDateTime <=");
        plan.Ctes.OfType<CteDefinition.Except>().ShouldNotBeEmpty();
    }

    [Fact]
    public void GivenAPatientEverythingSearchWithNoDateRange_WhenLowered_ThenNoDatePredicateIsComposed()
    {
        var symbols = BuildSymbols();
        var expression = new PatientEverythingExpression("pat-1", includeReferencedResources: false);

        var plan = Lowered(expression, symbols);

        plan.Ctes.OfType<CteDefinition.TableExistsPredicate>().ShouldBeEmpty();
        plan.Ctes.OfType<CteDefinition.Except>().ShouldBeEmpty();
    }

    [Fact]
    public void GivenAPatientEverythingSearchWithSince_WhenLowered_ThenTheCompartmentBranchIsFilteredByTransactionVisibility()
    {
        var symbols = BuildSymbols();
        var expression = new PatientEverythingExpression(
            "pat-1",
            sinceDate: new DateTimeOffset(2021, 6, 1, 0, 0, 0, TimeSpan.Zero),
            includeReferencedResources: false);

        var plan = Lowered(expression, symbols);
        var sql = SqlBuilder.Run(plan).Sql;

        plan.Explain().ShouldContain("VisibleSinceFilter(");
        sql.ShouldContain("dbo.Transactions");
        sql.ShouldContain("t.VisibleDate >=");
    }

    [Fact]
    public void GivenAPatientEverythingSearchWithSince_WhenLowered_ThenThePatientRowItselfIsNotFilteredBySince()
    {
        // The compartment root is always returned: legacy's own captured SQL carries no _since bound on the
        // seed patient row, and narrowing away the resource the operation is named for would be surprising.
        var symbols = BuildSymbols();
        var expression = new PatientEverythingExpression(
            "pat-1",
            sinceDate: new DateTimeOffset(2021, 6, 1, 0, 0, 0, TimeSpan.Zero),
            includeReferencedResources: false);

        var plan = Lowered(expression, symbols);

        var root = plan.Ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.Union>();
        var patientBranch = Reachable(root.Parts[0].Index, plan);
        var compartmentBranch = Reachable(root.Parts[1].Index, plan);

        patientBranch.Select(i => plan.Ctes[i]).OfType<CteDefinition.VisibleSinceFilter>().ShouldBeEmpty();
        compartmentBranch.Select(i => plan.Ctes[i]).OfType<CteDefinition.VisibleSinceFilter>().ShouldNotBeEmpty();
    }

    [Fact]
    public void GivenARelaxedVisibility_WhenAPatientEverythingSearchIsEmitted_ThenEveryEverythingCteDropsTheCurrentRowFilter()
    {
        // $everything is not run with relaxed visibility in production today, so nothing else would catch a
        // CTE kind that ignores the plan's visibility input and hardcodes its own IsHistory/IsDeleted
        // filter. This asserts the three $everything-only CTE kinds honour the contract the rest of the
        // emitters do.
        var symbols = BuildSymbols();
        var expression = new PatientEverythingExpression(
            "pat-1",
            startDate: new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero),
            sinceDate: new DateTimeOffset(2021, 6, 1, 0, 0, 0, TimeSpan.Zero),
            includeReferencedResources: true);

        var relaxed = Lowered(
            expression,
            symbols,
            new LowerOptions { Visibility = new ResourceVisibility(IncludeHistory: true, IncludeDeleted: true) });
        var current = Lowered(expression, symbols);

        SqlBuilder.Run(relaxed).Sql.ShouldNotContain("IsHistory");
        SqlBuilder.Run(relaxed).Sql.ShouldNotContain("IsDeleted");
        SqlBuilder.Run(current).Sql.ShouldContain("r.IsHistory = 0 AND r.IsDeleted = 0");
    }

    [Fact]
    public void GivenAFullyFeaturedPatientEverythingPlan_WhenExplained_ThenEveryParameterOrdinalMatchesTheEmittedSql()
    {
        // The three $everything CTE kinds bind parameters (the date range and the _since instant) or bind
        // none (the expansion's type ids are literals). PlanExplainer keeps its own ordinal counter, so a
        // kind that consumes the wrong number of ordinals silently renumbers every later @pN in the
        // explained plan relative to the SQL. Asserting the two agree is what catches that.
        var symbols = BuildSymbols();
        var expression = new PatientEverythingExpression(
            "pat-1",
            startDate: new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero),
            endDate: new DateTimeOffset(2023, 12, 31, 0, 0, 0, TimeSpan.Zero),
            sinceDate: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            includeReferencedResources: true);

        var plan = Lowered(expression, symbols);
        var emitted = SqlBuilder.Run(plan);
        var explained = plan.Explain();

        plan.Explain().ShouldBe(
            "cte0 = ResourceSource[103] WHERE ResourceId = @p1\n" +
            "cte1 = CompartmentSource[104,77]  ReferenceResourceTypeId = @p2 AND ReferenceResourceId = @p3\n" +
            "cte2 = Union(cte1)\n" +
            "cte3 = TableExistsPredicate[DateTimeSearchParam]  EndDateTime >= @p4 AND StartDateTime <= @p5\n" +
            "cte4 = TableExistsPredicate[DateTimeSearchParam]\n" +
            "cte5 = Intersect(cte2, cte3)\n" +
            "cte6 = Except(cte2, cte4)\n" +
            "cte7 = Union(cte5, cte6)\n" +
            "cte8 = VisibleSinceFilter(@p6)\n" +
            "cte9 = Intersect(cte7, cte8)\n" +
            "cte10 = ReferencedTypeExpansion(cte9, output=[201,202,203,204])\n" +
            "root = Union(cte0, cte9, cte10)");

        // Seven bound values reach the SQL: the Patient type id and its ResourceId, the compartment's
        // reference type and id, the two date bounds, and the _since instant. The second
        // TableExistsPredicate carries no predicate and binds nothing, and the expansion's type ids are
        // literals. @p0 is the ResourceSource type id, which PlanExplainer counts but renders inline as
        // [103] rather than as @p0, so the highest printed ordinal is the assertion available here.
        emitted.Parameters.Count.ShouldBe(7);
        explained.ShouldContain("@p6");
        explained.ShouldNotContain("@p7");
    }

    [Fact]
    public void GivenAPatientEverythingSearchWithTypeFilter_WhenLowered_ThenOnlyTheRequestedMemberTypesAreTraversed()
    {
        // Compartment membership covers both Observation and Encounter; _type=Encounter must narrow the
        // member scan to Encounter alone.
        var symbols = BuildSymbols(["Observation", "Encounter"]);
        var expression = new PatientEverythingExpression("pat-1", filteredResourceTypes: new HashSet<string> { "Encounter" });

        var plan = Lowered(expression, symbols);

        var compartmentSource = plan.Ctes.OfType<CteDefinition.CompartmentSource>().ShouldHaveSingleItem();
        compartmentSource.ResourceTypeIds.ShouldBe([EncounterTypeId]);
        compartmentSource.ResourceTypeIds.ShouldNotContain(ObservationTypeId);
    }

    [Fact]
    public void GivenAConstrainedMemberType_WhenReachedThroughEverything_ThenTheConstraintStillNarrowsThatType()
    {
        // Arrange -- Observation is a compartment member and is access-constrained to status=final. A
        // single-type Apply against the "Patient" target would intersect the whole union down to Patient
        // rows, both dropping every Observation member and never enforcing its constraint. ApplyToTypes must
        // narrow Observation in place while leaving the Patient row and other members untouched.
        var symbols = BuildSymbols(["Observation"]);
        var constraint = new AccessConstraint(
            "Observation",
            new SearchParameterExpression(
                StatusParam,
                new SearchParameterPredicateExpression(StatusParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "final", text: null))));

        var expression = new PatientEverythingExpression("pat-1");

        // Act
        var plan = Lowered(expression, symbols, new LowerOptions { AccessConstraints = [constraint] });
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert -- assert the ApplyToTypes subtract-then-union wiring on the CTE graph, not merely the
        // presence of the constraint CTE (which survives even a plan that narrows nothing). The match root
        // must be (base MINUS Observation) UNION (base INTERSECT constraint) so that reverting the
        // $everything -> ApplyToTypes special case (which would leave the root as the bare patient+members
        // union) fails here.
        var ctes = plan.Ctes;
        var root = ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.Union>();
        root.Parts.Count.ShouldBe(2);

        var subtract = root.Parts.Select(p => ctes[p.Index]).OfType<CteDefinition.Except>().ShouldHaveSingleItem();
        var admitted = root.Parts.Select(p => ctes[p.Index]).OfType<CteDefinition.Intersect>().ShouldHaveSingleItem();

        ctes[subtract.Right.Index].ShouldBeOfType<CteDefinition.ResourceSource>().ResourceTypeId.ShouldBe(ObservationTypeId);
        ctes[admitted.Right.Index].ShouldBeOfType<CteDefinition.ParamSource>().SearchParamId.ShouldBe(StatusParamId);
        subtract.Left.ShouldBe(admitted.Left);

        sql.ShouldContain("SearchParamId = 220");
    }

    [Fact]
    public void GivenAPatientEverythingSearchWithAnUnknownTypeFilter_WhenLowered_ThenTheCompartmentMatchesNothingRatherThanThrowing()
    {
        // _type=foo names a type that is not a member of the Patient compartment, so the membership set
        // narrows to zero groups. Per ISymbolResolver's "not found is data, not an error" convention this
        // must lower to an empty match (a Predicate.False), the same way an unresolvable token system or
        // resource type does -- not throw a NotSupportedException the caller would surface as a 500.
        // Referenced-resource expansion is off so the only dbo.ReferenceSearchParam read this assertion
        // could see is the compartment traversal that must not be emitted.
        var symbols = BuildSymbols(["Observation"]);
        var expression = new PatientEverythingExpression(
            "pat-1",
            filteredResourceTypes: new HashSet<string> { "foo" },
            includeReferencedResources: false);

        var plan = Lowered(expression, symbols);
        var sql = SqlBuilder.Run(plan).Sql;

        // The compartment member scan collapsed to an unsatisfiable predicate: no ReferenceSearchParam is
        // read, and the plan carries the false predicate that emits `1 = 0` as valid SQL.
        plan.Ctes.OfType<CteDefinition.CompartmentSource>().ShouldBeEmpty();
        sql.ShouldNotContain("dbo.ReferenceSearchParam");
        sql.ShouldContain("1 = 0");

        var miss = plan.Ctes
            .OfType<CteDefinition.ResourceSource>()
            .Select(rs => rs.Predicate)
            .OfType<Predicate.False>()
            .ShouldHaveSingleItem();
        miss.Reason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GivenAPatientEverythingSearchWithAMixOfKnownAndUnknownTypes_WhenLowered_ThenOnlyTheKnownTypesAreTraversed()
    {
        // _type=Encounter,foo: Encounter is a compartment member, foo is not. The unknown type must drop
        // out while the known one is still traversed -- narrowing to zero is a per-type decision, not an
        // all-or-nothing throw.
        var symbols = BuildSymbols(["Observation", "Encounter"]);
        var expression = new PatientEverythingExpression(
            "pat-1",
            filteredResourceTypes: new HashSet<string> { "Encounter", "foo" });

        var plan = Lowered(expression, symbols);
        var sql = SqlBuilder.Run(plan).Sql;

        var compartmentSource = plan.Ctes.OfType<CteDefinition.CompartmentSource>().ShouldHaveSingleItem();
        compartmentSource.ResourceTypeIds.ShouldBe([EncounterTypeId]);
        compartmentSource.ResourceTypeIds.ShouldNotContain(ObservationTypeId);

        // The known type still produces a real compartment traversal, and the unknown one added no
        // unsatisfiable branch.
        sql.ShouldContain("dbo.ReferenceSearchParam");
        sql.ShouldNotContain("1 = 0");
    }

    [Fact]
    public void GivenAGroupEverythingSearchOverSeveralPatients_WhenLowered_ThenEachPatientRowAndCompartmentIsCovered()
    {
        var symbols = BuildSymbols();
        var patientIds = new List<string> { "pat-1", "pat-2" };
        var expression = new PatientEverythingExpression(patientIds, includeReferencedResources: false);

        var plan = Lowered(expression, symbols);

        // One Patient-itself ResourceSource covering both ids as an Or, and one CompartmentSource per
        // patient (the compartment mechanism is per compartment id).
        var patientItself = plan.Ctes.OfType<CteDefinition.ResourceSource>().ShouldHaveSingleItem();
        patientItself.Predicate.ShouldBeOfType<Predicate.Or>();
        plan.Ctes.OfType<CteDefinition.CompartmentSource>().Count().ShouldBe(2);
    }

    /// <summary>The CTE indexes reachable from <paramref name="index"/>, itself included.</summary>
    private static IReadOnlyList<int> Reachable(int index, QueryPlan plan)
    {
        var seen = new HashSet<int>();
        var pending = new Stack<int>();
        pending.Push(index);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current))
            {
                continue;
            }

            foreach (var child in PlanExplainer.ReferencedCteIndexesOf(plan.Ctes[current]))
            {
                pending.Push(child);
            }
        }

        return [.. seen];
    }
}
