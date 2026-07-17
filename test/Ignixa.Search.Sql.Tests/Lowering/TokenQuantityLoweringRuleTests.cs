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

public class TokenQuantityLoweringRuleTests
{
    private static LeafContext ContextResolving(SearchParameterInfo compositeParameter, short searchParamId)
        => new(new SymbolTable(
            new Dictionary<string, short> { [compositeParameter.Url!.ToString()] = searchParamId },
            new Dictionary<string, short>()));

    private static SearchParameterInfo CompositeParameter()
        => new("component-code-value-quantity", "component-code-value-quantity", SearchParamType.Composite,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-component-code-value-quantity"));

    private static SearchParameterInfo ComponentParameter(string code)
        => new(code, code, SearchParamType.Token, new Uri($"http://hl7.org/fhir/SearchParameter/Observation-{code}"));

    [Fact]
    public void GivenATokenComponentAndAnUnqualifiedQuantityComponent_WhenLowered_ThenComparesCode1AndLowHighValue2()
    {
        // Arrange
        var composite = CompositeParameter();
        var tokenParam = ComponentParameter("component-code");
        var quantityParam = ComponentParameter("component-value-quantity");
        var components = new SearchParameterPredicateExpression[]
        {
            new(tokenParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null)),
            new(quantityParam, SearchComparator.Eq, modifier: null, new QuantitySearchValue(system: null!, code: null!, 120m)),
        };

        // Act
        var cte = TokenQuantityLoweringRule.Lower(composite, components, ContextResolving(composite, 402), 104);

        // Assert
        cte.SearchParamId.ShouldBe((short)402);
        cte.ResourceTypeId.ShouldBe((short)104);
        cte.Table.TableName.ShouldBe("TokenQuantityCompositeSearchParam");
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var tokenPredicate = and.Left.ShouldBeOfType<Predicate.Equal>();
        tokenPredicate.Column.Column.ShouldBe("Code1");
        var quantityPredicate = and.Right.ShouldBeOfType<Predicate.And>();
        quantityPredicate.Left.ShouldBeOfType<Predicate.GreaterThanOrEqual>().Column.Column.ShouldBe("LowValue2");
        quantityPredicate.Right.ShouldBeOfType<Predicate.LessThanOrEqual>().Column.Column.ShouldBe("HighValue2");
    }

    [Fact]
    public void GivenASystemQualifiedQuantityComponent_WhenLowered_ThenThrows()
    {
        // Arrange
        var composite = CompositeParameter();
        var tokenParam = ComponentParameter("component-code");
        var quantityParam = ComponentParameter("component-value-quantity");
        var components = new SearchParameterPredicateExpression[]
        {
            new(tokenParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null)),
            new(quantityParam, SearchComparator.Eq, modifier: null, new QuantitySearchValue("http://unitsofmeasure.org", "mg", 120m)),
        };

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            TokenQuantityLoweringRule.Lower(composite, components, ContextResolving(composite, 402), 104));
    }
}
