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
        var cte = StringLoweringRule.Lower(predicate, (StringSearchValue)predicate.Value, ContextResolving(parameter, 202), 103);

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
        var cte = StringLoweringRule.Lower(predicate, (StringSearchValue)predicate.Value, ContextResolving(parameter, 202), 103);

        // Assert
        var like = cte.Predicate.ShouldBeOfType<Predicate.Like>();
        like.Column.Column.ShouldBe("Text");
        like.Match.ShouldBe(LikeMatch.StartsWith);
        like.Collation.ShouldBe("Latin1_General_100_CI_AI");
        like.Value.Value.ShouldBe("Smith");
    }

    [Fact]
    public void GivenAContainsModifierWithAValueLongerThanInlineWidth_WhenLowered_ThenUsesLikeWithContainsMatchAgainstTextOverflow()
    {
        // Arrange -- :contains is only expressible correctly against TextOverflow (see class doc):
        // the search value itself must exceed the inline width so a stored value that could match it
        // is guaranteed to have overflowed too, and TextOverflow holds its true, whole value.
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var longValue = new string('m', 300);
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Contains), new StringSearchValue(longValue));

        // Act
        var cte = StringLoweringRule.Lower(predicate, (StringSearchValue)predicate.Value, ContextResolving(parameter, 202), 103);

        // Assert
        var like = cte.Predicate.ShouldBeOfType<Predicate.Like>();
        like.Column.Column.ShouldBe("TextOverflow");
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
        var cte = StringLoweringRule.Lower(predicate, (StringSearchValue)predicate.Value, ContextResolving(parameter, 202), 103);

        // Assert
        var like = cte.Predicate.ShouldBeOfType<Predicate.Like>();
        like.Column.Column.ShouldBe("TextOverflow");
        like.Match.ShouldBe(LikeMatch.StartsWith);
        like.Value.Value.ShouldBe(longValue);
    }

    [Fact]
    public void GivenAContainsModifierWithAValueWithinInlineWidth_WhenLowered_ThenThrowsBecauseOverflowedRowsCouldBeMissed()
    {
        // Arrange
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Contains), new StringSearchValue("mit"));

        // Act
        var act = () => StringLoweringRule.Lower(predicate, (StringSearchValue)predicate.Value, ContextResolving(parameter, 202), 103);

        // Assert
        Should.Throw<NotSupportedException>(act);
    }

    [Fact]
    public void GivenAnExactModifierWithAValueOfExactlyTheInlineWidth_WhenLowered_ThenThrowsBecauseOverflowedRowsCouldFalsePositiveMatch()
    {
        // Arrange
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var valueAtInlineWidth = new string('A', 256);
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Exact), new StringSearchValue(valueAtInlineWidth));

        // Act
        var act = () => StringLoweringRule.Lower(predicate, (StringSearchValue)predicate.Value, ContextResolving(parameter, 202), 103);

        // Assert
        Should.Throw<NotSupportedException>(act);
    }
}
