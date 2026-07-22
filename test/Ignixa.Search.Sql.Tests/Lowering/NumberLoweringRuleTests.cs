using Ignixa.Search.Expressions;
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
        cte.ResourceTypeId.ShouldBe((short)103);
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
        cte.ResourceTypeId.ShouldBe((short)103);
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
        cte.ResourceTypeId.ShouldBe((short)103);
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
        cte.ResourceTypeId.ShouldBe((short)103);
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
        cte.ResourceTypeId.ShouldBe((short)103);
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
        cte.ResourceTypeId.ShouldBe((short)103);
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
        cte.ResourceTypeId.ShouldBe((short)103);
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
        cte.ResourceTypeId.ShouldBe((short)103);
        var lt = cte.Predicate.ShouldBeOfType<Predicate.LessThan>();
        lt.Column.Column.ShouldBe("HighValue");
        lt.Value.Value.ShouldBe(5.4m);
    }

    // :ap — numeric approximation: tolerance = max(precision_modifier, abs(value) * 0.10)
    // 5.4m: pm=0.05, rel=0.54, tol=0.54 → [4.86, 5.94]
    [Fact]
    public void GivenApComparator_WhenLoweredWith5Point4_ThenBuildsApproximateRangeLowGe4Point86HighLe5Point94()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ap, modifier: null, new NumberSearchValue(5.4m));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103);

        // Assert
        cte.SearchParamId.ShouldBe((short)201);
        cte.ResourceTypeId.ShouldBe((short)103);
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var ge = and.Left.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("LowValue");
        ge.Value.Value.ShouldBe(4.86m);
        var le = and.Right.ShouldBeOfType<Predicate.LessThanOrEqual>();
        le.Column.Column.ShouldBe("HighValue");
        le.Value.Value.ShouldBe(5.94m);
    }

    // -50m: pm=0.5, rel=5.0, tol=5.0 → [-55.0, -45.0]
    [Fact]
    public void GivenApComparator_WhenLoweredWithNegative50_ThenBuildsApproximateRangeLowGeMinus55HighLeMinus45()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ap, modifier: null, new NumberSearchValue(-50m));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103);

        // Assert
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var ge = and.Left.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("LowValue");
        ge.Value.Value.ShouldBe(-55.0m);
        var le = and.Right.ShouldBeOfType<Predicate.LessThanOrEqual>();
        le.Column.Column.ShouldBe("HighValue");
        le.Value.Value.ShouldBe(-45.0m);
    }

    // 0m: pm=0.5, rel=0.0, tol=0.5 → [-0.5, 0.5]
    [Fact]
    public void GivenApComparator_WhenLoweredWithZero_ThenBuildsApproximateRangeLowGeMinus0Point5HighLe0Point5()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ap, modifier: null, new NumberSearchValue(0m));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103);

        // Assert
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var ge = and.Left.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("LowValue");
        ge.Value.Value.ShouldBe(-0.5m);
        var le = and.Right.ShouldBeOfType<Predicate.LessThanOrEqual>();
        le.Column.Column.ShouldBe("HighValue");
        le.Value.Value.ShouldBe(0.5m);
    }

    // 0.001m: pm=0.0005, rel=0.0001, tol=0.0005 → [0.0005, 0.0015]
    [Fact]
    public void GivenApComparator_WhenLoweredWith0Point001_ThenBuildsApproximateRangeLowGe0Point0005HighLe0Point0015()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ap, modifier: null, new NumberSearchValue(0.001m));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103);

        // Assert
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var ge = and.Left.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("LowValue");
        ge.Value.Value.ShouldBe(0.0005m);
        var le = and.Right.ShouldBeOfType<Predicate.LessThanOrEqual>();
        le.Column.Column.ShouldBe("HighValue");
        le.Value.Value.ShouldBe(0.0015m);
    }

    [Fact]
    public void GivenApComparator_WhenLoweredWithDecimalMaxValue_ThenBuildsRepresentableLowerBoundOnly()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ap, modifier: null, new NumberSearchValue(decimal.MaxValue));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103);

        // Assert
        var ge = cte.Predicate.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("LowValue");
        ge.Value.Value.ShouldBe(decimal.MaxValue - (decimal.MaxValue * 0.10m));
    }

    [Fact]
    public void GivenApComparator_WhenLoweredWithDecimalMinValue_ThenBuildsRepresentableUpperBoundOnly()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ap, modifier: null, new NumberSearchValue(decimal.MinValue));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103);

        // Assert
        var le = cte.Predicate.ShouldBeOfType<Predicate.LessThanOrEqual>();
        le.Column.Column.ShouldBe("HighValue");
        le.Value.Value.ShouldBe(decimal.MinValue + (decimal.MaxValue * 0.10m));
    }
}
