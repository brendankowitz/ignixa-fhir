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
    private static readonly SearchParameterInfo UrlParameter =
        new("url", "url", SearchParamType.Uri, new Uri("http://hl7.org/fhir/SearchParameter/ValueSet-url"));

    private static LeafContext ContextResolving(SearchParameterInfo parameter, short searchParamId)
        => new(new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = searchParamId },
            new Dictionary<string, short>()));

    private static EmittedSql EmitSql(CteDefinition.ParamSource cte)
        => SqlBuilder.Run(new QueryPlan([cte], new CteRef(0)));

    private static CteDefinition.ParamSource Lower(string uri, SearchModifierCode? modifier = null)
    {
        var predicate = new SearchParameterPredicateExpression(
            UrlParameter,
            SearchComparator.Eq,
            modifier is { } code ? new SearchModifier(code) : null,
            new UriSearchValue(uri, separateCanonicalComponents: false));

        return UriLoweringRule.Lower(predicate, (UriSearchValue)predicate.Value, ContextResolving(UrlParameter, 88), 105);
    }

    [Fact]
    public void GivenAPlainUriValue_WhenLowered_ThenComparesTheUriColumnWithoutACollationOverride()
    {
        // Act
        var cte = Lower("http://example.org/fhir/ValueSet/1");

        // Assert -- no COLLATE override: the column is already CS_AS, and forcing BIN2 on the column side
        // made the predicate incompatible with the index key ordering.
        cte.SearchParamId.ShouldBe((short)88);
        cte.ResourceTypeId.ShouldBe((short)105);
        var equal = cte.Predicate.ShouldBeOfType<Predicate.Equal>();
        equal.Column.Column.ShouldBe("Uri");
        equal.Value.Value.ShouldBe("http://example.org/fhir/ValueSet/1");
        equal.Collation.ShouldBeNull();
    }

    [Fact]
    public void GivenABelowModifier_WhenLowered_ThenMatchesSelfOrAnyDescendantAtASegmentBoundary()
    {
        // Act
        var cte = Lower("http://example.org/fhir/ValueSet", SearchModifierCode.Below);

        // Assert -- self, OR anything under the "/" boundary.
        var or = cte.Predicate.ShouldBeOfType<Predicate.Or>();

        var self = or.Left.ShouldBeOfType<Predicate.Equal>();
        self.Column.Column.ShouldBe("Uri");
        self.Value.Value.ShouldBe("http://example.org/fhir/ValueSet");

        var descendants = or.Right.ShouldBeOfType<Predicate.Like>();
        descendants.Column.Column.ShouldBe("Uri");
        descendants.Match.ShouldBe(LikeMatch.StartsWith);
        descendants.Value.Value.ShouldBe("http://example.org/fhir/ValueSet/");
        descendants.Collation.ShouldBeNull();
    }

    [Fact]
    public void GivenABelowModifier_WhenTheStoredValueIsASamePrefixSibling_ThenTheEmittedPatternCannotMatchIt()
    {
        // Arrange -- the exact false positive a bare lexical prefix produces:
        // url:below=http://acme.org/fhir/ValueSet must not match a stored .../ValueSetOther.
        var cte = Lower("http://acme.org/fhir/ValueSet", SearchModifierCode.Below);

        // Act
        var emitted = EmitSql(cte);

        // Assert -- the LIKE pattern carries the separator, so "ValueSetOther" cannot satisfy it, while
        // the equality arm still admits the exact value itself.
        var patterns = emitted.Parameters.Select(p => p.Value).ToArray();
        patterns.ShouldContain("http://acme.org/fhir/ValueSet");
        patterns.ShouldContain("http://acme.org/fhir/ValueSet/%");
        patterns.ShouldNotContain("http://acme.org/fhir/ValueSet%");
    }

    [Fact]
    public void GivenABelowModifierAndATrailingSlash_WhenLowered_ThenTheSeparatorIsNotDoubled()
    {
        // Act
        var cte = Lower("http://example.org/fhir/ValueSet/", SearchModifierCode.Below);

        // Assert
        var or = cte.Predicate.ShouldBeOfType<Predicate.Or>();
        or.Right.ShouldBeOfType<Predicate.Like>().Value.Value.ShouldBe("http://example.org/fhir/ValueSet/");
    }

    [Fact]
    public void GivenAnAboveModifier_WhenLowered_ThenBindsTheValueWithASegmentSeparatorAppended()
    {
        // Act
        var cte = Lower("http://example.org/fhir", SearchModifierCode.Above);

        // Assert -- LEFT(@p, LEN(Uri)) = Uri. The appended separator makes the parameter one character
        // longer than an exact-matching stored value, so the exact match still succeeds while a
        // same-prefix sibling fails character-for-character.
        var prefixOf = cte.Predicate.ShouldBeOfType<Predicate.PrefixOfParameter>();
        prefixOf.Column.Column.ShouldBe("Uri");
        prefixOf.Value.Value.ShouldBe("http://example.org/fhir/");
        prefixOf.Collation.ShouldBeNull();
    }

    [Fact]
    public void GivenABelowModifierAndPlainUri_WhenEmitted_ThenSqlUsesLikeWithNoCollationOverrideAndAnEscapeClause()
    {
        // Act
        var emitted = EmitSql(Lower("http://example.org/fhir/ValueSet", SearchModifierCode.Below));

        // Assert
        emitted.Sql.ShouldContain("Uri LIKE @p1 ESCAPE '\\'");
        emitted.Sql.ShouldNotContain("COLLATE");
        emitted.Parameters[1].Value.ShouldBe("http://example.org/fhir/ValueSet/%");
    }

    [Fact]
    public void GivenAnAboveModifierAndPlainUri_WhenEmitted_ThenSqlUsesLeftLenEqualityWithNoCollationOverride()
    {
        // Act
        var emitted = EmitSql(Lower("http://example.org/fhir/Patient/123", SearchModifierCode.Above));

        // Assert
        emitted.Sql.ShouldContain("LEFT(@p0, LEN(Uri)) = Uri");
        emitted.Sql.ShouldNotContain("COLLATE");
        emitted.Parameters[0].Value.ShouldBe("http://example.org/fhir/Patient/123/");
    }

    [Fact]
    public void GivenDifferingCaseUri_WhenLoweredWithNoModifier_ThenBindsTheValueVerbatim()
    {
        // Arrange & Act -- case sensitivity now comes from the column's own CS_AS collation rather than
        // an emitted override, so the rule must not fold case itself.
        var cte = Lower("HTTP://EXAMPLE.ORG/fhir/ValueSet/1");

        // Assert
        var equal = cte.Predicate.ShouldBeOfType<Predicate.Equal>();
        equal.Collation.ShouldBeNull();
        equal.Value.Value.ShouldBe("HTTP://EXAMPLE.ORG/fhir/ValueSet/1");
    }

    [Fact]
    public void GivenABelowModifierWithSpecialCharsInUri_WhenEmitted_ThenTheLikeArmIsEscapedExactlyOnce()
    {
        // Arrange -- URI contains %, _, [, \
        var cte = Lower("http://example.org/fhir/ValueSet/a%b_c[d\\e", SearchModifierCode.Below);

        // Assert -- raw value in the AST (pre-escape), separator appended
        var or = cte.Predicate.ShouldBeOfType<Predicate.Or>();
        or.Right.ShouldBeOfType<Predicate.Like>().Value.Value.ShouldBe("http://example.org/fhir/ValueSet/a%b_c[d\\e/");

        // Assert -- emitted parameter is escaped by SqlBuilder (escaped + trailing %)
        var emitted = EmitSql(cte);
        emitted.Parameters[1].Value.ShouldBe("http://example.org/fhir/ValueSet/a\\%b\\_c\\[d\\\\e/%");
        emitted.Sql.ShouldContain("LIKE @p1 ESCAPE '\\'");
    }

    [Fact]
    public void GivenAnAboveModifierWithSpecialCharsInUri_WhenEmitted_ThenParameterIsRawWithNoEscaping()
    {
        // Arrange -- URI contains %, _, [, \
        var cte = Lower("http://example.org/fhir/ValueSet/a%b_c[d\\e", SearchModifierCode.Above);

        // Assert -- raw value in the AST, separator appended, no LIKE escaping
        cte.Predicate.ShouldBeOfType<Predicate.PrefixOfParameter>()
            .Value.Value.ShouldBe("http://example.org/fhir/ValueSet/a%b_c[d\\e/");

        var emitted = EmitSql(cte);
        emitted.Parameters[0].Value.ShouldBe("http://example.org/fhir/ValueSet/a%b_c[d\\e/");
        emitted.Sql.ShouldContain("LEFT(@p0, LEN(Uri))");
    }

    [Fact]
    public void GivenAnUnsupportedModifier_WhenLowered_ThenThrowsNotSupportedExceptionNamingTheModifier()
    {
        // Act & Assert
        var ex = Should.Throw<NotSupportedException>(() => Lower("http://example.org/fhir", SearchModifierCode.Exact));
        ex.Message.ShouldContain("Exact");
    }
}
