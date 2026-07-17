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
    public void GivenANotExpressionWithNoTargetResourceTypeSupplied_WhenLowered_ThenThrowsRatherThanSilentlyDroppingIt()
    {
        // Arrange -- NotExpression itself IS supported by Lower now (see :not's ResourceSource/Except
        // wiring), but its ResourceSource seed needs a targetResourceType, and this call uses the
        // 2-arg Lower.Run overload, which defaults it to null -- still throws, now for that reason
        // rather than "NotExpression is unsupported".
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));
        var notExpression = Expression.Not(predicate);
        var symbols = new SymbolTable(new Dictionary<string, short> { [parameter.Url.ToString()] = 202 }, new Dictionary<string, short>());

        // Act & Assert
        Should.Throw<NotSupportedException>(() => Lower.Run(notExpression, symbols));
    }

    [Fact]
    public void GivenABareNotModifiedPredicateOutsideASearchParameterExpressionWrapper_WhenLowered_ThenThrowsRatherThanSilentlyMatchingPositively()
    {
        // Arrange -- the real binder always wraps a :not-modified predicate in SearchParameterExpression
        // (LowerSearchParameter is where :not is actually handled), so this shape never occurs in
        // practice. This is a defense-in-depth guard: if it ever did occur (a hand-built tree, or a
        // future binder change), the old bug this test guards against was LowerNode's leaf case
        // silently lowering it as a positive match instead of a negation -- a real bug this plan's
        // Task 5 review caught for the SearchParameterExpression-wrapped shape, closed here for the
        // unwrapped shape too.
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Not), new StringSearchValue("Smith"));
        var symbols = new SymbolTable(new Dictionary<string, short> { [parameter.Url.ToString()] = 202 }, new Dictionary<string, short>());

        // Act & Assert
        Should.Throw<NotSupportedException>(() => Lower.Run(predicate, symbols));
    }

    [Fact]
    public void GivenAPredicateWithAnUnsupportedSearchValueType_WhenLowered_ThenThrowsRatherThanSilentlyDroppingIt()
    {
        // Arrange -- CompositeIndexSearchValue has no tier-1 lowering rule (composites are out of
        // scope for this plan); the dispatcher must throw, not fall through to one of the handled rules.
        var parameter = new SearchParameterInfo("component-value-quantity", "component-value-quantity", SearchParamType.Composite, new Uri("http://hl7.org/fhir/SearchParameter/Observation-component-value-quantity"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new CompositeIndexSearchValue([[new QuantitySearchValue(system: null!, code: null!, 5.4m)]]));
        var symbols = new SymbolTable(new Dictionary<string, short> { [parameter.Url.ToString()] = 202 }, new Dictionary<string, short>());

        // Act & Assert
        Should.Throw<NotSupportedException>(() => Lower.Run(predicate, symbols));
    }
}
