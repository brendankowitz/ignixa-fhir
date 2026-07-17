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

public class QuantityLoweringRuleTests
{
    private static LeafContext ContextResolving(SearchParameterInfo parameter, short searchParamId)
        => new(new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = searchParamId },
            new Dictionary<string, short>()));

    private static SearchParameterInfo Parameter()
        => new("value-quantity", "value-quantity", SearchParamType.Quantity, new Uri("http://hl7.org/fhir/SearchParameter/Observation-value-quantity"));

    [Fact]
    public void GivenAnUnqualifiedQuantityValue_WhenLowered_ThenComparesLowAndHighValueOnly()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new QuantitySearchValue(system: null!, code: null!, 5.4m));

        // Act
        var cte = QuantityLoweringRule.Lower(predicate, (QuantitySearchValue)predicate.Value, ContextResolving(parameter, 202), 103);

        // Assert
        cte.SearchParamId.ShouldBe((short)202);
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        and.Left.ShouldBeOfType<Predicate.GreaterThanOrEqual>().Column.Column.ShouldBe("LowValue");
        and.Right.ShouldBeOfType<Predicate.LessThanOrEqual>().Column.Column.ShouldBe("HighValue");
    }

    [Fact]
    public void GivenASystemQualifiedQuantity_WhenLowered_ThenThrowsRatherThanSilentlyIgnoringTheSystem()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new QuantitySearchValue("http://unitsofmeasure.org", "mg", 5.4m));

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            QuantityLoweringRule.Lower(predicate, (QuantitySearchValue)predicate.Value, ContextResolving(parameter, 202), 103));
    }

    [Fact]
    public void GivenACodeQualifiedQuantity_WhenLowered_ThenThrowsRatherThanSilentlyIgnoringTheCode()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new QuantitySearchValue(system: null!, code: "mg", 5.4m));

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            QuantityLoweringRule.Lower(predicate, (QuantitySearchValue)predicate.Value, ContextResolving(parameter, 202), 103));
    }
}
