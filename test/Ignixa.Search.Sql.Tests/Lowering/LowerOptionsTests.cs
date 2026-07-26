using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class LowerOptionsTests
{
    [Fact]
    public void GivenLowerOptions_WhenSettingAsAddedInputs_ThenEachIsReadableByName()
    {
        // Arrange & Act
        var options = new LowerOptions
        {
            SystemLevelSearch = true,
            OffsetPage = new OffsetSpec(20, 10),
            CountPhaseScoped = true,
        };

        // Assert
        options.SystemLevelSearch.ShouldBeTrue();
        options.OffsetPage!.Offset.ShouldBe(20);
        options.CountPhaseScoped.ShouldBeTrue();
    }

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
            CountOnly: true,
            OffsetPage: new OffsetSpec(5, 10),
            CountPhaseScoped: true);

        // Assert
        plan.CountOnly.ShouldBeTrue();
        plan.OffsetPage!.Offset.ShouldBe(5);
        plan.CountPhaseScoped.ShouldBeTrue();
        plan.Visibility.ShouldBeNull();
        plan.Projection.ShouldBeNull();
    }

    [Fact]
    public void GivenOffsetPageAndKeysetPage_WhenLowering_ThenThrowsNotSupported()
    {
        // Arrange
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = 202 },
            new Dictionary<string, short> { ["Patient"] = 103 });
        var page = new PageSpec([], new SqlParameterRef((short)103), new SqlParameterRef(7000L));
        var options = new LowerOptions { OffsetPage = new OffsetSpec(0, 10) };

        // Act & Assert
        Should.Throw<NotSupportedException>(() => Lower.Run(
            predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: page, options: options));
    }

    [Fact]
    public void GivenCountPhaseScopedWithoutCountOnly_WhenLowering_ThenThrowsNotSupported()
    {
        // Arrange
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = 202 },
            new Dictionary<string, short> { ["Patient"] = 103 });
        var options = new LowerOptions { CountPhaseScoped = true, CountOnly = false };

        // Act & Assert
        Should.Throw<NotSupportedException>(() => Lower.Run(
            predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null, options: options));
    }
}
