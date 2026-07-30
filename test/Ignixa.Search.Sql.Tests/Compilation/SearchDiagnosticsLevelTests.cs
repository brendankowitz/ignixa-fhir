using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Tests.TestSupport;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Compilation;

/// <summary>
/// Pins that <see cref="SearchDiagnosticsLevel.None"/> is actually cheaper, not merely quieter. The options
/// builder takes a far more expensive path when it is handed a trace collector — it parses with a full syntax
/// tree to attach a <see cref="ParameterTrace"/> per parameter — so an untraced compile must pass none.
/// </summary>
public class SearchDiagnosticsLevelTests
{
    [Theory]
    [InlineData(SearchDiagnosticsLevel.None, false)]
    [InlineData(SearchDiagnosticsLevel.Parameters, true)]
    [InlineData(SearchDiagnosticsLevel.Full, true)]
    public async Task GivenADiagnosticsLevel_WhenCompilingAQueryString_ThenTheBuilderCollectsTracesOnlyWhenTraced(
        SearchDiagnosticsLevel level,
        bool expectedCollection)
    {
        // Arrange
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));
        var expression = new SearchParameterExpression(nameParam, predicate);
        var builder = new FakeSearchOptionsBuilder(
            new SearchOptions { ResourceType = "Patient", Expression = expression },
            [new ParameterTrace(0, "name", null, "Smith", null, expression, new ParameterOutcome.Compiled(), null)]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var result = await new SearchSqlCompiler(resolver, builder).TryCreatePlanAsync(
            "Patient",
            [new QueryParameter("name", "Smith")],
            new SearchPlanOptions { DiagnosticsLevel = level });

        // Assert
        result.Succeeded.ShouldBeTrue();
        builder.LastCallCollectedTraces.ShouldBe(expectedCollection);
    }
}
