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
    public void GivenNeComparator_WhenLowered_ThenNegatesTheEqContainment()
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
        lt.Column.Column.ShouldBe("LowValue");
        lt.Value.Value.ShouldBe(5.35m);
        var gt = or.Right.ShouldBeOfType<Predicate.GreaterThan>();
        gt.Column.Column.ShouldBe("HighValue");
        gt.Value.Value.ShouldBe(5.45m);
    }

    [Fact]
    public void GivenGeComparator_WhenLowered_ThenComparesHighValue()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ge, modifier: null, new NumberSearchValue(5.4m));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103);

        // Assert
        cte.ResourceTypeId.ShouldBe((short)103);
        var ge = cte.Predicate.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("HighValue");
        ge.Value.Value.ShouldBe(5.4m);
    }

    [Fact]
    public void GivenGtComparator_WhenLowered_ThenComparesHighValue()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Gt, modifier: null, new NumberSearchValue(5.4m));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103);

        // Assert
        cte.ResourceTypeId.ShouldBe((short)103);
        var gt = cte.Predicate.ShouldBeOfType<Predicate.GreaterThan>();
        gt.Column.Column.ShouldBe("HighValue");
        gt.Value.Value.ShouldBe(5.4m);
    }

    [Fact]
    public void GivenSaComparator_WhenLowered_ThenComparesLowValueUnlikeGt()
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
    public void GivenLeComparator_WhenLowered_ThenComparesLowValue()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Le, modifier: null, new NumberSearchValue(5.4m));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103);

        // Assert
        cte.ResourceTypeId.ShouldBe((short)103);
        var le = cte.Predicate.ShouldBeOfType<Predicate.LessThanOrEqual>();
        le.Column.Column.ShouldBe("LowValue");
        le.Value.Value.ShouldBe(5.4m);
    }

    [Fact]
    public void GivenLtComparator_WhenLowered_ThenComparesLowValue()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Lt, modifier: null, new NumberSearchValue(5.4m));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103);

        // Assert
        cte.ResourceTypeId.ShouldBe((short)103);
        var lt = cte.Predicate.ShouldBeOfType<Predicate.LessThan>();
        lt.Column.Column.ShouldBe("LowValue");
        lt.Value.Value.ShouldBe(5.4m);
    }

    [Fact]
    public void GivenEbComparator_WhenLowered_ThenComparesHighValueUnlikeLt()
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

    // :ap — numeric approximation is OVERLAP against the widened bounds, not containment:
    // tolerance = max(precision_modifier, abs(value) * 0.10)
    // 5.4m: pm=0.05, rel=0.54, tol=0.54 → LowValue <= 5.94 AND HighValue >= 4.86
    [Fact]
    public void GivenApComparator_WhenLoweredWith5Point4_ThenBuildsApproximateOverlapLowLe5Point94HighGe4Point86()
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
        var le = and.Left.ShouldBeOfType<Predicate.LessThanOrEqual>();
        le.Column.Column.ShouldBe("LowValue");
        le.Value.Value.ShouldBe(5.94m);
        var ge = and.Right.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("HighValue");
        ge.Value.Value.ShouldBe(4.86m);
    }

    // -50m: pm=0.5, rel=5.0, tol=5.0 → LowValue <= -45.0 AND HighValue >= -55.0
    [Fact]
    public void GivenApComparator_WhenLoweredWithNegative50_ThenBuildsApproximateOverlapLowLeMinus45HighGeMinus55()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ap, modifier: null, new NumberSearchValue(-50m));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103);

        // Assert
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var le = and.Left.ShouldBeOfType<Predicate.LessThanOrEqual>();
        le.Column.Column.ShouldBe("LowValue");
        le.Value.Value.ShouldBe(-45.0m);
        var ge = and.Right.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("HighValue");
        ge.Value.Value.ShouldBe(-55.0m);
    }

    // 0m: pm=0.5, rel=0.0, tol=0.5 → LowValue <= 0.5 AND HighValue >= -0.5
    [Fact]
    public void GivenApComparator_WhenLoweredWithZero_ThenBuildsApproximateOverlapLowLe0Point5HighGeMinus0Point5()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ap, modifier: null, new NumberSearchValue(0m));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103);

        // Assert
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var le = and.Left.ShouldBeOfType<Predicate.LessThanOrEqual>();
        le.Column.Column.ShouldBe("LowValue");
        le.Value.Value.ShouldBe(0.5m);
        var ge = and.Right.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("HighValue");
        ge.Value.Value.ShouldBe(-0.5m);
    }

    // 0.001m: pm=0.0005, rel=0.0001, tol=0.0005 → LowValue <= 0.0015 AND HighValue >= 0.0005
    [Fact]
    public void GivenApComparator_WhenLoweredWith0Point001_ThenBuildsApproximateOverlapLowLe0Point0015HighGe0Point0005()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ap, modifier: null, new NumberSearchValue(0.001m));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103);

        // Assert
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var le = and.Left.ShouldBeOfType<Predicate.LessThanOrEqual>();
        le.Column.Column.ShouldBe("LowValue");
        le.Value.Value.ShouldBe(0.0015m);
        var ge = and.Right.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("HighValue");
        ge.Value.Value.ShouldBe(0.0005m);
    }

    [Fact]
    public void GivenApComparator_WhenLoweredWithDecimalMaxValue_ThenBuildsRepresentableHighValueBoundOnly()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ap, modifier: null, new NumberSearchValue(decimal.MaxValue));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103);

        // Assert
        var ge = cte.Predicate.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("HighValue");
        ge.Value.Value.ShouldBe(decimal.MaxValue - (decimal.MaxValue * 0.10m));
    }

    [Fact]
    public void GivenApComparator_WhenLoweredWithDecimalMinValue_ThenBuildsRepresentableLowValueBoundOnly()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ap, modifier: null, new NumberSearchValue(decimal.MinValue));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103);

        // Assert
        var le = cte.Predicate.ShouldBeOfType<Predicate.LessThanOrEqual>();
        le.Column.Column.ShouldBe("LowValue");
        le.Value.Value.ShouldBe(decimal.MinValue + (decimal.MaxValue * 0.10m));
    }
}
