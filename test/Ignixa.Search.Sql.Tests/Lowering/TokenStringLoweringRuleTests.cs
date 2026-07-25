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

public class TokenStringLoweringRuleTests
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
        => new("code-value-string", "code-value-string", SearchParamType.Composite,
            new Uri("http://example.org/fhir/SearchParameter/Observation-code-value-string"));

    private static SearchParameterInfo ComponentParameter(string code)
        => new(code, code, SearchParamType.Token, new Uri($"http://example.org/fhir/SearchParameter/Observation-{code}"));

    [Fact]
    public void GivenATokenComponentAndAShortStringComponent_WhenLowered_ThenComparesCode1AndText2WithStartsWith()
    {
        // Arrange
        var composite = CompositeParameter();
        var tokenParam = ComponentParameter("code");
        var stringParam = ComponentParameter("value-string");
        var components = new SearchParameterPredicateExpression[]
        {
            new(tokenParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null)),
            new(stringParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Elevated")),
        };

        // Act
        var cte = TokenStringLoweringRule.Lower(composite, components, ContextResolving(composite, 401), 104);

        // Assert
        cte.SearchParamId.ShouldBe((short)401);
        cte.ResourceTypeId.ShouldBe((short)104);
        cte.Table.TableName.ShouldBe("TokenStringCompositeSearchParam");
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var tokenPredicate = and.Left.ShouldBeOfType<Predicate.Equal>();
        tokenPredicate.Column.Column.ShouldBe("Code1");
        var stringPredicate = and.Right.ShouldBeOfType<Predicate.Like>();
        stringPredicate.Column.Column.ShouldBe("Text2");
        stringPredicate.Match.ShouldBe(LikeMatch.StartsWith);
        stringPredicate.Collation.ShouldBe("Latin1_General_CI_AI");
        stringPredicate.Value.Value.ShouldBe("Elevated");
    }

    [Fact]
    public void GivenAStringComponentLongerThanTheInlineWidth_WhenLowered_ThenComparesTextOverflow2()
    {
        // Arrange
        var composite = CompositeParameter();
        var tokenParam = ComponentParameter("code");
        var stringParam = ComponentParameter("value-string");
        var longValue = new string('x', 300);
        var components = new SearchParameterPredicateExpression[]
        {
            new(tokenParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null)),
            new(stringParam, SearchComparator.Eq, modifier: null, new StringSearchValue(longValue)),
        };

        // Act
        var cte = TokenStringLoweringRule.Lower(composite, components, ContextResolving(composite, 401), 104);

        // Assert
        cte.ResourceTypeId.ShouldBe((short)104);
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var stringPredicate = and.Right.ShouldBeOfType<Predicate.Like>();
        stringPredicate.Column.Column.ShouldBe("TextOverflow2");
    }

    [Fact]
    public void GivenASystemQualifiedTokenComponent_WhenLowered_ThenComparesSystemId1AndCode1()
    {
        // Arrange — system|code on the token slot
        var composite = CompositeParameter();
        var tokenParam = ComponentParameter("code");
        var stringParam = ComponentParameter("value-string");
        var systemIds = new Dictionary<string, int?> { ["http://loinc.org"] = 42 };
        var components = new SearchParameterPredicateExpression[]
        {
            new(tokenParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: "http://loinc.org", code: "8480-6", text: null)),
            new(stringParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Elevated")),
        };

        // Act
        var cte = TokenStringLoweringRule.Lower(composite, components, ContextResolving(composite, 401, systemIds), 104);

        // Assert
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var tokenAnd = and.Left.ShouldBeOfType<Predicate.And>();
        var systemEqual = tokenAnd.Left.ShouldBeOfType<Predicate.Equal>();
        systemEqual.Column.Column.ShouldBe("SystemId1");
        systemEqual.Value.Value.ShouldBe(42);
        var codeEqual = tokenAnd.Right.ShouldBeOfType<Predicate.Equal>();
        codeEqual.Column.Column.ShouldBe("Code1");
        codeEqual.Value.Value.ShouldBe("8480-6");
        and.Right.ShouldBeOfType<Predicate.Like>();
    }
}
