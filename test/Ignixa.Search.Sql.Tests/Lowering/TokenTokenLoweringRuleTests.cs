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

public class TokenTokenLoweringRuleTests
{
    private static LeafContext ContextResolving(SearchParameterInfo compositeParameter, short searchParamId)
        => new(new SymbolTable(
            new Dictionary<string, short> { [compositeParameter.Url!.ToString()] = searchParamId },
            new Dictionary<string, short>()));

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
    public void GivenASystemQualifiedFirstComponent_WhenLowered_ThenThrowsRatherThanSilentlyIgnoringTheSystem()
    {
        // Arrange
        var composite = CompositeParameter();
        var components = new[]
        {
            TokenComponent("code", system: "http://loinc.org", tokenCode: "8480-6"),
            TokenComponent("value-concept", system: null, tokenCode: "high"),
        };

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            TokenTokenLoweringRule.Lower(composite, components, ContextResolving(composite, 301), 104));
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
