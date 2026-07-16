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

public class TokenLoweringRuleTests
{
    private static LeafContext ContextResolving(SearchParameterInfo parameter, short searchParamId)
        => new(new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = searchParamId },
            new Dictionary<string, short>()));

    [Fact]
    public void GivenACodeOnlyToken_WhenLowered_ThenComparesCodeColumnOnly()
    {
        // Arrange
        var parameter = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null));

        // Act
        var cte = TokenLoweringRule.Lower(predicate, (TokenSearchValue)predicate.Value, ContextResolving(parameter, 44));

        // Assert
        cte.SearchParamId.ShouldBe((short)44);
        var equal = cte.Predicate.ShouldBeOfType<Predicate.Equal>();
        equal.Column.Column.ShouldBe("Code");
        equal.Value.Value.ShouldBe("true");
    }

    [Fact]
    public void GivenASystemQualifiedToken_WhenLowered_ThenThrowsRatherThanSilentlyIgnoringTheSystem()
    {
        // Arrange
        var parameter = new SearchParameterInfo("identifier", "identifier", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-identifier"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: "http://example.org/mrn", code: "12345", text: null));

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            TokenLoweringRule.Lower(predicate, (TokenSearchValue)predicate.Value, ContextResolving(parameter, 55)));
    }

    [Fact]
    public void GivenASystemMustBeAbsentToken_WhenLowered_ThenThrows()
    {
        // Arrange
        var parameter = new SearchParameterInfo("identifier", "identifier", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-identifier"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, TokenSearchValue.Parse("|12345"));

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            TokenLoweringRule.Lower(predicate, (TokenSearchValue)predicate.Value, ContextResolving(parameter, 55)));
    }

    [Fact]
    public void GivenATextOnlyToken_WhenLowered_ThenThrows()
    {
        // Arrange
        var parameter = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: null, text: "foo"));

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            TokenLoweringRule.Lower(predicate, (TokenSearchValue)predicate.Value, ContextResolving(parameter, 44)));
    }
}
