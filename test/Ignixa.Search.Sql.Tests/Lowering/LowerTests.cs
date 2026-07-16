using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class LowerTests
{
    [Fact]
    public void GivenASingleLeafPredicate_WhenLowered_ThenProducesAOneCteQueryPlan()
    {
        // Arrange
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Exact), new StringSearchValue("Smith"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = 202 },
            new Dictionary<string, short>());

        // Act
        var plan = Lower.Run(predicate, symbols);

        // Assert
        plan.Ctes.Count.ShouldBe(1);
        plan.Match.ShouldBe(new CteRef(0));
        plan.Ctes[0].ShouldBeOfType<CteDefinition.ParamSource>();
    }

    [Fact]
    public void GivenTwoAndedLeafPredicates_WhenLowered_ThenProducesAnIntersectOverBothCtes()
    {
        // Arrange
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var activeParam = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var namePredicate = new SearchParameterPredicateExpression(
            nameParam, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Exact), new StringSearchValue("Smith"));
        var activePredicate = new SearchParameterPredicateExpression(
            activeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null));
        var tree = new MultiaryExpression(MultiaryOperator.And, [namePredicate, activePredicate]);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [nameParam.Url.ToString()] = 202, [activeParam.Url.ToString()] = 44 },
            new Dictionary<string, short>());

        // Act
        var plan = Lower.Run(tree, symbols, top: 10);

        // Assert
        plan.Ctes.Count.ShouldBe(3);
        plan.Ctes[2].ShouldBeOfType<CteDefinition.Intersect>();
        plan.Match.ShouldBe(new CteRef(2));
        plan.Top.ShouldBe(10);
    }

    [Fact]
    public void GivenASingleElementAndTree_WhenLowered_ThenProducesNoIntersectNodeAndMatchesTheLeafDirectly()
    {
        // Arrange -- MultiaryExpression enforces a non-empty Expressions list, but a single-element
        // And is still a legal shape; LowerAnd must not synthesize a spurious Intersect(x, x) node.
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));
        var tree = new MultiaryExpression(MultiaryOperator.And, [predicate]);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = 202 },
            new Dictionary<string, short>());

        // Act
        var plan = Lower.Run(tree, symbols);

        // Assert
        plan.Ctes.Count.ShouldBe(1);
        plan.Match.ShouldBe(new CteRef(0));
        plan.Ctes[0].ShouldBeOfType<CteDefinition.ParamSource>();
    }

    [Fact]
    public void GivenAnUnsupportedExpressionShape_WhenLowered_ThenThrowsRatherThanSilentlyDroppingIt()
    {
        // Arrange -- NotExpression is out of scope (":not" needs ResourceTypeId-based seed synthesis, not built yet)
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));
        var notExpression = Expression.Not(predicate);
        var symbols = new SymbolTable(new Dictionary<string, short> { [parameter.Url.ToString()] = 202 }, new Dictionary<string, short>());

        // Act & Assert
        Should.Throw<NotSupportedException>(() => Lower.Run(notExpression, symbols));
    }

    [Fact]
    public void GivenAPredicateWithAnUnsupportedSearchValueType_WhenLowered_ThenThrowsRatherThanSilentlyDroppingIt()
    {
        // Arrange -- DateSearchValue has no tier-1 lowering rule (Date/Number/Quantity/Uri and
        // composites are out of scope for this plan); the dispatcher must throw, not fall through
        // to one of the three handled rules.
        var parameter = new SearchParameterInfo("birthdate", "birthdate", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Patient-birthdate"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new DateTimeSearchValue(new PartialDateTime(DateTimeOffset.UtcNow)));
        var symbols = new SymbolTable(new Dictionary<string, short> { [parameter.Url.ToString()] = 202 }, new Dictionary<string, short>());

        // Act & Assert
        Should.Throw<NotSupportedException>(() => Lower.Run(predicate, symbols));
    }
}
