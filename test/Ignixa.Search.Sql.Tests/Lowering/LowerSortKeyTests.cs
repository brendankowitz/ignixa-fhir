using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class LowerSortKeyTests
{
    private static SymbolTable SymbolsResolving(SearchParameterInfo parameter, short searchParamId)
        => new(
            new Dictionary<string, short> { [parameter.Url.ToString()] = searchParamId },
            new Dictionary<string, short>());

    [Theory]
    [InlineData(SearchParamType.Token, "TokenSearchParam", "Code")]
    [InlineData(SearchParamType.Number, "NumberSearchParam", "LowValue")]
    [InlineData(SearchParamType.Quantity, "QuantitySearchParam", "LowValue")]
    [InlineData(SearchParamType.Reference, "ReferenceSearchParam", "ReferenceResourceId")]
    [InlineData(SearchParamType.Uri, "UriSearchParam", "Uri")]
    public void GivenASortByAnAggregatedType_WhenLowered_ThenTheKeyCarriesTheCorrectTableAndColumn(
        SearchParamType paramType, string expectedTable, string expectedColumn)
    {
        // Arrange
        var parameter = new SearchParameterInfo("status", "status", paramType, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        var sortExpression = new SortExpression(parameter, Ignixa.Search.Expressions.SortOrder.Ascending);
        var symbols = SymbolsResolving(parameter, 77);

        // Act
        var key = Lower.BuildSortKey(sortExpression, symbols);

        // Assert
        key.Kind.ShouldBe(SortKeyKind.Aggregated);
        key.SearchParamId.ShouldBe((short)77);
        key.Table.ShouldNotBeNull();
        key.Table!.TableName.ShouldBe(expectedTable);
        key.Column.ShouldNotBeNull();
        key.Column!.Name.ShouldBe(expectedColumn);
    }

    [Fact]
    public void GivenASortByAnUnsupportedCompositeType_WhenLowered_ThenThrowsNotSupportedException()
    {
        // Arrange -- Composite has no sort meaning (no single scalar column); confirms the switch's
        // default arm still throws for genuinely unsortable types, not silently falling into Aggregated.
        var parameter = new SearchParameterInfo("component-code-value", "component-code-value", SearchParamType.Composite, new Uri("http://hl7.org/fhir/SearchParameter/Observation-component-code-value"));
        var sortExpression = new SortExpression(parameter, Ignixa.Search.Expressions.SortOrder.Ascending);
        var symbols = SymbolsResolving(parameter, 88);

        // Act & Assert
        Should.Throw<NotSupportedException>(() => Lower.BuildSortKey(sortExpression, symbols))
            .Message.ShouldContain("Composite");
    }
}
