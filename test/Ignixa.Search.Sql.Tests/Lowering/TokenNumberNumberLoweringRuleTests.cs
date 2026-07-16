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

public class TokenNumberNumberLoweringRuleTests
{
    private static LeafContext ContextResolving(SearchParameterInfo compositeParameter, short searchParamId)
        => new(new SymbolTable(
            new Dictionary<string, short> { [compositeParameter.Url!.ToString()] = searchParamId },
            new Dictionary<string, short>()));

    private static SearchParameterInfo CompositeParameter()
        => new("component-code-value-number-number", "component-code-value-number-number", SearchParamType.Composite,
            new Uri("http://example.org/fhir/SearchParameter/Observation-component-code-value-number-number"));

    private static SearchParameterInfo ComponentParameter(string code)
        => new(code, code, SearchParamType.Token, new Uri($"http://example.org/fhir/SearchParameter/Observation-{code}"));

    [Fact]
    public void GivenACodeAndTwoUnqualifiedNumberComponents_WhenLowered_ThenComparesCode1AndBothLowHighPairs()
    {
        // Arrange
        var composite = CompositeParameter();
        var components = new SearchParameterPredicateExpression[]
        {
            new(ComponentParameter("code"), SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null)),
            new(ComponentParameter("low"), SearchComparator.Ge, modifier: null, new NumberSearchValue(5m)),
            new(ComponentParameter("high"), SearchComparator.Le, modifier: null, new NumberSearchValue(10m)),
        };

        // Act
        var cte = TokenNumberNumberLoweringRule.Lower(composite, components, ContextResolving(composite, 302));

        // Assert
        cte.SearchParamId.ShouldBe((short)302);
        cte.Table.TableName.ShouldBe("TokenNumberNumberCompositeSearchParam");
        var outer = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var inner = outer.Left.ShouldBeOfType<Predicate.And>();
        var tokenPredicate = inner.Left.ShouldBeOfType<Predicate.Equal>();
        tokenPredicate.Column.Column.ShouldBe("Code1");
        tokenPredicate.Value.Value.ShouldBe("8480-6");
        var number1Predicate = inner.Right.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        number1Predicate.Column.Column.ShouldBe("LowValue2");
        var number2Predicate = outer.Right.ShouldBeOfType<Predicate.LessThanOrEqual>();
        number2Predicate.Column.Column.ShouldBe("HighValue3");
    }

    [Fact]
    public void GivenASystemQualifiedTokenComponent_WhenLowered_ThenThrows()
    {
        // Arrange
        var composite = CompositeParameter();
        var components = new SearchParameterPredicateExpression[]
        {
            new(ComponentParameter("code"), SearchComparator.Eq, modifier: null, new TokenSearchValue(system: "http://loinc.org", code: "8480-6", text: null)),
            new(ComponentParameter("low"), SearchComparator.Ge, modifier: null, new NumberSearchValue(5m)),
            new(ComponentParameter("high"), SearchComparator.Le, modifier: null, new NumberSearchValue(10m)),
        };

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            TokenNumberNumberLoweringRule.Lower(composite, components, ContextResolving(composite, 302)));
    }
}
