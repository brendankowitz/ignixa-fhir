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
            Shape: new ResultShape.Count(),
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
        plan.EffectiveShape.ShouldBe(new ResultShape.Count());
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
        plan.EffectiveShape.ShouldBe(new ResultShape.Count(RestrictToSortPhase: true));
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

    [Fact]
    public void GivenANegativeIncludeLimit_WhenLowering_ThenThrowsNotSupported()
    {
        // Arrange
        var (predicate, symbols) = NameQuery();

        // Act & Assert
        Should.Throw<NotSupportedException>(() => LowerHarness.Run(
            predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: -1,
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
    public void GivenANullOffsetSpec_WhenLowering_ThenThrowsRatherThanSilentlyUnpaging()
    {
        // Arrange -- a positional record parameter cannot enforce non-nullness against a caller who ignores the
        // annotation, and falling through would emit an unpaged statement returning every row.
        var (predicate, symbols) = NameQuery();
        var context = CompilationContextFactory.For(
            predicate,
            "Patient",
            options: new SearchPlanOptions { Paging = new SearchPaging.Offset(null!) });

        // Act & Assert
        Should.Throw<NotSupportedException>(() => Lower.Run(context, symbols));
    }

    [Fact]
    public void GivenAnOffsetPageInTheMissingPrimaryPhase_WhenLowering_ThenBothSurvive()
    {
        // Arrange -- the phase names a segment of the sort, which is orthogonal to how that segment is paged.
        // Nesting it under keyset paging would make resources missing the sort key unreachable by offset paging.
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
