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
    public void GivenSortByResourceId_WhenBuildingSortKey_ThenReturnsResourceIdKindWithNoSearchParamId()
    {
        // Arrange
        var idParameter = new SearchParameterInfo("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
        var sortExpression = new SortExpression(idParameter, Ignixa.Search.Expressions.SortOrder.Ascending);
        var symbols = new SymbolTable(new Dictionary<string, short>(), new Dictionary<string, short>());

        // Act
        var key = Lower.BuildSortKey(sortExpression, symbols);

        // Assert
        key.Kind.ShouldBe(SortKeyKind.ResourceId);
        key.SearchParamId.ShouldBeNull();
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

    [Fact]
    public void GivenASortByTheTypeResourceColumn_WhenLowered_ThenReturnsResourceTypeKindWithNoSearchParamId()
    {
        // Arrange -- _type is a Token parameter, so without an explicit guard it would fall into the
        // Aggregated arm and hit the SearchParamId lookup. Resolve deliberately never collects a
        // resource-column parameter, so that lookup would throw KeyNotFoundException blaming Resolve for
        // skipping a node kind rather than naming the real problem.
        var typeParameter = new SearchParameterInfo("_type", "_type", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-type"));
        var sortExpression = new SortExpression(typeParameter, Ignixa.Search.Expressions.SortOrder.Descending);
        var symbols = new SymbolTable(new Dictionary<string, short>(), new Dictionary<string, short>());

        // Act
        var key = Lower.BuildSortKey(sortExpression, symbols);

        // Assert -- the match set already projects the resource's type id as T1, so no join and no
        // search-param lookup is involved.
        key.Kind.ShouldBe(SortKeyKind.ResourceType);
        key.SearchParamId.ShouldBeNull();
        key.Table.ShouldBeNull();
        key.Column.ShouldBeNull();
        key.Direction.ShouldBe(Ignixa.Search.Expressions.SortOrder.Descending);
    }

    [Fact]
    public void GivenAResourceTypePrimarySortKeyInTheMissingPrimaryPhase_WhenLowered_ThenThrowsNotSupportedException()
    {
        // Arrange -- ResourceTypeId is non-nullable, so a _type sort value is never "missing" and the key
        // has no MissingPrimary segment. Without this rejection EmitMissingPrimaryFilter would interpolate
        // a null SearchParamId into SQL text.
        var typeParameter = new SearchParameterInfo("_type", "_type", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-type"));
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            Lower.Run(
                expression: null, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
                sort: [new SortExpression(typeParameter, Ignixa.Search.Expressions.SortOrder.Ascending)],
                sortPhase: SortPhase.MissingPrimary, page: null))
            .Message.ShouldContain("MissingPrimary");
    }

    [Fact]
    public void GivenAResourceIdPrimarySortKeyInTheMissingPrimaryPhase_WhenLowered_ThenThrowsNotSupportedException()
    {
        // Arrange -- _id resolves to dbo.Resource.ResourceId, a non-nullable resource column, so it is
        // never "missing" and has no MissingPrimary segment. Without this rejection
        // EmitMissingPrimaryFilter would interpolate a null SearchParamId into SQL text.
        var idParameter = new SearchParameterInfo("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            Lower.Run(
                expression: null, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
                sort: [new SortExpression(idParameter, Ignixa.Search.Expressions.SortOrder.Ascending)],
                sortPhase: SortPhase.MissingPrimary, page: null))
            .Message.ShouldContain("MissingPrimary");
    }
}
