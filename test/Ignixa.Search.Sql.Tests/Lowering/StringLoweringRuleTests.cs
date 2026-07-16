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

public class StringLoweringRuleTests
{
    private static LeafContext ContextResolving(SearchParameterInfo parameter, short searchParamId)
        => new(new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = searchParamId },
            new Dictionary<string, short>()));

    [Fact]
    public void GivenAnExactModifier_WhenLowered_ThenComparesTextWithCaseSensitiveCollation()
    {
        // Arrange
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Exact), new StringSearchValue("Smith"));

        // Act
        var cte = StringLoweringRule.Lower(predicate, (StringSearchValue)predicate.Value, ContextResolving(parameter, 202));

        // Assert
        cte.SearchParamId.ShouldBe((short)202);
        var equal = cte.Predicate.ShouldBeOfType<Predicate.Equal>();
        equal.Column.Column.ShouldBe("Text");
        equal.Collation.ShouldBe("Latin1_General_100_CS_AS");
        equal.Value.Value.ShouldBe("Smith");
    }

    [Fact]
    public void GivenNoModifier_WhenLowered_ThenUsesLikeWithStartsWithMatchAndCaseInsensitiveCollation()
    {
        // Arrange
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));

        // Act
        var cte = StringLoweringRule.Lower(predicate, (StringSearchValue)predicate.Value, ContextResolving(parameter, 202));

        // Assert
        var like = cte.Predicate.ShouldBeOfType<Predicate.Like>();
        like.Column.Column.ShouldBe("Text");
        like.Match.ShouldBe(LikeMatch.StartsWith);
        like.Collation.ShouldBe("Latin1_General_100_CI_AI");
        like.Value.Value.ShouldBe("Smith");
    }

    [Fact]
    public void GivenAContainsModifier_WhenLowered_ThenUsesLikeWithContainsMatch()
    {
        // Arrange
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Contains), new StringSearchValue("mit"));

        // Act
        var cte = StringLoweringRule.Lower(predicate, (StringSearchValue)predicate.Value, ContextResolving(parameter, 202));

        // Assert
        var like = cte.Predicate.ShouldBeOfType<Predicate.Like>();
        like.Match.ShouldBe(LikeMatch.Contains);
    }

    [Fact]
    public void GivenAValueLongerThan256Chars_WhenLowered_ThenComparesTextOverflowInstead()
    {
        // Arrange
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var longValue = new string('A', 300);
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new StringSearchValue(longValue));

        // Act
        var cte = StringLoweringRule.Lower(predicate, (StringSearchValue)predicate.Value, ContextResolving(parameter, 202));

        // Assert
        var like = cte.Predicate.ShouldBeOfType<Predicate.Like>();
        like.Column.Column.ShouldBe("TextOverflow");
        like.Match.ShouldBe(LikeMatch.StartsWith);
        like.Value.Value.ShouldBe(longValue);
    }
}
