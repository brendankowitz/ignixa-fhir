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

public class TokenQuantityLoweringRuleTests
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
    public void GivenASystemQualifiedTokenComponent_WhenLowered_ThenComparesSystemId1AndCode1()
    {
        // Arrange — system|code on the token slot (slot 1)
        var composite = CompositeParameter();
        var tokenParam = ComponentParameter("component-code");
        var quantityParam = ComponentParameter("component-value-quantity");
        var systemIds = new Dictionary<string, int?> { ["http://loinc.org"] = 42 };
        var components = new SearchParameterPredicateExpression[]
        {
            new(tokenParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: "http://loinc.org", code: "8480-6", text: null)),
            new(quantityParam, SearchComparator.Eq, modifier: null, new QuantitySearchValue(system: null!, code: null!, 120m)),
        };

        // Act
        var cte = TokenQuantityLoweringRule.Lower(composite, components, ContextResolving(composite, 402, systemIds), 104);

        // Assert
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var tokenAnd = and.Left.ShouldBeOfType<Predicate.And>();
        var systemEqual = tokenAnd.Left.ShouldBeOfType<Predicate.Equal>();
        systemEqual.Column.Column.ShouldBe("SystemId1");
        systemEqual.Value.Value.ShouldBe(42);
        var codeEqual = tokenAnd.Right.ShouldBeOfType<Predicate.Equal>();
        codeEqual.Column.Column.ShouldBe("Code1");
        codeEqual.Value.Value.ShouldBe("8480-6");
    }

    [Fact]
    public void GivenASystemQualifiedQuantityComponent_WhenLowered_ThenThrows()
    {
        // Arrange — quantity identity is Task 5; retain throw for now
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
