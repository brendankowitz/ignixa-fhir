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

/// <summary>
/// Lowers the two <c>$includes</c> shapes from <see cref="SearchPlanOptions"/>: an
/// <see cref="ResultShape.IncludesCount"/> over the whole search, and an <see cref="ResultShape.IncludesPage"/>
/// that replays its match page. Proves the include stages carry the same authorization the includes page
/// applies, and that the options a caller carries over from an includes page do not bound the count.
/// </summary>
public class LowerIncludesCountAndReplayTests
{
    private const short PatientTypeId = 103;
    private const short OrganizationTypeId = 105;
    private const short ObservationTypeId = 104;
    private const short StatusParamId = 220;

    private static readonly SearchParameterInfo OrganizationParam = new(
        "organization", "organization", SearchParamType.Reference,
        new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"), targetResourceTypes: ["Organization"]);

    private static readonly SearchParameterInfo PartOfParam = new(
        "partof", "partof", SearchParamType.Reference,
        new Uri("http://hl7.org/fhir/SearchParameter/Organization-partof"), targetResourceTypes: ["Organization"]);

    private static readonly SearchParameterInfo SubjectParam = new(
        "subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));

    private static readonly SearchParameterInfo StatusParam = new(
        "status", "status", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));

    private static readonly SearchParameterInfo BirthDateParam = new(
        "birthdate", "birthdate", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Patient-birthdate"));

    private static readonly IncludeExpression IncludeOrganization = new(["Patient"], OrganizationParam, "Patient", "Organization", null, wildCard: false, reversed: false, iterate: false);

    private static readonly IncludeExpression IterateOrganizationPartOf = new(["Organization"], PartOfParam, "Organization", "Organization", null, wildCard: false, reversed: false, iterate: true);

    private static readonly IncludeExpression RevIncludeObservationSubject = new(["Observation"], SubjectParam, "Observation", "Patient", referencedTypes: null, wildCard: false, reversed: true, iterate: false);

    private static readonly IncludeExpression RevIncludeWildcard = new(["*"], referenceSearchParameter: null, "*", "Patient", referencedTypes: ["Observation"], wildCard: true, reversed: true, iterate: false);

    private static readonly SortExpression BirthDateDescending = new(BirthDateParam, SortOrder.Descending);

    private static readonly SymbolTable Symbols = new(
        new Dictionary<string, short>
        {
            [OrganizationParam.Url!.ToString()] = 55,
            [PartOfParam.Url!.ToString()] = 77,
            [SubjectParam.Url!.ToString()] = 230,
            [StatusParam.Url!.ToString()] = StatusParamId,
            [BirthDateParam.Url!.ToString()] = 203,
        },
        new Dictionary<string, short> { ["Patient"] = PatientTypeId, ["Organization"] = OrganizationTypeId, ["Observation"] = ObservationTypeId });

    private static readonly AccessConstraint ObservationFinalOnly = new(
        "Observation",
        new SearchParameterExpression(
            StatusParam,
            new SearchParameterPredicateExpression(StatusParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "final", text: null))));

    // ─── IncludesCount ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GivenAnIncludesCountCarryingTheIncludesPageWindowSortAndBudget_WhenLowered_ThenNoneOfThemBoundsTheMatchSeed()
    {
        // A caller counting the includes of a sorted, windowed, budgeted $includes page passes the same search:
        // the count must still cover the whole search.
        var context = CompilationContextFactory.For(
            expression: null,
            "Patient",
            includes: [IncludeOrganization],
            sort: [BirthDateDescending],
            surrogateRange: new SurrogateIdRange(new SqlParameterRef(1000L), new SqlParameterRef(2000L)),
            options: new SearchPlanOptions { Shape = new ResultShape.IncludesCount(), IncludeLimit = 5, SortPhase = SortPhase.MissingPrimary });

        var plan = Lower.Run(context, Symbols).Plan;
        var sql = SqlBuilder.Run(plan).Sql;

        plan.IncludesCountOnly.ShouldBeTrue();
        plan.Sort.ShouldBeNull();
        plan.SurrogateRange.ShouldBeNull();
        plan.Top.ShouldBeNull();
        plan.Page.ShouldBeNull();
        plan.Ctes[^1].ShouldBeOfType<CteDefinition.MatchPage>();
        plan.Ctes.ShouldNotContain(cte => cte is CteDefinition.MatchSeed);
        sql.ShouldNotContain("TOP");
        sql.ShouldNotContain("DateTimeSearchParam");
        sql.ShouldNotContain("m.Sid1 >=");
        sql.ShouldContain("SELECT COUNT_BIG(*) FROM (");
    }

    [Fact]
    public void GivenAnIncludesCountWithAnUnsortableSort_WhenLowered_ThenTheSortIsStillValidatedBeforeItIsDropped()
    {
        var context = CompilationContextFactory.For(
            expression: null,
            "Patient",
            includes: [IncludeOrganization],
            sort: [new SortExpression(new SearchParameterInfo("_lastUpdated", "_lastUpdated", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Resource-lastUpdated")), SortOrder.Ascending)],
            options: new SearchPlanOptions { Shape = new ResultShape.IncludesCount(), SortPhase = SortPhase.MissingPrimary });

        Should.Throw<NotSupportedException>(() => Lower.Run(context, Symbols));
    }

    [Fact]
    public void GivenAnIncludesCountWithNoIncludes_WhenLowered_ThenItIsRejected()
    {
        var context = CompilationContextFactory.For(
            expression: null, "Patient", options: new SearchPlanOptions { Shape = new ResultShape.IncludesCount() });

        Should.Throw<NotSupportedException>(() => Lower.Run(context, Symbols)).Message.ShouldContain("IncludesCount was requested");
    }

    [Fact]
    public void GivenAnIncludesCountWithIterateAndWildcardIncludes_WhenLowered_ThenEveryStageIsCounted()
    {
        var context = CompilationContextFactory.For(
            expression: null,
            "Patient",
            includes: [IncludeOrganization, IterateOrganizationPartOf],
            revIncludes: [RevIncludeWildcard],
            options: new SearchPlanOptions { Shape = new ResultShape.IncludesCount() });

        var plan = Lower.Run(context, Symbols).Plan;
        var sql = SqlBuilder.Run(plan).Sql;

        plan.Includes.ShouldNotBeNull();
        plan.Includes!.ShouldContain(stage => stage.Iterate);
        plan.Includes!.ShouldContain(stage => stage.ReferenceSearchParamId == null);
        var count = sql[sql.IndexOf("SELECT COUNT_BIG(*) FROM (", StringComparison.Ordinal)..];
        count.Split("\nUNION\n").Length.ShouldBe(plan.Includes!.Count);
    }

    [Fact]
    public void GivenAnAccessConstraintOnARevIncludedType_WhenCountingIncludes_ThenTheCountedStageIsConstrainedExactlyAsTheIncludesPageStageIs()
    {
        // The count must not reveal how many hidden resources an include would reach: its stage body has to be
        // the access-controlled body the includes page reads, byte for byte.
        var count = LowerAndEmit(new ResultShape.IncludesCount(), accessConstraints: [ObservationFinalOnly]);
        var page = LowerAndEmit(new ResultShape.IncludesPage(), accessConstraints: [ObservationFinalOnly]);

        var binding = count.Plan.Includes!.ShouldHaveSingleItem().Constraints.ShouldNotBeNull().ShouldHaveSingleItem();
        binding.ConstraintTypeId.ShouldBe(ObservationTypeId);
        count.Sql.ShouldContain($"SearchParamId = {StatusParamId}");
        count.Sql.ShouldContain(
            $"(rsp.ResourceTypeId <> {ObservationTypeId} OR EXISTS (SELECT 1 FROM {SqlLabels.CteLabel(binding.ConstraintCteIndex)} ac " +
            "WHERE ac.T1 = rsp.ResourceTypeId AND ac.Sid1 = rsp.ResourceSurrogateId))");
        StageBody(count.Sql, 0).ShouldBe(StageBody(page.Sql, 0));
    }

    [Fact]
    public void GivenAnAllowListThatExcludesARevIncludedType_WhenCountingIncludes_ThenTheCountedStageCannotReachIt()
    {
        var count = LowerAndEmit(new ResultShape.IncludesCount(), allowedResourceTypes: ["Patient"], revIncludes: [RevIncludeObservationSubject]);
        var page = LowerAndEmit(new ResultShape.IncludesPage(), allowedResourceTypes: ["Patient"], revIncludes: [RevIncludeObservationSubject]);

        count.Plan.Includes!.ShouldHaveSingleItem().OutputTypeIds.ShouldBe([(short)-1]);
        StageBody(count.Sql, 0).ShouldContain("rsp.ResourceTypeId = -1");
        StageBody(count.Sql, 0).ShouldBe(StageBody(page.Sql, 0));
    }

    [Fact]
    public void GivenAnAllowListAndAWildcardRevInclude_WhenCountingIncludes_ThenTheWildcardIsNarrowedToTheAllowList()
    {
        var count = LowerAndEmit(new ResultShape.IncludesCount(), allowedResourceTypes: ["Patient", "Organization"], revIncludes: [RevIncludeWildcard]);

        count.Plan.Includes!.ShouldHaveSingleItem().OutputTypeIds.ShouldNotBeNull().ShouldBe([PatientTypeId, OrganizationTypeId], ignoreOrder: true);
    }

    [Fact]
    public void GivenAnAccessConstraintOnTheMatchType_WhenCountingIncludes_ThenTheSeedAndTheExclusionReadTheConstrainedMatchSet()
    {
        // Includes are seeded from -- and matches excluded through -- the constrained match set, so a hidden
        // match neither seeds an include nor shields one from the count.
        var context = CompilationContextFactory.For(
            expression: null,
            "Observation",
            includes: [new IncludeExpression(["Observation"], SubjectParam, "Observation", "Patient", null, wildCard: false, reversed: false, iterate: false)],
            accessConstraints: [ObservationFinalOnly],
            options: new SearchPlanOptions { Shape = new ResultShape.IncludesCount() });

        var plan = Lower.Run(context, Symbols).Plan;
        var sql = SqlBuilder.Run(plan).Sql;

        var matchPage = plan.Ctes[^1].ShouldBeOfType<CteDefinition.MatchPage>();
        plan.Ctes[matchPage.Spec.Root.Index].ShouldBeOfType<CteDefinition.Intersect>();
        sql.ShouldContain($"SearchParamId = {StatusParamId}");
    }

    // ─── IncludesPage.MatchReplay ──────────────────────────────────────────────────────────────────

    [Fact]
    public void GivenAnIncludesPageReplayingASortedMatchPage_WhenLowered_ThenTheSeedIsTheReplayedPageWithNoProbeRow()
    {
        var boundary = new PageSpec([new SqlParameterRef("2024-03-01")], BoundaryResourceTypeId: null, new SqlParameterRef(4200L));
        var replay = new MatchPageReplay(10, boundary);
        var context = CompilationContextFactory.For(
            expression: null,
            "Patient",
            includes: [IncludeOrganization],
            sort: [BirthDateDescending],
            options: new SearchPlanOptions { Shape = new ResultShape.IncludesPage(new IncludeBoundary(OrganizationTypeId, 900), replay), IncludeLimit = 10 });

        var plan = Lower.Run(context, Symbols).Plan;
        var sql = SqlBuilder.Run(plan).Sql;

        plan.Top.ShouldBe(10);
        plan.Page.ShouldBeSameAs(boundary);
        plan.MatchSpec.TopIncludesProbeRow.ShouldBeFalse();
        plan.MatchSpec.MatchReplay.ShouldBe(replay);
        plan.Ctes.ShouldNotContain(cte => cte is CteDefinition.MatchSeed);
        plan.IncludeSeed.ShouldBe(new CteRef(plan.Ctes.Count - 1));
        sql.ShouldContain("SELECT TOP (10) m.T1, m.Sid1");
        sql.ShouldContain("ORDER BY sk0.StartDateTime DESC, m.Sid1 ASC");
    }

    [Fact]
    public void GivenAReplayAndTheOriginalSearchPage_WhenLowered_ThenBothSeekTheSameBoundaryUnderTheSameSort()
    {
        var boundary = new PageSpec([new SqlParameterRef("2024-03-01")], BoundaryResourceTypeId: null, new SqlParameterRef(4200L));
        var original = Lower.Run(
            CompilationContextFactory.For(
                expression: null, "Patient", includes: [IncludeOrganization], sort: [BirthDateDescending],
                options: new SearchPlanOptions { Shape = new ResultShape.Matches(new SearchPaging.Keyset(11, boundary, TopIncludesProbeRow: true)) }),
            Symbols).Plan;
        var replay = Lower.Run(
            CompilationContextFactory.For(
                expression: null, "Patient", includes: [IncludeOrganization], sort: [BirthDateDescending],
                options: new SearchPlanOptions { Shape = new ResultShape.IncludesPage(MatchReplay: new MatchPageReplay(10, boundary)) }),
            Symbols).Plan;

        replay.Sort!.Keys.ShouldBe(original.Sort!.Keys);
        replay.Sort.Phase.ShouldBe(original.Sort.Phase);
        replay.Page.ShouldBe(original.Page);
        replay.Top.ShouldBe(original.MatchSpec.TrimmedPageSize);
    }

    [Fact]
    public void GivenANegativeReplayPageSize_WhenLowered_ThenItIsRejected()
    {
        var context = CompilationContextFactory.For(
            expression: null, "Patient", includes: [IncludeOrganization],
            options: new SearchPlanOptions { Shape = new ResultShape.IncludesPage(MatchReplay: new MatchPageReplay(-1)) });

        Should.Throw<NotSupportedException>(() => Lower.Run(context, Symbols)).Message.ShouldContain("PageSize must not be negative");
    }

    [Fact]
    public void GivenAReplayWithATypedBoundaryUnderACustomSort_WhenLowered_ThenTheKeysetGuardRejectsIt()
    {
        var typed = new PageSpec([new SqlParameterRef("2024-03-01")], new SqlParameterRef(PatientTypeId), new SqlParameterRef(4200L));
        var context = CompilationContextFactory.For(
            expression: null, "Patient", includes: [IncludeOrganization], sort: [BirthDateDescending],
            options: new SearchPlanOptions { Shape = new ResultShape.IncludesPage(MatchReplay: new MatchPageReplay(10, typed)) });

        Should.Throw<NotSupportedException>(() => Lower.Run(context, Symbols)).Message.ShouldContain("typed keyset Page");
    }

    [Fact]
    public void GivenAReplayBoundaryDecodedForTheOtherPhase_WhenLowered_ThenItIsRejected()
    {
        // A Valued-phase boundary carries the primary key's value; the MissingPrimary phase has none.
        var valuedBoundary = new PageSpec([new SqlParameterRef("2024-03-01")], BoundaryResourceTypeId: null, new SqlParameterRef(4200L));
        var context = CompilationContextFactory.For(
            expression: null, "Patient", includes: [IncludeOrganization], sort: [BirthDateDescending],
            options: new SearchPlanOptions
            {
                SortPhase = SortPhase.MissingPrimary,
                Shape = new ResultShape.IncludesPage(MatchReplay: new MatchPageReplay(10, valuedBoundary)),
            });

        Should.Throw<NotSupportedException>(() => Lower.Run(context, Symbols)).Message.ShouldContain("active sort key");
    }

    [Fact]
    public async Task GivenSearchOptionsWithASurrogateWindow_WhenCountingIncludesThroughTheCompiler_ThenTheWindowIsIgnored()
    {
        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[OrganizationParam.Url!.ToString()] = 55;
        resolver.ResourceTypeIds["Patient"] = PatientTypeId;
        resolver.ResourceTypeIds["Organization"] = OrganizationTypeId;
        var searchOptions = new SearchOptions
        {
            ResourceType = "Patient",
            Include = [IncludeOrganization],
            StartSurrogateId = 1000,
            EndSurrogateId = 2000,
        };

        var plan = await new SearchSqlCompiler(resolver).CreatePlanFromOptionsAsync(
            searchOptions, "Patient", new SearchPlanOptions { Shape = new ResultShape.IncludesCount() });
        var compiled = plan.Compile();

        compiled.Sql.ShouldContain("SELECT COUNT_BIG(*) FROM (");
        compiled.Parameters.Select(p => p.Value).ShouldNotContain(1000L);
        compiled.Parameters.Select(p => p.Value).ShouldNotContain(2000L);
    }

    private static (QueryPlan Plan, string Sql) LowerAndEmit(
        ResultShape shape,
        IReadOnlyList<AccessConstraint>? accessConstraints = null,
        IReadOnlyList<string>? allowedResourceTypes = null,
        IReadOnlyList<IncludeExpression>? revIncludes = null)
    {
        var context = CompilationContextFactory.For(
            expression: null,
            "Patient",
            revIncludes: revIncludes ?? [RevIncludeObservationSubject],
            accessConstraints: accessConstraints,
            allowedResourceTypes: allowedResourceTypes,
            options: new SearchPlanOptions { Shape = shape, IncludeLimit = 10 });

        var plan = Lower.Run(context, Symbols).Plan;
        return (plan, SqlBuilder.Run(plan).Sql);
    }

    private static string StageBody(string sql, int index)
    {
        var start = sql.IndexOf($"inc{index} AS (", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        return sql[start..sql.IndexOf("\n)", start, StringComparison.Ordinal)];
    }
}
