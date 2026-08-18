using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Search.Sql.Tests.TestSupport;
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

    private static QueryPlan Lowered(
        PatientEverythingExpression expression,
        SymbolTable symbols,
        LowerOptions? options = null,
        PageSpec? page = null)
        => LowerHarness.Run(
            expression,
            symbols,
            "Patient",
            includes: [],
            revIncludes: [],
            includeLimit: 100,
            sort: [],
            SortPhase.Valued,
            page,
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
    public void GivenTheExpansionIsNotRequested_WhenLowered_ThenTheExpansionTypesAreNeverResolved()
    {
        // BuildSymbols above registers all four expansion types, which is what every other test here needs
        // and exactly why this case escaped until it was executed. SymbolCollectingVisitor.VisitPatientEverything
        // only collects those types when IncludeReferencedResources is set, so the real symbol table for a
        // non-expanding $everything does NOT contain them -- reproduced here by omitting them. Lowering must
        // not resolve what the collector did not collect: doing so threw RequestNotValidException for
        // $everything?_type=X, which is the request shape that turns the flag off.
        var symbols = new SymbolTable(
            new Dictionary<string, short>
            {
                [SubjectParam.Url!.ToString()] = SubjectParamId,
                [StatusParam.Url!.ToString()] = StatusParamId,
            },
            new Dictionary<string, short>
            {
                ["Patient"] = PatientTypeId,
                ["Observation"] = ObservationTypeId,
            },
            compartmentMembership: new Dictionary<string, IReadOnlyList<(SearchParameterInfo Parameter, IReadOnlyList<string> ResourceTypes)>>
            {
                ["Patient"] = new List<(SearchParameterInfo, IReadOnlyList<string>)>
                {
                    (SubjectParam, ["Observation"]),
                },
            });

        var expression = new PatientEverythingExpression("pat-1", includeReferencedResources: false);

        var plan = Should.NotThrow(() => Lowered(expression, symbols));

        plan.Explain().ShouldNotContain("ReferencedTypeExpansion(");
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
    public void GivenAPatientEverythingSearchIncludingReferencedResources_WhenLowered_ThenTheExpansionSeedIncludesThePatientItself()
    {
        // The patient is not a member of its own compartment -- no ReferenceSearchParam row points from the
        // patient at itself -- so a generalPractitioner/managingOrganization reachable only from the patient
        // row (no compartment member happens to reference the same target) is missed unless the expansion's
        // seed set includes the patient-itself branch alongside the compartment branch. This isolates that:
        // the only member type registered is Observation, which never carries a generalPractitioner or
        // managingOrganization reference, so the compartment branch alone can never reach those two targets.
        var symbols = BuildSymbols();
        var expression = new PatientEverythingExpression("pat-1", includeReferencedResources: true);

        var plan = Lowered(expression, symbols);

        var expansion = plan.Ctes.OfType<CteDefinition.ReferencedTypeExpansion>().ShouldHaveSingleItem();
        var seedReachable = Reachable(expansion.Seed.Index, plan);

        var patientItselfIndex = plan.Ctes
            .Select((cte, index) => (cte, index))
            .Where(t => t.cte is CteDefinition.ResourceSource rs && rs.ResourceTypeId == PatientTypeId)
            .Select(t => t.index)
            .ShouldHaveSingleItem();

        seedReachable.ShouldContain(patientItselfIndex);
    }

    [Fact]
    public void GivenAPatientEverythingSearchWithSinceAndReferencedResources_WhenLowered_ThenTheExpansionSeedsFromThePatientItselfAndTheFilteredCompartmentSet()
    {
        // Legacy runs its referenced-resource expansion after the _since/date narrowing, so the compartment
        // half of the seed must follow the filtered compartment set -- seeding from the raw compartment
        // union instead would return referenced resources reachable only from members _since had already
        // excluded. The patient-itself branch is never touched by _since (asserted elsewhere), so it joins
        // the seed unfiltered, alongside the filtered compartment set.
        var symbols = BuildSymbols();
        var expression = new PatientEverythingExpression(
            "pat-1",
            sinceDate: new DateTimeOffset(2021, 6, 1, 0, 0, 0, TimeSpan.Zero),
            includeReferencedResources: true);

        var plan = Lowered(expression, symbols);

        var expansion = plan.Ctes.OfType<CteDefinition.ReferencedTypeExpansion>().ShouldHaveSingleItem();
        var seed = plan.Ctes[expansion.Seed.Index].ShouldBeOfType<CteDefinition.Union>();
        seed.Parts.Count.ShouldBe(2);

        var patientItselfIndex = plan.Ctes
            .Select((cte, index) => (cte, index))
            .Where(t => t.cte is CteDefinition.ResourceSource rs && rs.ResourceTypeId == PatientTypeId)
            .Select(t => t.index)
            .ShouldHaveSingleItem();

        seed.Parts.ShouldContain(new CteRef(patientItselfIndex));
        var compartmentHalf = seed.Parts.Single(p => p.Index != patientItselfIndex);
        plan.Ctes[compartmentHalf.Index].ShouldBeOfType<CteDefinition.Intersect>();
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
        // filter, or one that silently omits the filter it owes under the default visibility. Asserted per
        // CTE body rather than across the whole statement: a whole-statement Contains would pass as long as
        // any one of the three kinds carries the filter, even if another kind of the three emits none at
        // all -- exactly the shape a revert of visibility threading on a single emitter would produce.
        var symbols = BuildSymbols();
        var expression = new PatientEverythingExpression(
            "pat-1",
            startDate: new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero),
            sinceDate: new DateTimeOffset(2021, 6, 1, 0, 0, 0, TimeSpan.Zero),
            includeReferencedResources: true);

        var relaxed = Lowered(
            expression,
            symbols,
            new LowerOptions { Visibility = new ResourceVisibility(IsHistory: null, IsDeleted: null) });
        var current = Lowered(expression, symbols);

        var relaxedSql = SqlBuilder.Run(relaxed).Sql;
        relaxedSql.ShouldNotContain("IsHistory");
        relaxedSql.ShouldNotContain("IsDeleted");

        var currentSql = SqlBuilder.Run(current).Sql;

        var referencedTypeExpansionCtes = CteIndexesOf<CteDefinition.ReferencedTypeExpansion>(current);
        var visibleSinceFilterCtes = CteIndexesOf<CteDefinition.VisibleSinceFilter>(current);
        var tableExistsPredicateCtes = CteIndexesOf<CteDefinition.TableExistsPredicate>(current);

        referencedTypeExpansionCtes.ShouldNotBeEmpty();
        visibleSinceFilterCtes.ShouldNotBeEmpty();
        tableExistsPredicateCtes.ShouldNotBeEmpty();

        foreach (var index in referencedTypeExpansionCtes.Concat(visibleSinceFilterCtes))
        {
            CteBody(currentSql, index).ShouldContain("r.IsHistory = 0 AND r.IsDeleted = 0");
        }

        // TableExistsPredicate scans dbo.DateTimeSearchParam, which has neither IsHistory nor IsDeleted, so
        // its catalog-driven SearchParamTableHistoryClause correctly renders empty for this table -- not
        // ResourceRowFilter, which would demand a column this table doesn't have.
        foreach (var index in tableExistsPredicateCtes)
        {
            CteBody(currentSql, index).ShouldNotContain("IsDeleted");
        }
    }

    [Fact]
    public void GivenAFullyFeaturedPatientEverythingPlan_WhenExplained_ThenEveryParameterOrdinalMatchesTheEmittedSql()
    {
        // The three $everything CTE kinds bind parameters (the date range and the _since instant) or bind
        // none (the expansion's type ids are literals). PlanExplainer names parameters through
        // EmittedParameterCursor, which fails on the first row whose expected value disagrees with the one
        // emission bound, so this golden pins WHICH ordinal each kind takes rather than merely that the
        // counts line up.
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
            "cte0 = ResourceSource[103] WHERE ResourceId = @p0\n" +
            "cte1 = CompartmentSource[104,77]  ReferenceResourceTypeId = @p2 AND ReferenceResourceId = @p3\n" +
            "cte2 = Union(cte1)\n" +
            "cte3 = TableExistsPredicate[DateTimeSearchParam]  EndDateTime >= @p4 AND StartDateTime <= @p5\n" +
            "cte4 = TableExistsPredicate[DateTimeSearchParam]\n" +
            "cte5 = Intersect(cte2, cte3)\n" +
            "cte6 = Except(cte2, cte4)\n" +
            "cte7 = Union(cte5, cte6)\n" +
            "cte8 = VisibleSinceFilter(@p6)\n" +
            "cte9 = Intersect(cte7, cte8)\n" +
            "cte10 = Union(cte0, cte9)\n" +
            "cte11 = ReferencedTypeExpansion(cte10, output=[201,202,203,204])\n" +
            "root = Union(cte0, cte9, cte11)");

        // Seven bound values reach the SQL: the Patient type id and its ResourceId, the compartment's
        // reference type and id, the two date bounds, and the _since instant. The second
        // TableExistsPredicate carries no predicate and binds nothing, cte10's Union of the patient-itself
        // and filtered-compartment branches binds nothing of its own, and the expansion's type ids are
        // literals. The ResourceSource binds its predicate BEFORE its type id, so @p0 is the ResourceId and
        // @p1 is the type id -- which the plan renders inline as [103] rather than as a parameter, so the
        // highest printed ordinal is the assertion available here.
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
    public void GivenAPatientEverythingSearchWhoseTypeFilterExcludesEveryReferencedType_WhenLowered_ThenNoExpansionIsEmitted()
    {
        // Arrange -- $everything?_type=Encounter with referenced-resource expansion still switched on. The
        // expansion's output set is fixed at Practitioner/Organization/Location/Medication, so before the
        // intersection it emitted all four regardless of _type: the compartment branch honoured the filter
        // and the expansion branch quietly did not.
        var symbols = BuildSymbols(["Observation", "Encounter"]);
        var expression = new PatientEverythingExpression(
            "pat-1",
            filteredResourceTypes: new HashSet<string> { "Encounter" },
            includeReferencedResources: true);

        // Act
        var plan = Lowered(expression, symbols);
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert -- the intersection is empty, so the expansion is dropped rather than emitted with an
        // empty type-in filter (which would match every referenced type, the inverse of what was asked).
        plan.Ctes.OfType<CteDefinition.ReferencedTypeExpansion>().ShouldBeEmpty();
        plan.Explain().ShouldNotContain("ReferencedTypeExpansion(");
        sql.ShouldNotContain($"rsp.ReferenceResourceTypeId = {PractitionerTypeId}");
    }

    [Fact]
    public void GivenAPatientEverythingSearchWhoseTypeFilterNamesOneReferencedType_WhenLowered_ThenTheExpansionOutputsOnlyThatType()
    {
        // Arrange -- _type=Practitioner names a type the Patient compartment cannot reach (its only member
        // here is Observation) but the expansion can. The expansion must survive, narrowed to that one
        // type: dropping it whenever _type is present would make this request return nothing but the
        // patient row, and emitting all four would return Organizations that were excluded.
        var symbols = BuildSymbols(["Observation"]);
        var expression = new PatientEverythingExpression(
            "pat-1",
            filteredResourceTypes: new HashSet<string> { "Practitioner" },
            includeReferencedResources: true);

        // Act
        var plan = Lowered(expression, symbols);
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert
        var expansion = plan.Ctes.OfType<CteDefinition.ReferencedTypeExpansion>().ShouldHaveSingleItem();
        expansion.OutputResourceTypeIds.ShouldBe([PractitionerTypeId]);
        sql.ShouldContain($"rsp.ReferenceResourceTypeId = {PractitionerTypeId}");
        sql.ShouldNotContain($"rsp.ReferenceResourceTypeId = {OrganizationTypeId}");
    }

    [Fact]
    public void GivenAPatientEverythingSearchWithNoTypeFilter_WhenLowered_ThenTheExpansionStillOutputsAllFourReferencedTypes()
    {
        // The intersection must be a no-op on the unfiltered request -- an empty _type set means "every
        // type", not "no type", and reading it as the latter would silently delete the expansion from the
        // ordinary $everything call.
        var symbols = BuildSymbols();
        var expression = new PatientEverythingExpression("pat-1", includeReferencedResources: true);

        var plan = Lowered(expression, symbols);

        var expansion = plan.Ctes.OfType<CteDefinition.ReferencedTypeExpansion>().ShouldHaveSingleItem();
        expansion.OutputResourceTypeIds.ShouldBe([PractitionerTypeId, OrganizationTypeId, LocationTypeId, MedicationTypeId]);
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
    public void GivenAConstrainedReferencedType_WhenReachedThroughEverythingsExpansion_ThenTheConstraintStillNarrowsThatType()
    {
        // Arrange -- Practitioner is reachable only through the referenced-type expansion; it is not a
        // compartment member (the only member type registered is Observation), so this exercises a
        // different row-producing stage than GivenAConstrainedMemberType above. ApplyToTypes wraps the
        // whole $everything match set -- patient-itself, filtered compartment, and the expansion output
        // together -- so a constraint on an expansion-only type must narrow that stage in place too, or a
        // constrained Practitioner reached via generalPractitioner/managingOrganization would leak through
        // unfiltered.
        var symbols = BuildSymbols(["Observation"]);
        var constraint = new AccessConstraint(
            "Practitioner",
            new SearchParameterExpression(
                StatusParam,
                new SearchParameterPredicateExpression(StatusParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "final", text: null))));

        var expression = new PatientEverythingExpression("pat-1", includeReferencedResources: true);

        // Act
        var plan = Lowered(expression, symbols, new LowerOptions { AccessConstraints = [constraint] });
        var sql = SqlBuilder.Run(plan).Sql;

        // Assert -- same subtract-then-union wiring as the compartment-member case: the match root is
        // (base MINUS Practitioner) UNION (base INTERSECT constraint).
        var ctes = plan.Ctes;
        var root = ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.Union>();
        root.Parts.Count.ShouldBe(2);

        var subtract = root.Parts.Select(p => ctes[p.Index]).OfType<CteDefinition.Except>().ShouldHaveSingleItem();
        var admitted = root.Parts.Select(p => ctes[p.Index]).OfType<CteDefinition.Intersect>().ShouldHaveSingleItem();

        ctes[subtract.Right.Index].ShouldBeOfType<CteDefinition.ResourceSource>().ResourceTypeId.ShouldBe(PractitionerTypeId);
        subtract.Left.ShouldBe(admitted.Left);

        // The narrowed base must still reach the ReferencedTypeExpansion CTE -- proving the constraint
        // wraps the expansion's output rather than only the patient-itself and compartment branches
        // beneath it. A wiring that applied ApplyToTypes before adding the expansion branch (or skipped it
        // entirely) would fail this while still passing the assertions above.
        var narrowedReachable = Reachable(subtract.Left.Index, plan);
        var expansionIndex = plan.Ctes
            .Select((cte, index) => (cte, index))
            .Where(t => t.cte is CteDefinition.ReferencedTypeExpansion)
            .Select(t => t.index)
            .ShouldHaveSingleItem();
        narrowedReachable.ShouldContain(expansionIndex);

        sql.ShouldContain("SearchParamId = 220");
    }

    [Fact]
    public void GivenAConstrainedMemberType_WhenEverythingIsAndedWithAResourceColumnPredicate_ThenTheConstraintIsStillEnforced()
    {
        // Arrange -- `$everything AND _lastUpdated ge X`, the shape a caller gets from adding _lastUpdated
        // to the operation. ExtractResourceColumnPredicates peels _lastUpdated into the outer WHERE and
        // leaves the bare PatientEverythingExpression as the node the match set is lowered from, so the
        // match set is still the multi-type $everything union. The enforcement dispatch has to read that
        // residue: reading the original And instead sees a MultiaryExpression, falls through to the
        // single-type Apply for "Patient", finds Patient unconstrained, and emits no guard at all --
        // returning every Observation in the compartment to a caller restricted to status=final.
        var symbols = BuildSymbols(["Observation"]);
        var lastUpdated = new SearchParameterInfo("_lastUpdated", "_lastUpdated", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Resource-lastUpdated"));
        var constraint = new AccessConstraint(
            "Observation",
            new SearchParameterExpression(
                StatusParam,
                new SearchParameterPredicateExpression(StatusParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "final", text: null))));

        var expression = new MultiaryExpression(
            MultiaryOperator.And,
            [
                new PatientEverythingExpression("pat-1"),
                new SearchParameterExpression(
                    lastUpdated,
                    new SearchParameterPredicateExpression(
                        lastUpdated,
                        SearchComparator.Ge,
                        modifier: null,
                        new DateTimeSearchValue(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)))),
            ]);

        // Act
        var plan = LowerHarness.Run(
            expression, symbols, "Patient", includes: [], revIncludes: [], includeLimit: 100,
            sort: [], SortPhase.Valued, page: null,
            new LowerOptions { AccessConstraints = [constraint] }).Plan;

        // Assert -- the multi-type subtract-then-union wiring, same as the un-ANDed case. A single-type
        // Apply would leave the match root as the plain $everything union with no Except/Intersect at all.
        var ctes = plan.Ctes;
        var root = ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.Union>();
        var subtract = root.Parts.Select(p => ctes[p.Index]).OfType<CteDefinition.Except>().ShouldHaveSingleItem();
        var admitted = root.Parts.Select(p => ctes[p.Index]).OfType<CteDefinition.Intersect>().ShouldHaveSingleItem();

        ctes[subtract.Right.Index].ShouldBeOfType<CteDefinition.ResourceSource>().ResourceTypeId.ShouldBe(ObservationTypeId);
        subtract.Left.ShouldBe(admitted.Left);

        // Reachability, not text presence: SqlBuilder emits every CTE in plan.Ctes whether or not it is
        // joined to anything, so ShouldContain("SearchParamId = 220") passes even on a plan where the
        // constraint was lowered and then discarded.
        Reachable(plan.Match.Index, plan).ShouldContain(admitted.Right.Index);

        // The _lastUpdated conjunct still reached the outer WHERE -- the fix must not have swallowed it.
        plan.OuterPredicate.ShouldNotBeNull();
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

    [Fact]
    public void GivenAPatientEverythingSearchWithAKeysetPageBoundary_WhenLowered_ThenTheSeekAndOrderByWindowTheUnionAsAWhole()
    {
        // $everything's match set is a Union of CTEs, not a single ParamSource, so the question this
        // pins down is whether the existing keyset machinery composes over it at all. It does, and for a
        // structural reason: the outer SELECT reads the union's own output through the m alias, so the
        // seek predicate and ORDER BY constrain the union as one relation rather than any single arm.
        var symbols = BuildSymbols();
        var expression = new PatientEverythingExpression("pat-1");
        var page = new PageSpec([], new SqlParameterRef(PatientTypeId), new SqlParameterRef(5000L));

        var plan = Lowered(expression, symbols, page: page);
        var emitted = SqlBuilder.Run(plan);

        plan.Ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.Union>();
        emitted.Sql.ShouldContain($"FROM {SqlLabels.CteLabel(plan.Match.Index)} m");

        var typeParam = $"@p{emitted.Parameters.Count - 2}";
        var sidParam = $"@p{emitted.Parameters.Count - 1}";
        emitted.Sql.ShouldContain(
            $"WHERE ((m.T1 = {typeParam} AND m.Sid1 > {sidParam})\n" +
            $"       OR (m.T1 > {typeParam}))\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
    }

    [Fact]
    public void GivenAPatientEverythingSearchWithAKeysetPageBoundary_WhenEmitted_ThenTheCteParametersStillOccupyTheLeadingOrdinals()
    {
        // The page boundary is bound by the shape emitter, after EmitCteBlocks has bound every CTE value.
        // Reversing that -- hoisting the window ahead of the CTE prelude -- renumbers every @pN
        // PlanExplainer reads back positionally, which is silent in the SQL text and loud only here.
        var symbols = BuildSymbols();
        var expression = new PatientEverythingExpression("pat-1");
        var page = new PageSpec([], new SqlParameterRef(PatientTypeId), new SqlParameterRef(5000L));

        var unpaged = SqlBuilder.Run(Lowered(expression, symbols));
        var paged = SqlBuilder.Run(Lowered(expression, symbols, page: page));

        paged.Parameters.Take(unpaged.Parameters.Count).Select(p => p.Value)
            .ShouldBe(unpaged.Parameters.Select(p => p.Value));
        paged.Parameters.Skip(unpaged.Parameters.Count).Select(p => p.Value)
            .ShouldBe([PatientTypeId, 5000L]);
    }

    [Fact]
    public void GivenAPatientEverythingSearchWithAnOffsetWindow_WhenLowered_ThenOffsetFetchFollowsTheOrderByOverTheUnion()
    {
        var symbols = BuildSymbols();
        var expression = new PatientEverythingExpression("pat-1");

        var emitted = SqlBuilder.Run(Lowered(expression, symbols, new LowerOptions { OffsetPage = new OffsetSpec(20, 10) }));

        var offsetParam = $"@p{emitted.Parameters.Count - 2}";
        var limitParam = $"@p{emitted.Parameters.Count - 1}";
        emitted.Sql.ShouldContain(
            "ORDER BY m.T1 ASC, m.Sid1 ASC\n" +
            $"OFFSET {offsetParam} ROWS FETCH NEXT {limitParam} ROWS ONLY");
        emitted.Parameters.Skip(emitted.Parameters.Count - 2).Select(p => p.Value).ShouldBe([20, 10]);
    }

    [Fact]
    public void GivenAPatientEverythingSearchWithATopCap_WhenLowered_ThenTheCapBoundsTheUnionedResultAndNotAnyOneArm()
    {
        // A cap pushed down into the arms would return up to N patient rows AND N compartment members
        // AND N referenced resources -- silently more than the caller asked for, and a different set
        // each time the arms' relative sizes change. Exactly one TOP, on the union's output.
        var symbols = BuildSymbols();
        var expression = new PatientEverythingExpression("pat-1");

        var sql = SqlBuilder.Run(Lowered(expression, symbols, new LowerOptions { Top = 25 })).Sql;

        sql.ShouldContain($"SELECT TOP (25) m.T1, m.Sid1 FROM {SqlLabels.CteLabel(Lowered(expression, symbols).Match.Index)} m");
        (sql.Split("TOP (").Length - 1).ShouldBe(1);
    }

    [Fact]
    public void GivenAWindowedPatientEverythingSearch_WhenEmitted_ThenEveryUnionInTheMatchGraphDeduplicates()
    {
        // The invariant a single windowed query rests on: the arms can overlap (a Practitioner reachable
        // from both the patient row and a compartment member; a member reachable from two membership
        // parameters), and only a de-duplicating UNION makes (T1, Sid1) unique across them. Under UNION
        // ALL the (T1 ASC, Sid1 ASC) order is no longer total, so a page boundary landing on a duplicated
        // pair would repeat or skip resources between pages -- invisible to every other assertion here.
        var symbols = BuildSymbols();
        var expression = new PatientEverythingExpression("pat-1", includeReferencedResources: true);
        var page = new PageSpec([], new SqlParameterRef(PatientTypeId), new SqlParameterRef(5000L));

        var plan = Lowered(expression, symbols, page: page);
        var sql = SqlBuilder.Run(plan).Sql;

        plan.Ctes.OfType<CteDefinition.Union>().ShouldNotBeEmpty();
        sql.ShouldNotContain("UNION ALL");
    }

    /// <summary>The plan indexes of every CTE of kind <typeparamref name="T"/>.</summary>
    private static IReadOnlyList<int> CteIndexesOf<T>(QueryPlan plan) where T : CteDefinition
        => [.. plan.Ctes.Select((cte, index) => (cte, index)).Where(t => t.cte is T).Select(t => t.index)];

    /// <summary>
    /// The SQL body of the CTE at <paramref name="index"/> in <paramref name="sql"/>, delimited by the
    /// balanced parentheses following its <c>cteN AS (</c> header. Balances parens rather than searching
    /// for the next <c>)</c> because emitters like TableExistsPredicate's NOT EXISTS nest their own.
    /// </summary>
    private static string CteBody(string sql, int index)
    {
        var marker = $"{SqlLabels.CteLabel(index)} AS (";
        var start = sql.IndexOf(marker, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);

        var bodyStart = start + marker.Length;
        var depth = 1;
        var cursor = bodyStart;
        while (depth > 0)
        {
            if (sql[cursor] == '(')
            {
                depth++;
            }
            else if (sql[cursor] == ')')
            {
                depth--;
            }

            cursor++;
        }

        return sql[bodyStart..(cursor - 1)];
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
