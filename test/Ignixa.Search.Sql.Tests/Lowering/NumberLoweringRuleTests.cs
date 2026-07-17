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

public class NumberLoweringRuleTests
{
    private static LeafContext ContextResolving(SearchParameterInfo parameter, short searchParamId)
        => new(new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = searchParamId },
            new Dictionary<string, short>()));

    private static SearchParameterInfo Parameter()
        => new("probability", "probability", SearchParamType.Number, new Uri("http://hl7.org/fhir/SearchParameter/RiskAssessment-probability"));

    [Fact]
    public void GivenEqComparator_WhenLowered_ThenBuildsCompoundAndOfWidenedLowAndHighBounds()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new NumberSearchValue(5.4m));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103);

        // Assert
        cte.SearchParamId.ShouldBe((short)201);
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var ge = and.Left.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("LowValue");
        ge.Value.Value.ShouldBe(5.35m);
        var le = and.Right.ShouldBeOfType<Predicate.LessThanOrEqual>();
        le.Column.Column.ShouldBe("HighValue");
        le.Value.Value.ShouldBe(5.45m);
    }

    [Fact]
    public void GivenNeComparator_WhenLowered_ThenBuildsOrOfWidenedLowAndHighBounds()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ne, modifier: null, new NumberSearchValue(5.4m));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103);

        // Assert
        var or = cte.Predicate.ShouldBeOfType<Predicate.Or>();
        var lt = or.Left.ShouldBeOfType<Predicate.LessThan>();
        lt.Column.Column.ShouldBe("HighValue");
        lt.Value.Value.ShouldBe(5.35m);
        var gt = or.Right.ShouldBeOfType<Predicate.GreaterThan>();
        gt.Column.Column.ShouldBe("LowValue");
        gt.Value.Value.ShouldBe(5.45m);
    }

    [Fact]
    public void GivenGeComparator_WhenLowered_ThenComparesLowValueOnly()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ge, modifier: null, new NumberSearchValue(5.4m));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103);

        // Assert
        var ge = cte.Predicate.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("LowValue");
        ge.Value.Value.ShouldBe(5.4m);
    }

    [Fact]
    public void GivenGtComparator_WhenLowered_ThenComparesLowValueOnly()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Gt, modifier: null, new NumberSearchValue(5.4m));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103);

        // Assert
        var gt = cte.Predicate.ShouldBeOfType<Predicate.GreaterThan>();
        gt.Column.Column.ShouldBe("LowValue");
        gt.Value.Value.ShouldBe(5.4m);
    }

    [Fact]
    public void GivenSaComparator_WhenLowered_ThenComparesLowValueOnlySameAsGt()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Sa, modifier: null, new NumberSearchValue(5.4m));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103);

        // Assert
        var gt = cte.Predicate.ShouldBeOfType<Predicate.GreaterThan>();
        gt.Column.Column.ShouldBe("LowValue");
        gt.Value.Value.ShouldBe(5.4m);
    }

    [Fact]
    public void GivenLeComparator_WhenLowered_ThenComparesHighValueOnly()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Le, modifier: null, new NumberSearchValue(5.4m));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103);

        // Assert
        var le = cte.Predicate.ShouldBeOfType<Predicate.LessThanOrEqual>();
        le.Column.Column.ShouldBe("HighValue");
        le.Value.Value.ShouldBe(5.4m);
    }

    [Fact]
    public void GivenLtComparator_WhenLowered_ThenComparesHighValueOnly()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Lt, modifier: null, new NumberSearchValue(5.4m));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103);

        // Assert
        var lt = cte.Predicate.ShouldBeOfType<Predicate.LessThan>();
        lt.Column.Column.ShouldBe("HighValue");
        lt.Value.Value.ShouldBe(5.4m);
    }

    [Fact]
    public void GivenEbComparator_WhenLowered_ThenComparesHighValueOnlySameAsLt()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eb, modifier: null, new NumberSearchValue(5.4m));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103);

        // Assert
        var lt = cte.Predicate.ShouldBeOfType<Predicate.LessThan>();
        lt.Column.Column.ShouldBe("HighValue");
        lt.Value.Value.ShouldBe(5.4m);
    }

    [Fact]
    public void GivenApComparator_WhenLowered_ThenThrows()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ap, modifier: null, new NumberSearchValue(5.4m));

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103));
    }
}
