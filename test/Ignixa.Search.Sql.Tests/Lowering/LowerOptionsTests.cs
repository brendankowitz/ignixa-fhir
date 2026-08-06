using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Search.Sql.Tests.TestSupport;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class LowerOptionsTests
{
    [Fact]
    public void GivenAQueryPlan_WhenConstructedWithNamedTailArguments_ThenEachSlotHoldsItsOwnValue()
    {
        // Arrange
        var ctes = new List<CteDefinition>();
        var match = new CteRef(0);

        // Act -- named arguments are mandatory for the tail; this test exists to pin the order
        var plan = new QueryPlan(
            ctes,
            match,
            Shape: new ResultShape.Count.AllMatches(),
            OffsetPage: new OffsetSpec(5, 10));

        // Assert
        plan.CountOnly.ShouldBeTrue();
        plan.OffsetPage!.Offset.ShouldBe(5);
        plan.Visibility.ShouldBeNull();
        plan.Projection.ShouldBeNull();
    }

    [Fact]
    public void GivenAnUnrestrictedCount_WhenLowering_ThenTheSortIsStillValidatedAndCarried()
    {
        // Arrange
        var (predicate, symbols) = NameQuery();
        var sortExpression = new SortExpression(
            new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name")),
            SortOrder.Ascending);

        // Act
        var plan = LowerHarness.Run(
            predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [sortExpression], sortPhase: SortPhase.Valued, page: null,
            options: new LowerOptions { CountOnly = true }).Plan;

        // Assert -- lowering never skips sort validation; the emitter ignores the Sort for a count that did
        // not ask to be restricted to the phase.
        plan.CountOnly.ShouldBeTrue();
        plan.Sort.ShouldNotBeNull();
        plan.EffectiveShape.ShouldBe(new ResultShape.Count.AllMatches());
    }

    [Fact]
    public void GivenAnUnrestrictedCountWithTooManySortKeys_WhenLowering_ThenThrowsNotSupported()
    {
        // Arrange -- a count must not become a hole in sort validation. Four keys are rejected for a row search,
        // so they are rejected here too even though the emitter will ignore the Sort.
        var (predicate, symbols) = NameQuery();
        var name = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var fourKeys = Enumerable.Repeat(new SortExpression(name, SortOrder.Ascending), 4).ToList();

        // Act & Assert
        Should.Throw<NotSupportedException>(() => LowerHarness.Run(
            predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: fourKeys, sortPhase: SortPhase.Valued, page: null,
            options: new LowerOptions { CountOnly = true }));
    }

    [Fact]
    public void GivenACountRestrictedToItsSortPhase_WhenLowering_ThenTheSortSurvivesToScopeTheCountToItsPhase()
    {
        // Arrange
        var (predicate, symbols) = NameQuery();
        var sortExpression = new SortExpression(
            new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name")),
            SortOrder.Ascending);

        // Act
        var plan = LowerHarness.Run(
            predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [sortExpression], sortPhase: SortPhase.MissingPrimary, page: null,
            options: new LowerOptions { CountOnly = true, CountPhaseScoped = true }).Plan;

        // Assert
        plan.Sort.ShouldNotBeNull();
        plan.Sort!.Phase.ShouldBe(SortPhase.MissingPrimary);
        plan.EffectiveShape.ShouldBe(new ResultShape.Count.CurrentSortPhase());
    }

    [Fact]
    public void GivenACountRestrictedToASortPhaseButNoSort_WhenLowering_ThenThrowsNotSupported()
    {
        // Arrange -- there is no sort phase for the count to be restricted to
        var (predicate, symbols) = NameQuery();

        // Act & Assert
        Should.Throw<NotSupportedException>(() => LowerHarness.Run(
            predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null,
            options: new LowerOptions { CountOnly = true, CountPhaseScoped = true }));
    }

    [Fact]
    public void GivenAMissingPrimaryPhaseWithNoSort_WhenLoweringAMatch_ThenThrowsNotSupported()
    {
        // Arrange -- with no _sort there is no primary key to be missing, so there is no second segment. Emitting
        // the phase-free statement instead would hand a two-phase caller an ordinary first page.
        var (predicate, symbols) = NameQuery();

        // Act & Assert
        Should.Throw<NotSupportedException>(() => LowerHarness.Run(
            predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.MissingPrimary, page: null));
    }

    [Fact]
    public void GivenANegativeTop_WhenLowering_ThenThrowsNotSupported()
    {
        // Arrange
        var (predicate, symbols) = NameQuery();

        // Act & Assert
        Should.Throw<NotSupportedException>(() => LowerHarness.Run(
            predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null,
            options: new LowerOptions { Top = -1 }));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void GivenAnOutOfRangeIncludeLimit_WhenLowering_ThenThrowsNotSupported(int includeLimit)
    {
        // Arrange -- the limit is emitted as TOP (IncludeLimit + 1), so int.MaxValue overflows the
        // truncation probe to a negative row count just as a negative limit is invalid outright.
        var (predicate, symbols) = NameQuery();

        // Act & Assert
        Should.Throw<NotSupportedException>(() => LowerHarness.Run(
            predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: includeLimit,
            sort: [], sortPhase: SortPhase.Valued, page: null));
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(0, 0)]
    [InlineData(0, -5)]
    public void GivenAnOutOfRangeOffsetSpec_WhenLowering_ThenThrowsNotSupported(int offset, int limit)
    {
        // Arrange -- OFFSET/FETCH rejects a negative skip and a non-positive fetch at runtime
        var (predicate, symbols) = NameQuery();

        // Act & Assert
        Should.Throw<NotSupportedException>(() => LowerHarness.Run(
            predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null,
            options: new LowerOptions { OffsetPage = new OffsetSpec(offset, limit) }));
    }

    [Fact]
    public void GivenAZeroLimitOffsetSpecWithAProbeRow_WhenLowering_ThenItIsAcceptedBecauseTheFetchIsStillPositive()
    {
        // Arrange -- the two-phase sort executor's floor case: an earlier phase already filled the page, so
        // this phase's entire budget is the has-more lookahead row. It fetches one row, which OFFSET/FETCH
        // accepts; rejecting it on Limit alone would 400 an ordinary paged sort.
        var (predicate, symbols) = NameQuery();

        // Act
        var plan = LowerHarness.Run(
            predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null,
            options: new LowerOptions { OffsetPage = new OffsetSpec(40, 0, ProbeExtraRow: true) }).Plan;

        // Assert
        plan.OffsetPage!.Limit.ShouldBe(0);
        plan.OffsetPage!.FetchCount.ShouldBe(1);
    }

    [Fact]
    public void GivenANullOffsetSpec_WhenLowering_ThenThrowsRatherThanSilentlyUnpaging()
    {
        // Arrange -- a positional record parameter cannot enforce non-nullness against a caller who ignores the
        // annotation, and falling through would emit an unpaged statement returning every row.
        var (predicate, symbols) = NameQuery();
        var context = CompilationContextFactory.For(
            predicate,
            "Patient",
            options: new SearchPlanOptions { Shape = new ResultShape.Matches(new SearchPaging.Offset(null!)) });

        // Act & Assert
        Should.Throw<NotSupportedException>(() => Lower.Run(context, symbols));
    }

    [Fact]
    public void GivenAnOffsetPageInTheMissingPrimaryPhase_WhenLowering_ThenBothSurvive()
    {
        // Arrange -- the phase names a segment of the sort, which is orthogonal to how that segment is paged.
        // Were it a property of keyset paging, resources missing the sort key would be unreachable by offset
        // paging at all.
        var (predicate, symbols) = NameQuery();
        var sortExpression = new SortExpression(
            new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name")),
            SortOrder.Ascending);

        // Act
        var plan = LowerHarness.Run(
            predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [sortExpression], sortPhase: SortPhase.MissingPrimary, page: null,
            options: new LowerOptions { OffsetPage = new OffsetSpec(20, 10) }).Plan;

        // Assert
        plan.OffsetPage!.Offset.ShouldBe(20);
        plan.Sort!.Phase.ShouldBe(SortPhase.MissingPrimary);
    }

    [Fact]
    public void GivenASortedQueryWithNoPaging_WhenLowering_ThenTheSortStartsInTheValuedSegment()
    {
        // Arrange -- the phase is independent of paging, so an unpaged sorted query still has to name a
        // segment. Valued is the first one a caller reads, so it is the default.
        var (predicate, symbols) = NameQuery();
        var sortExpression = new SortExpression(
            new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name")),
            SortOrder.Ascending);

        // Act
        var plan = LowerHarness.RunWithoutPaging(
            predicate, symbols, targetResourceType: "Patient", sort: [sortExpression]).Plan;

        // Assert
        plan.Top.ShouldBeNull();
        plan.Page.ShouldBeNull();
        plan.OffsetPage.ShouldBeNull();
        plan.Sort!.Phase.ShouldBe(SortPhase.Valued);
    }

    [Fact]
    public void GivenAnUnpagedMissingPrimaryQuery_WhenLowering_ThenTheSegmentStillApplies()
    {
        // Arrange -- the missing segment is reachable with no paging at all, which is why the phase does not
        // live on SearchPaging: expressing this would otherwise need a keyset that pages nothing.
        var (predicate, symbols) = NameQuery();
        var sortExpression = new SortExpression(
            new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name")),
            SortOrder.Ascending);

        // Act
        var plan = LowerHarness.RunWithoutPaging(
            predicate, symbols, targetResourceType: "Patient", sort: [sortExpression],
            sortPhase: SortPhase.MissingPrimary).Plan;

        // Assert
        plan.Sort!.Phase.ShouldBe(SortPhase.MissingPrimary);
        plan.Top.ShouldBeNull();
    }

    [Fact]
    public void GivenAnUndefinedSortPhase_WhenLowering_ThenThrowsNotSupportedException()
    {
        // Arrange -- SortPhase arrives from caller options, so a cast or a deserialised int reaches lowering.
        // Anything but MissingPrimary reads the Valued segment, replaying rows the caller has already paged.
        var (predicate, symbols) = NameQuery();

        // Act / Assert
        Should.Throw<NotSupportedException>(() => LowerHarness.Run(
            predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: (SortPhase)7, page: null));
    }

    private static (Expression Predicate, SymbolTable Symbols) NameQuery()
    {
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = 202 },
            new Dictionary<string, short> { ["Patient"] = 103 });
        return (predicate, symbols);
    }
}
