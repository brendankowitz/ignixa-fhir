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

public class UriLoweringRuleTests
{
    private static LeafContext ContextResolving(SearchParameterInfo parameter, short searchParamId)
        => new(new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = searchParamId },
            new Dictionary<string, short>()));

    [Fact]
    public void GivenAPlainUriValue_WhenLowered_ThenComparesTheUriColumn()
    {
        // Arrange
        var parameter = new SearchParameterInfo("url", "url", SearchParamType.Uri, new Uri("http://hl7.org/fhir/SearchParameter/ValueSet-url"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new UriSearchValue("http://example.org/fhir/ValueSet/1", separateCanonicalComponents: false));

        // Act
        var cte = UriLoweringRule.Lower(predicate, (UriSearchValue)predicate.Value, ContextResolving(parameter, 88), 105);

        // Assert
        cte.SearchParamId.ShouldBe((short)88);
        var equal = cte.Predicate.ShouldBeOfType<Predicate.Equal>();
        equal.Column.Column.ShouldBe("Uri");
        equal.Value.Value.ShouldBe("http://example.org/fhir/ValueSet/1");
    }

    [Fact]
    public void GivenAnAboveModifier_WhenLowered_ThenThrowsRatherThanSilentlyIgnoringHierarchy()
    {
        // Arrange
        var parameter = new SearchParameterInfo("url", "url", SearchParamType.Uri, new Uri("http://hl7.org/fhir/SearchParameter/ValueSet-url"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Above), new UriSearchValue("http://example.org/fhir", separateCanonicalComponents: false));

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            UriLoweringRule.Lower(predicate, (UriSearchValue)predicate.Value, ContextResolving(parameter, 88), 105));
    }

    [Fact]
    public void GivenABelowModifier_WhenLowered_ThenThrowsRatherThanSilentlyIgnoringHierarchy()
    {
        // Arrange
        var parameter = new SearchParameterInfo("url", "url", SearchParamType.Uri, new Uri("http://hl7.org/fhir/SearchParameter/ValueSet-url"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Below), new UriSearchValue("http://example.org/fhir/ValueSet", separateCanonicalComponents: false));

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            UriLoweringRule.Lower(predicate, (UriSearchValue)predicate.Value, ContextResolving(parameter, 88), 105));
    }
}
