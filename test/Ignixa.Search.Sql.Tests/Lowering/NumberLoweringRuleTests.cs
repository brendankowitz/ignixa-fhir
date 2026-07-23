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

    // The FHIR prefix table (search.html) over parameter range [S,E] and resource range [Low,High]:
    // gt: High > E, ge: High >= S, lt: Low < S, le: Low <= E, sa: Low > E, eb: High < S. The ordering
    // comparators do not widen ("treated as if they have arbitrarily high precision"), so S = E = 5.4.
    // The column each names is the whole content of this test: comparing gt against Low instead of High
    // silently collapses gt into sa, and the collapse is invisible on a point-valued row.
    public static TheoryData<SearchComparator, string> OrderingComparatorColumns() => new()
    {
        { SearchComparator.Gt, "HighValue" },
        { SearchComparator.Ge, "HighValue" },
        { SearchComparator.Lt, "LowValue" },
        { SearchComparator.Le, "LowValue" },
        { SearchComparator.Sa, "LowValue" },
        { SearchComparator.Eb, "HighValue" },
    };

    [Theory]
    [MemberData(nameof(OrderingComparatorColumns))]
    public void GivenAnOrderingComparator_WhenLowered_ThenComparesTheSpecifiedColumnAgainstTheUnwidenedValue(SearchComparator comparator, string expectedColumn)
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, comparator, modifier: null, new NumberSearchValue(5.4m));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103);

        // Assert
        cte.ResourceTypeId.ShouldBe((short)103);
        var (column, value) = cte.Predicate! switch
        {
            Predicate.GreaterThan gt => (gt.Column.Column, gt.Value.Value),
            Predicate.GreaterThanOrEqual ge => (ge.Column.Column, ge.Value.Value),
            Predicate.LessThan lt => (lt.Column.Column, lt.Value.Value),
            Predicate.LessThanOrEqual le => (le.Column.Column, le.Value.Value),
            var other => throw new ShouldAssertException($"{comparator} lowered to {other.GetType().Name}, not a single column comparison."),
        };

        column.ShouldBe(expectedColumn);
        value.ShouldBe(5.4m);
    }

    [Fact]
    public void GivenGtAndSaComparators_WhenLowered_ThenTheyAreDifferentRelations()
    {
        // Arrange — gt and sa share the > operator, so the only thing distinguishing them is the column.
        // They were a single switch arm until this test existed.
        var parameter = Parameter();

        // Act
        var gt = Lower(SearchComparator.Gt, parameter);
        var sa = Lower(SearchComparator.Sa, parameter);

        // Assert
        gt.ShouldBeOfType<Predicate.GreaterThan>().Column.Column.ShouldBe("HighValue");
        sa.ShouldBeOfType<Predicate.GreaterThan>().Column.Column.ShouldBe("LowValue");
    }

    [Fact]
    public void GivenLtAndEbComparators_WhenLowered_ThenTheyAreDifferentRelations()
    {
        // Arrange
        var parameter = Parameter();

        // Act
        var lt = Lower(SearchComparator.Lt, parameter);
        var eb = Lower(SearchComparator.Eb, parameter);

        // Assert
        lt.ShouldBeOfType<Predicate.LessThan>().Column.Column.ShouldBe("LowValue");
        eb.ShouldBeOfType<Predicate.LessThan>().Column.Column.ShouldBe("HighValue");
    }

    private static Predicate Lower(SearchComparator comparator, SearchParameterInfo parameter)
    {
        var predicate = new SearchParameterPredicateExpression(parameter, comparator, modifier: null, new NumberSearchValue(5.4m));
        return NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103).Predicate!;
    }

    // decimal.MaxValue's precision modifier is 0.5, so the eq window's upper edge is not representable.
    // Computing it throws OverflowException on plain user input (?value-number=eq7922...335), which
    // surfaces as a 500. The edge is dropped instead: no stored decimal can exceed decimal.MaxValue, so
    // the constraint it would express holds for every row.
    [Fact]
    public void GivenEqComparator_WhenLoweredWithDecimalMaxValue_ThenDropsTheUnrepresentableUpperEdge()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new NumberSearchValue(decimal.MaxValue));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103);

        // Assert
        var ge = cte.Predicate.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("LowValue");
        ge.Value.Value.ShouldBe(decimal.MaxValue - 0.5m);
    }

    [Fact]
    public void GivenEqComparator_WhenLoweredWithDecimalMinValue_ThenDropsTheUnrepresentableLowerEdge()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new NumberSearchValue(decimal.MinValue));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103);

        // Assert
        var le = cte.Predicate.ShouldBeOfType<Predicate.LessThanOrEqual>();
        le.Column.Column.ShouldBe("HighValue");
        le.Value.Value.ShouldBe(decimal.MinValue + 0.5m);
    }

    [Fact]
    public void GivenNeComparator_WhenLoweredWithDecimalMaxValue_ThenDropsTheUnsatisfiableDisjunct()
    {
        // Arrange — ne is eq's De Morgan negation, so the edge eq drops as always-true negates to an
        // always-false disjunct here.
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ne, modifier: null, new NumberSearchValue(decimal.MaxValue));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103);

        // Assert
        var lt = cte.Predicate.ShouldBeOfType<Predicate.LessThan>();
        lt.Column.Column.ShouldBe("LowValue");
        lt.Value.Value.ShouldBe(decimal.MaxValue - 0.5m);
    }

    [Fact]
    public void GivenNeComparator_WhenLoweredWithDecimalMinValue_ThenDropsTheUnsatisfiableDisjunct()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ne, modifier: null, new NumberSearchValue(decimal.MinValue));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201), 103);

        // Assert
        var gt = cte.Predicate.ShouldBeOfType<Predicate.GreaterThan>();
        gt.Column.Column.ShouldBe("HighValue");
        gt.Value.Value.ShouldBe(decimal.MinValue + 0.5m);
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
