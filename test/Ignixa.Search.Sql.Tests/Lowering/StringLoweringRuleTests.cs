using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Lowering.Leaf;
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
    public void GivenAnExactModifier_WhenLowered_ThenAppliesIsNullGuardAndComparesTextWithCaseSensitiveCollation()
    {
        // Arrange — "Smith" is well within the 256-char inline width; the IsNull(TextOverflow) guard
        // prevents an overflowed row whose 256-char prefix happens to equal the search value from
        // false-positive matching.
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Exact), new StringSearchValue("Smith"));

        // Act
        var cte = StringLoweringRule.Lower(predicate, (StringSearchValue)predicate.Value, ContextResolving(parameter, 202), 103);

        // Assert
        cte.SearchParamId.ShouldBe((short)202);
        cte.ResourceTypeId.ShouldBe((short)103);
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var isNull = and.Left.ShouldBeOfType<Predicate.IsNull>();
        isNull.Column.Table.ShouldBe("StringSearchParam");
        isNull.Column.Column.ShouldBe("TextOverflow");
        var equal = and.Right.ShouldBeOfType<Predicate.Equal>();
        equal.Column.Table.ShouldBe("StringSearchParam");
        equal.Column.Column.ShouldBe("Text");
        equal.Collation.ShouldBe("Latin1_General_100_CS_AS");
        equal.Value.Value.ShouldBe("Smith");
    }

    [Fact]
    public void GivenAnExactModifierWithAValueAt255Chars_WhenLowered_ThenAppliesIsNullGuardAndComparesText()
    {
        // Arrange — 255 chars is strictly less than the 256-char inline width; exact must still guard
        // against rows that overflowed and whose Text prefix happens to match.
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var value255 = new string('A', 255);
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Exact), new StringSearchValue(value255));

        // Act
        var cte = StringLoweringRule.Lower(predicate, (StringSearchValue)predicate.Value, ContextResolving(parameter, 202), 103);

        // Assert — And(IsNull(TextOverflow), Equal(Text, value, CS_AS))
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var isNull = and.Left.ShouldBeOfType<Predicate.IsNull>();
        isNull.Column.Table.ShouldBe("StringSearchParam");
        isNull.Column.Column.ShouldBe("TextOverflow");
        var equal = and.Right.ShouldBeOfType<Predicate.Equal>();
        equal.Column.Table.ShouldBe("StringSearchParam");
        equal.Column.Column.ShouldBe("Text");
        equal.Collation.ShouldBe("Latin1_General_100_CS_AS");
        equal.Value.Value.ShouldBe(value255);
    }

    [Fact]
    public void GivenAnExactModifierWithAValueAt256Chars_WhenLowered_ThenAppliesIsNullGuardAndComparesText()
    {
        // Arrange — 256 chars equals the inline width; without the IsNull guard an overflowed row's
        // truncated 256-char Text prefix would false-positive match this search value.
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var value256 = new string('A', 256);
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Exact), new StringSearchValue(value256));

        // Act
        var cte = StringLoweringRule.Lower(predicate, (StringSearchValue)predicate.Value, ContextResolving(parameter, 202), 103);

        // Assert — And(IsNull(TextOverflow), Equal(Text, value, CS_AS))
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var isNull = and.Left.ShouldBeOfType<Predicate.IsNull>();
        isNull.Column.Table.ShouldBe("StringSearchParam");
        isNull.Column.Column.ShouldBe("TextOverflow");
        var equal = and.Right.ShouldBeOfType<Predicate.Equal>();
        equal.Column.Table.ShouldBe("StringSearchParam");
        equal.Column.Column.ShouldBe("Text");
        equal.Collation.ShouldBe("Latin1_General_100_CS_AS");
        equal.Value.Value.ShouldBe(value256);
    }

    [Fact]
    public void GivenAnExactModifierWithAValueAt257Chars_WhenLowered_ThenComparesTextOverflowWithCaseSensitiveCollation()
    {
        // Arrange — 257 chars exceeds the inline width; the value can only exist in TextOverflow,
        // which holds the complete stored value, so a direct equality comparison is correct.
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var value257 = new string('A', 257);
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Exact), new StringSearchValue(value257));

        // Act
        var cte = StringLoweringRule.Lower(predicate, (StringSearchValue)predicate.Value, ContextResolving(parameter, 202), 103);

        // Assert — Equal(TextOverflow, value, CS_AS) only; no IsNull guard needed
        var equal = cte.Predicate.ShouldBeOfType<Predicate.Equal>();
        equal.Column.Table.ShouldBe("StringSearchParam");
        equal.Column.Column.ShouldBe("TextOverflow");
        equal.Collation.ShouldBe("Latin1_General_100_CS_AS");
        equal.Value.Value.ShouldBe(value257);
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
        cte.ResourceTypeId.ShouldBe((short)103);
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
        cte.ResourceTypeId.ShouldBe((short)103);
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
        cte.ResourceTypeId.ShouldBe((short)103);
        var like = cte.Predicate.ShouldBeOfType<Predicate.Like>();
        like.Column.Column.ShouldBe("TextOverflow");
        like.Match.ShouldBe(LikeMatch.StartsWith);
        like.Value.Value.ShouldBe(longValue);
    }

    [Fact]
    public void GivenAContainsModifierWithAValueWithinInlineWidth_WhenLowered_ThenProducesOrOfIsNullGuardedTextAndOverflowLike()
    {
        // Arrange — "mit" is well within the 256-char inline width; the Or(And(IsNull(TextOverflow),
        // Like(Text, …, Contains)), Like(TextOverflow, …, Contains)) shape ensures that both non-
        // overflowed rows (via Text) and overflowed rows (via the complete TextOverflow) are searched.
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Contains), new StringSearchValue("mit"));

        // Act
        var cte = StringLoweringRule.Lower(predicate, (StringSearchValue)predicate.Value, ContextResolving(parameter, 202), 103);

        // Assert — Or(And(IsNull(TextOverflow), Like(Text, …, Contains, CI_AI)), Like(TextOverflow, …, Contains, CI_AI))
        cte.SearchParamId.ShouldBe((short)202);
        cte.ResourceTypeId.ShouldBe((short)103);
        var or = cte.Predicate.ShouldBeOfType<Predicate.Or>();

        var and = or.Left.ShouldBeOfType<Predicate.And>();
        var isNull = and.Left.ShouldBeOfType<Predicate.IsNull>();
        isNull.Column.Table.ShouldBe("StringSearchParam");
        isNull.Column.Column.ShouldBe("TextOverflow");
        var textLike = and.Right.ShouldBeOfType<Predicate.Like>();
        textLike.Column.Table.ShouldBe("StringSearchParam");
        textLike.Column.Column.ShouldBe("Text");
        textLike.Match.ShouldBe(LikeMatch.Contains);
        textLike.Collation.ShouldBe("Latin1_General_100_CI_AI");
        textLike.Value.Value.ShouldBe("mit");

        var overflowLike = or.Right.ShouldBeOfType<Predicate.Like>();
        overflowLike.Column.Table.ShouldBe("StringSearchParam");
        overflowLike.Column.Column.ShouldBe("TextOverflow");
        overflowLike.Match.ShouldBe(LikeMatch.Contains);
        overflowLike.Collation.ShouldBe("Latin1_General_100_CI_AI");
        overflowLike.Value.Value.ShouldBe("mit");
    }

    [Fact]
    public void GivenAContainsModifierWithAValueAt255Chars_WhenLowered_ThenProducesGuardedOrShape()
    {
        // Arrange — 255 chars is within the 256-char inline width, so the dual-column Or shape applies.
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var value255 = new string('x', 255);
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Contains), new StringSearchValue(value255));

        // Act
        var cte = StringLoweringRule.Lower(predicate, (StringSearchValue)predicate.Value, ContextResolving(parameter, 202), 103);

        // Assert — Or(And(IsNull(TextOverflow), Like(Text)), Like(TextOverflow))
        var or = cte.Predicate.ShouldBeOfType<Predicate.Or>();
        var and = or.Left.ShouldBeOfType<Predicate.And>();
        and.Left.ShouldBeOfType<Predicate.IsNull>().Column.Column.ShouldBe("TextOverflow");
        var textLike = and.Right.ShouldBeOfType<Predicate.Like>();
        textLike.Column.Column.ShouldBe("Text");
        textLike.Match.ShouldBe(LikeMatch.Contains);
        textLike.Value.Value.ShouldBe(value255);

        var overflowLike = or.Right.ShouldBeOfType<Predicate.Like>();
        overflowLike.Column.Column.ShouldBe("TextOverflow");
        overflowLike.Match.ShouldBe(LikeMatch.Contains);
        overflowLike.Value.Value.ShouldBe(value255);
    }

    [Fact]
    public void GivenAContainsModifierWithAValueAt256Chars_WhenLowered_ThenProducesGuardedOrShape()
    {
        // Arrange — 256 chars equals the inline width; the dual-column Or shape still applies.
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var value256 = new string('x', 256);
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Contains), new StringSearchValue(value256));

        // Act
        var cte = StringLoweringRule.Lower(predicate, (StringSearchValue)predicate.Value, ContextResolving(parameter, 202), 103);

        // Assert — Or(And(IsNull(TextOverflow), Like(Text)), Like(TextOverflow))
        var or = cte.Predicate.ShouldBeOfType<Predicate.Or>();
        var and = or.Left.ShouldBeOfType<Predicate.And>();
        and.Left.ShouldBeOfType<Predicate.IsNull>().Column.Column.ShouldBe("TextOverflow");
        var textLike = and.Right.ShouldBeOfType<Predicate.Like>();
        textLike.Column.Column.ShouldBe("Text");
        textLike.Match.ShouldBe(LikeMatch.Contains);
        textLike.Value.Value.ShouldBe(value256);

        var overflowLike = or.Right.ShouldBeOfType<Predicate.Like>();
        overflowLike.Column.Column.ShouldBe("TextOverflow");
        overflowLike.Match.ShouldBe(LikeMatch.Contains);
        overflowLike.Value.Value.ShouldBe(value256);
    }

    [Fact]
    public void GivenAContainsModifierWithAValueAt257Chars_WhenLowered_ThenUsesOnlyTextOverflowLike()
    {
        // Arrange — 257 chars exceeds the 256-char inline width; the value can only reside in
        // TextOverflow, so a single Like(TextOverflow, …, Contains) suffices.
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var value257 = new string('x', 257);
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Contains), new StringSearchValue(value257));

        // Act
        var cte = StringLoweringRule.Lower(predicate, (StringSearchValue)predicate.Value, ContextResolving(parameter, 202), 103);

        // Assert — single Like(TextOverflow, value, Contains, CI_AI)
        var like = cte.Predicate.ShouldBeOfType<Predicate.Like>();
        like.Column.Table.ShouldBe("StringSearchParam");
        like.Column.Column.ShouldBe("TextOverflow");
        like.Match.ShouldBe(LikeMatch.Contains);
        like.Collation.ShouldBe("Latin1_General_100_CI_AI");
        like.Value.Value.ShouldBe(value257);
    }

    [Fact]
    public void GivenAContainsModifierWithSpecialLikeMetacharacters_WhenLowered_ThenRawValueIsPreservedInAstAndEscapedOnlyAtEmit()
    {
        // Arrange — values containing %, _, [, \ must pass through the AST unescaped; escaping happens
        // in SqlBuilder.EscapeLike at emit time.
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var specialValue = @"%_[\";
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Contains), new StringSearchValue(specialValue));

        // Act
        var cte = StringLoweringRule.Lower(predicate, (StringSearchValue)predicate.Value, ContextResolving(parameter, 202), 103);

        // Assert — raw value preserved in both Like nodes
        var or = cte.Predicate.ShouldBeOfType<Predicate.Or>();
        var textLike = or.Left.ShouldBeOfType<Predicate.And>().Right.ShouldBeOfType<Predicate.Like>();
        textLike.Value.Value.ShouldBe(specialValue);
        var overflowLike = or.Right.ShouldBeOfType<Predicate.Like>();
        overflowLike.Value.Value.ShouldBe(specialValue);
    }

    [Fact]
    public void GivenAContainsModifierWithCaseAndAccentVariant_WhenLowered_ThenRawValueIsPreservedAndBothLikesUseCaseInsensitiveCollation()
    {
        // Arrange — "Müller" contains accented and mixed-case chars; the raw value must pass
        // through unchanged, and the collation CI_AI handles case/accent normalization at the DB layer.
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Contains), new StringSearchValue("Müller"));

        // Act
        var cte = StringLoweringRule.Lower(predicate, (StringSearchValue)predicate.Value, ContextResolving(parameter, 202), 103);

        // Assert — raw value preserved, CI_AI collation on both Like nodes
        var or = cte.Predicate.ShouldBeOfType<Predicate.Or>();
        var textLike = or.Left.ShouldBeOfType<Predicate.And>().Right.ShouldBeOfType<Predicate.Like>();
        textLike.Value.Value.ShouldBe("Müller");
        textLike.Collation.ShouldBe("Latin1_General_100_CI_AI");
        var overflowLike = or.Right.ShouldBeOfType<Predicate.Like>();
        overflowLike.Value.Value.ShouldBe("Müller");
        overflowLike.Collation.ShouldBe("Latin1_General_100_CI_AI");
    }

}
