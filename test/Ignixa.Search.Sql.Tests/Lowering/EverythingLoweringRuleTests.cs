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

public class EverythingLoweringRuleTests
{
    private const short PatientTypeId = 103;
    private const short ObservationTypeId = 104;
    private const short EncounterTypeId = 105;
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
    /// Observation access constraint can be lowered.
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
            new Dictionary<string, short> { ["Patient"] = PatientTypeId, ["Observation"] = ObservationTypeId, ["Encounter"] = EncounterTypeId },
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
    public void GivenAPatientEverythingSearchWithSince_WhenLowered_ThenTheCompartmentMembersAreBoundedByLastUpdated()
    {
        var symbols = BuildSymbols();

        // _since is a lower bound on meta.lastUpdated; it renders as a ResourceSurrogateId floor on the
        // member rows of dbo.ReferenceSearchParam. Without the bound the column appears only as the
        // selected `ResourceSurrogateId AS Sid1`, never in a `>=` comparison, so the comparison is the
        // discriminating assertion.
        var since = new DateTimeOffset(2021, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var expression = new PatientEverythingExpression("pat-1", sinceDate: since);

        var plan = Lowered(expression, symbols);
        var sql = SqlBuilder.Run(plan).Sql;

        sql.ShouldContain("ResourceSurrogateId >=");

        var compartmentSource = plan.Ctes.OfType<CteDefinition.CompartmentSource>().ShouldHaveSingleItem();
        compartmentSource.Predicate.ShouldBeOfType<Predicate.And>();
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
}

