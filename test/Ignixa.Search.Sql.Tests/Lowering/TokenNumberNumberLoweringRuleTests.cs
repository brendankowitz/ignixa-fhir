using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Lowering.Composite;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class TokenNumberNumberLoweringRuleTests
{
    private static LeafContext ContextResolving(
        SearchParameterInfo compositeParameter,
        short searchParamId,
        IReadOnlyDictionary<string, int?>? systemIds = null)
        => new(new SymbolTable(
            new Dictionary<string, short> { [compositeParameter.Url!.ToString()] = searchParamId },
            new Dictionary<string, short>(),
            compartmentMembership: null,
            systemIds: systemIds));

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
        var cte = TokenNumberNumberLoweringRule.Lower(composite, components, ContextResolving(composite, 302), 104);

        // Assert
        cte.SearchParamId.ShouldBe((short)302);
        cte.ResourceTypeId.ShouldBe((short)104);
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
    public void GivenASystemQualifiedTokenComponent_WhenLowered_ThenComparesSystemId1AndCode1()
    {
        // Arrange — system|code on the token slot
        var composite = CompositeParameter();
        var systemIds = new Dictionary<string, int?> { ["http://loinc.org"] = 42 };
        var components = new SearchParameterPredicateExpression[]
        {
            new(ComponentParameter("code"), SearchComparator.Eq, modifier: null, new TokenSearchValue(system: "http://loinc.org", code: "8480-6", text: null)),
            new(ComponentParameter("low"), SearchComparator.Ge, modifier: null, new NumberSearchValue(5m)),
            new(ComponentParameter("high"), SearchComparator.Le, modifier: null, new NumberSearchValue(10m)),
        };

        // Act
        var cte = TokenNumberNumberLoweringRule.Lower(composite, components, ContextResolving(composite, 302, systemIds), 104);

        // Assert
        var outer = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var inner = outer.Left.ShouldBeOfType<Predicate.And>();
        var tokenAnd = inner.Left.ShouldBeOfType<Predicate.And>();
        var systemEqual = tokenAnd.Left.ShouldBeOfType<Predicate.Equal>();
        systemEqual.Column.Column.ShouldBe("SystemId1");
        systemEqual.Value.Value.ShouldBe(42);
        var codeEqual = tokenAnd.Right.ShouldBeOfType<Predicate.Equal>();
        codeEqual.Column.Column.ShouldBe("Code1");
        codeEqual.Value.Value.ShouldBe("8480-6");
    }
}
