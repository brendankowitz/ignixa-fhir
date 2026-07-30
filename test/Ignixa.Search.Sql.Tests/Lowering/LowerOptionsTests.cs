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
    public void GivenACountWithoutAContinuation_WhenLowering_ThenTheSortIsDroppedSoTheCountCoversEveryMatch()
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

        // Assert -- a sort survives onto a count plan only when it scopes the count
        plan.CountOnly.ShouldBeTrue();
        plan.Sort.ShouldBeNull();
    }

    [Fact]
    public void GivenACountWithAContinuation_WhenLowering_ThenTheSortSurvivesToScopeTheCountToItsPhase()
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
            options: new LowerOptions { CountOnly = true }).Plan;

        // Assert
        plan.Sort.ShouldNotBeNull();
        plan.Sort!.Phase.ShouldBe(SortPhase.MissingPrimary);
    }

    [Fact]
    public void GivenACountWithAContinuationButNoSort_WhenLowering_ThenThrowsNotSupported()
    {
        // Arrange -- there is no sort phase for the continuation to scope the count to
        var (predicate, symbols) = NameQuery();

        // Act & Assert
        Should.Throw<NotSupportedException>(() => LowerHarness.Run(
            predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.MissingPrimary, page: null,
            options: new LowerOptions { CountOnly = true }));
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
