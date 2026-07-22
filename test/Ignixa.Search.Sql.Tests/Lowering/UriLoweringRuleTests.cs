using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Lowering.Leaf;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class UriLoweringRuleTests
{
    private static LeafContext ContextResolving(SearchParameterInfo parameter, short searchParamId)
        => new(new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = searchParamId },
            new Dictionary<string, short>()));

    private static EmittedSql EmitSql(CteDefinition.ParamSource cte)
        => SqlBuilder.Run(new QueryPlan([cte], new CteRef(0)));

    [Fact]
    public void GivenAPlainUriValue_WhenLowered_ThenComparesTheUriColumnWithBinaryCollation()
    {
        // Arrange
        var parameter = new SearchParameterInfo("url", "url", SearchParamType.Uri, new Uri("http://hl7.org/fhir/SearchParameter/ValueSet-url"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new UriSearchValue("http://example.org/fhir/ValueSet/1", separateCanonicalComponents: false));

        // Act
        var cte = UriLoweringRule.Lower(predicate, (UriSearchValue)predicate.Value, ContextResolving(parameter, 88), 105);

        // Assert
        cte.SearchParamId.ShouldBe((short)88);
        cte.ResourceTypeId.ShouldBe((short)105);
        var equal = cte.Predicate.ShouldBeOfType<Predicate.Equal>();
        equal.Column.Column.ShouldBe("Uri");
        equal.Value.Value.ShouldBe("http://example.org/fhir/ValueSet/1");
        equal.Collation.ShouldBe("Latin1_General_100_BIN2");
    }

    [Fact]
    public void GivenABelowModifier_WhenLowered_ThenProducesStartsWithLikeWithBinaryCollation()
    {
        // Arrange
        var parameter = new SearchParameterInfo("url", "url", SearchParamType.Uri, new Uri("http://hl7.org/fhir/SearchParameter/ValueSet-url"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Below), new UriSearchValue("http://example.org/fhir/ValueSet", separateCanonicalComponents: false));

        // Act
        var cte = UriLoweringRule.Lower(predicate, (UriSearchValue)predicate.Value, ContextResolving(parameter, 88), 105);

        // Assert
        var like = cte.Predicate.ShouldBeOfType<Predicate.Like>();
        like.Column.Column.ShouldBe("Uri");
        like.Match.ShouldBe(LikeMatch.StartsWith);
        like.Collation.ShouldBe("Latin1_General_100_BIN2");
        like.Value.Value.ShouldBe("http://example.org/fhir/ValueSet");
    }

    [Fact]
    public void GivenAnAboveModifier_WhenLowered_ThenProducesPrefixOfParameterWithBinaryCollation()
    {
        // Arrange
        var parameter = new SearchParameterInfo("url", "url", SearchParamType.Uri, new Uri("http://hl7.org/fhir/SearchParameter/ValueSet-url"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Above), new UriSearchValue("http://example.org/fhir", separateCanonicalComponents: false));

        // Act
        var cte = UriLoweringRule.Lower(predicate, (UriSearchValue)predicate.Value, ContextResolving(parameter, 88), 105);

        // Assert
        var prefixOf = cte.Predicate.ShouldBeOfType<Predicate.PrefixOfParameter>();
        prefixOf.Column.Column.ShouldBe("Uri");
        prefixOf.Value.Value.ShouldBe("http://example.org/fhir");
        prefixOf.Collation.ShouldBe("Latin1_General_100_BIN2");
    }

    [Fact]
    public void GivenABelowModifierAndPlainUri_WhenEmitted_ThenSqlUsesLikeWithBinaryCollationAndEscapeClause()
    {
        // Arrange
        var parameter = new SearchParameterInfo("url", "url", SearchParamType.Uri, new Uri("http://hl7.org/fhir/SearchParameter/ValueSet-url"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Below), new UriSearchValue("http://example.org/fhir/ValueSet", separateCanonicalComponents: false));

        // Act
        var cte = UriLoweringRule.Lower(predicate, (UriSearchValue)predicate.Value, ContextResolving(parameter, 88), 105);
        var emitted = EmitSql(cte);

        // Assert
        emitted.Sql.ShouldContain("Uri COLLATE Latin1_General_100_BIN2 LIKE @p0 ESCAPE '\\'");
        emitted.Parameters[0].Value.ShouldBe("http://example.org/fhir/ValueSet%");
    }

    [Fact]
    public void GivenAnAboveModifierAndPlainUri_WhenEmitted_ThenSqlUsesLeftLenEqualityWithBinaryCollation()
    {
        // Arrange
        var parameter = new SearchParameterInfo("url", "url", SearchParamType.Uri, new Uri("http://hl7.org/fhir/SearchParameter/ValueSet-url"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Above), new UriSearchValue("http://example.org/fhir/Patient/123", separateCanonicalComponents: false));

        // Act
        var cte = UriLoweringRule.Lower(predicate, (UriSearchValue)predicate.Value, ContextResolving(parameter, 88), 105);
        var emitted = EmitSql(cte);

        // Assert
        emitted.Sql.ShouldContain("LEFT(@p0, LEN(Uri)) COLLATE Latin1_General_100_BIN2 = Uri");
        emitted.Parameters[0].Value.ShouldBe("http://example.org/fhir/Patient/123");
    }

    [Fact]
    public void GivenDifferingCaseUri_WhenLoweredWithNoModifier_ThenPredicateUsesExactBinaryCollation()
    {
        // Arrange
        var parameter = new SearchParameterInfo("url", "url", SearchParamType.Uri, new Uri("http://hl7.org/fhir/SearchParameter/ValueSet-url"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new UriSearchValue("HTTP://EXAMPLE.ORG/fhir/ValueSet/1", separateCanonicalComponents: false));

        // Act
        var cte = UriLoweringRule.Lower(predicate, (UriSearchValue)predicate.Value, ContextResolving(parameter, 88), 105);

        // Assert
        var equal = cte.Predicate.ShouldBeOfType<Predicate.Equal>();
        equal.Collation.ShouldBe("Latin1_General_100_BIN2");
        equal.Value.Value.ShouldBe("HTTP://EXAMPLE.ORG/fhir/ValueSet/1");
    }

    [Fact]
    public void GivenANearPrefixUri_WhenLoweredWithBelowModifier_ThenPredicateBindsRawValueForLexicalPrefixMatch()
    {
        // Arrange
        var parameter = new SearchParameterInfo("url", "url", SearchParamType.Uri, new Uri("http://hl7.org/fhir/SearchParameter/ValueSet-url"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Below), new UriSearchValue("http://example.org/fhir/ValueSet/1b", separateCanonicalComponents: false));

        // Act
        var cte = UriLoweringRule.Lower(predicate, (UriSearchValue)predicate.Value, ContextResolving(parameter, 88), 105);

        // Assert
        var like = cte.Predicate.ShouldBeOfType<Predicate.Like>();
        like.Value.Value.ShouldBe("http://example.org/fhir/ValueSet/1b");
        like.Match.ShouldBe(LikeMatch.StartsWith);
        like.Collation.ShouldBe("Latin1_General_100_BIN2");
    }

    [Fact]
    public void GivenABelowModifierWithSpecialCharsInUri_WhenEmitted_ThenParameterIsEscapedExactlyOnce()
    {
        // Arrange — URI contains %, _, [, \
        var parameter = new SearchParameterInfo("url", "url", SearchParamType.Uri, new Uri("http://hl7.org/fhir/SearchParameter/ValueSet-url"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Below), new UriSearchValue("http://example.org/fhir/ValueSet/a%b_c[d\\e", separateCanonicalComponents: false));

        // Act
        var cte = UriLoweringRule.Lower(predicate, (UriSearchValue)predicate.Value, ContextResolving(parameter, 88), 105);

        // Assert — raw value in the AST (pre-escape)
        var like = cte.Predicate.ShouldBeOfType<Predicate.Like>();
        like.Value.Value.ShouldBe("http://example.org/fhir/ValueSet/a%b_c[d\\e");

        // Assert — emitted parameter is escaped by SqlBuilder (escaped + trailing %)
        var emitted = EmitSql(cte);
        emitted.Parameters[0].Value.ShouldBe("http://example.org/fhir/ValueSet/a\\%b\\_c\\[d\\\\e%");
        emitted.Sql.ShouldContain("LIKE @p0 ESCAPE '\\'");
    }

    [Fact]
    public void GivenAnAboveModifierWithSpecialCharsInUri_WhenEmitted_ThenParameterIsRawWithNoEscaping()
    {
        // Arrange — URI contains %, _, [, \
        var parameter = new SearchParameterInfo("url", "url", SearchParamType.Uri, new Uri("http://hl7.org/fhir/SearchParameter/ValueSet-url"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Above), new UriSearchValue("http://example.org/fhir/ValueSet/a%b_c[d\\e", separateCanonicalComponents: false));

        // Act
        var cte = UriLoweringRule.Lower(predicate, (UriSearchValue)predicate.Value, ContextResolving(parameter, 88), 105);

        // Assert — raw value in the AST (unchanged)
        var prefixOf = cte.Predicate.ShouldBeOfType<Predicate.PrefixOfParameter>();
        prefixOf.Value.Value.ShouldBe("http://example.org/fhir/ValueSet/a%b_c[d\\e");

        // Assert — emitted parameter is raw (no escaping for PrefixOfParameter)
        var emitted = EmitSql(cte);
        emitted.Parameters[0].Value.ShouldBe("http://example.org/fhir/ValueSet/a%b_c[d\\e");
        emitted.Sql.ShouldContain("LEFT(@p0, LEN(Uri))");
    }

    [Fact]
    public void GivenAnUnsupportedModifier_WhenLowered_ThenThrowsNotSupportedExceptionNamingTheModifier()
    {
        // Arrange
        var parameter = new SearchParameterInfo("url", "url", SearchParamType.Uri, new Uri("http://hl7.org/fhir/SearchParameter/ValueSet-url"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Exact), new UriSearchValue("http://example.org/fhir", separateCanonicalComponents: false));

        // Act & Assert
        var ex = Should.Throw<NotSupportedException>(() =>
            UriLoweringRule.Lower(predicate, (UriSearchValue)predicate.Value, ContextResolving(parameter, 88), 105));
        ex.Message.ShouldContain("Exact");
    }
}
