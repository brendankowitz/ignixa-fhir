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

public class TokenTokenLoweringRuleTests
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
        => new("code-value-concept", "code-value-concept", SearchParamType.Composite,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-code-value-concept"));

    private static SearchParameterInfo ComponentParameter(string code)
        => new(code, code, SearchParamType.Token, new Uri($"http://hl7.org/fhir/SearchParameter/Observation-{code}"));

    private static SearchParameterPredicateExpression TokenComponent(string code, string? system, string? tokenCode)
        => new(ComponentParameter(code), SearchComparator.Eq, modifier: null, new TokenSearchValue(system, tokenCode, text: null));

    [Fact]
    public void GivenTwoCodeOnlyTokenComponents_WhenLowered_ThenComparesBothCodeColumnsOnTheCompositeTable()
    {
        // Arrange
        var composite = CompositeParameter();
        var components = new[]
        {
            TokenComponent("code", system: null, tokenCode: "8480-6"),
            TokenComponent("value-concept", system: null, tokenCode: "high"),
        };

        // Act
        var cte = TokenTokenLoweringRule.Lower(composite, components, ContextResolving(composite, 301), 104);

        // Assert
        cte.SearchParamId.ShouldBe((short)301);
        cte.ResourceTypeId.ShouldBe((short)104);
        cte.Table.TableName.ShouldBe("TokenTokenCompositeSearchParam");
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var left = and.Left.ShouldBeOfType<Predicate.Equal>();
        left.Column.Column.ShouldBe("Code1");
        left.Value.Value.ShouldBe("8480-6");
        var right = and.Right.ShouldBeOfType<Predicate.Equal>();
        right.Column.Column.ShouldBe("Code2");
        right.Value.Value.ShouldBe("high");
    }

    [Fact]
    public void GivenASystemQualifiedFirstComponent_WhenLowered_ThenComparesSystemId1AndCode1()
    {
        // Arrange — system|code on slot 1
        var composite = CompositeParameter();
        var systemIds = new Dictionary<string, int?> { ["http://loinc.org"] = 42 };
        var components = new[]
        {
            TokenComponent("code", system: "http://loinc.org", tokenCode: "8480-6"),
            TokenComponent("value-concept", system: null, tokenCode: "high"),
        };

        // Act
        var cte = TokenTokenLoweringRule.Lower(composite, components, ContextResolving(composite, 301, systemIds), 104);

        // Assert
        var outer = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var slot1 = outer.Left.ShouldBeOfType<Predicate.And>();
        var systemEqual = slot1.Left.ShouldBeOfType<Predicate.Equal>();
        systemEqual.Column.Column.ShouldBe("SystemId1");
        systemEqual.Value.Value.ShouldBe(42);
        var codeEqual = slot1.Right.ShouldBeOfType<Predicate.Equal>();
        codeEqual.Column.Column.ShouldBe("Code1");
        codeEqual.Value.Value.ShouldBe("8480-6");
        var slot2 = outer.Right.ShouldBeOfType<Predicate.Equal>();
        slot2.Column.Column.ShouldBe("Code2");
        slot2.Value.Value.ShouldBe("high");
    }

    [Fact]
    public void GivenASystemQualifiedSecondComponent_WhenLowered_ThenComparesSystemId2AndCode2()
    {
        // Arrange — system|code on slot 2
        var composite = CompositeParameter();
        var systemIds = new Dictionary<string, int?> { ["http://snomed.info/sct"] = 99 };
        var components = new[]
        {
            TokenComponent("code", system: null, tokenCode: "8480-6"),
            TokenComponent("value-concept", system: "http://snomed.info/sct", tokenCode: "high"),
        };

        // Act
        var cte = TokenTokenLoweringRule.Lower(composite, components, ContextResolving(composite, 301, systemIds), 104);

        // Assert
        var outer = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var slot1 = outer.Left.ShouldBeOfType<Predicate.Equal>();
        slot1.Column.Column.ShouldBe("Code1");
        slot1.Value.Value.ShouldBe("8480-6");
        var slot2 = outer.Right.ShouldBeOfType<Predicate.And>();
        var systemEqual = slot2.Left.ShouldBeOfType<Predicate.Equal>();
        systemEqual.Column.Column.ShouldBe("SystemId2");
        systemEqual.Value.Value.ShouldBe(99);
        var codeEqual = slot2.Right.ShouldBeOfType<Predicate.Equal>();
        codeEqual.Column.Column.ShouldBe("Code2");
        codeEqual.Value.Value.ShouldBe("high");
    }

    [Fact]
    public void GivenATextOnlySecondComponent_WhenLowered_ThenThrowsRatherThanProducingAnUnconstrainedMatch()
    {
        // Arrange
        var composite = CompositeParameter();
        var valueParam = ComponentParameter("value-concept");
        var components = new[]
        {
            TokenComponent("code", system: null, tokenCode: "8480-6"),
            new SearchParameterPredicateExpression(valueParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: null, text: "High")),
        };

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            TokenTokenLoweringRule.Lower(composite, components, ContextResolving(composite, 301), 104));
    }
}
