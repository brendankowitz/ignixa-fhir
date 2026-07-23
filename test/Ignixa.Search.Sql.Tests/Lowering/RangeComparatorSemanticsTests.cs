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

/// <summary>
/// Proves the properties the ranged-comparator rework exists for, by evaluating the lowered predicates
/// against concrete rows instead of only inspecting their AST shape: eq is containment, ne is its exact
/// negation (so the two partition every row), and ap is a strictly looser overlap. Rows that store a
/// genuine range are the cases that separate the three — on a point-valued row containment and overlap
/// coincide, which is why the old disjoint-ne bug survived the point-valued tests.
/// </summary>
public class RangeComparatorSemanticsTests
{
    private static SearchParameterInfo NumberParameter()
        => new("value-number", "value-number", SearchParamType.Number, new Uri("http://example.org/fhir/SearchParameter/Observation-value-number"));

    private static SearchParameterInfo DateParameter()
        => new("date", "date", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Observation-date"));

    private static LeafContext Context(SearchParameterInfo parameter, short searchParamId)
        => new(new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = searchParamId },
            new Dictionary<string, short>()));

    private static Predicate LowerNumber(SearchComparator comparator, decimal value)
    {
        var parameter = NumberParameter();
        var predicate = new SearchParameterPredicateExpression(parameter, comparator, modifier: null, new NumberSearchValue(value));
        return NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, Context(parameter, 201), 103).Predicate!;
    }

    private static Predicate LowerDate(SearchComparator comparator, DateTimeSearchValue value)
    {
        var parameter = DateParameter();
        var predicate = new SearchParameterPredicateExpression(parameter, comparator, modifier: null, value);
        return DateTimeLoweringRule.Lower(predicate, (DateTimeSearchValue)predicate.Value, Context(parameter, 203), 103).Predicate!;
    }

    private static Dictionary<string, object> NumericRow(decimal low, decimal high)
        => new() { ["LowValue"] = low, ["HighValue"] = high };

    private static Dictionary<string, object> DateRow(DateTimeOffset start, DateTimeOffset end)
        => new() { ["StartDateTime"] = start, ["EndDateTime"] = end };

    // eq for 5.4 widens to the containment window [5.35, 5.45]. Each row names its relation to that
    // window: inside, straddling one edge, or entirely outside.
    public static TheoryData<string, decimal, decimal> NumericRows() => new()
    {
        { "point at the search value", 5.4m, 5.4m },
        { "point on the lower edge", 5.35m, 5.35m },
        { "point on the upper edge", 5.45m, 5.45m },
        { "point below the window", 5.0m, 5.0m },
        { "point above the window", 6.0m, 6.0m },
        { "range strictly inside the window", 5.36m, 5.44m },
        { "range exactly spanning the window", 5.35m, 5.45m },
        { "range straddling the lower edge", 5.30m, 5.40m },
        { "range straddling the upper edge", 5.40m, 5.50m },
        { "range enclosing the window", 5.0m, 6.0m },
        { "range entirely below the window", 4.0m, 5.0m },
        { "range entirely above the window", 6.0m, 7.0m },
    };

    [Theory]
    [MemberData(nameof(NumericRows))]
    public void GivenAnyNumericRow_WhenLoweredWithEqAndNe_ThenExactlyOneMatches(string scenario, decimal low, decimal high)
    {
        // Arrange
        var row = NumericRow(low, high);

        // Act
        var eq = PredicateRowEvaluator.Matches(LowerNumber(SearchComparator.Eq, 5.4m), row);
        var ne = PredicateRowEvaluator.Matches(LowerNumber(SearchComparator.Ne, 5.4m), row);

        // Assert
        (eq ^ ne).ShouldBeTrue($"{scenario}: eq={eq}, ne={ne} — eq and ne must partition every row");
    }

    // The date window for eq is the search value's own [Start, End]; the rows mirror the numeric cases.
    public static TheoryData<string, DateTimeOffset, DateTimeOffset> DateRows() => new()
    {
        { "instant inside the year", Instant(2023, 6, 1), Instant(2023, 6, 1) },
        { "instant on the lower edge", Instant(2023, 1, 1), Instant(2023, 1, 1) },
        { "instant before the year", Instant(2022, 12, 31), Instant(2022, 12, 31) },
        { "range strictly inside the year", Instant(2023, 3, 1), Instant(2023, 9, 1) },
        { "range straddling the lower edge", Instant(2022, 12, 1), Instant(2023, 3, 1) },
        { "range straddling the upper edge", Instant(2023, 12, 1), Instant(2024, 3, 1) },
        { "range enclosing the year", Instant(2022, 1, 1), Instant(2024, 1, 1) },
        { "range entirely after the year", Instant(2024, 1, 1), Instant(2024, 6, 1) },
    };

    [Theory]
    [MemberData(nameof(DateRows))]
    public void GivenAnyDateRow_WhenLoweredWithEqAndNe_ThenExactlyOneMatches(string scenario, DateTimeOffset start, DateTimeOffset end)
    {
        // Arrange — date=2023, i.e. the whole of 2023 as the parameter range
        var value = Year2023();
        var row = DateRow(start, end);

        // Act
        var eq = PredicateRowEvaluator.Matches(LowerDate(SearchComparator.Eq, value), row);
        var ne = PredicateRowEvaluator.Matches(LowerDate(SearchComparator.Ne, value), row);

        // Assert
        (eq ^ ne).ShouldBeTrue($"{scenario}: eq={eq}, ne={ne} — eq and ne must partition every row");
    }

    [Fact]
    public void GivenARangeRowEnclosingTheSearchValue_WhenLowered_ThenEqRejectsItButApAcceptsIt()
    {
        // Arrange — a row storing [5.0, 6.0], which encloses 5.4 without being contained by any window
        // eq or ap builds. This is the row that separates containment from overlap; a point-valued row
        // cannot, because for LowValue = HighValue the two relations coincide.
        var row = NumericRow(5.0m, 6.0m);

        // Act
        var eq = PredicateRowEvaluator.Matches(LowerNumber(SearchComparator.Eq, 5.4m), row);
        var ne = PredicateRowEvaluator.Matches(LowerNumber(SearchComparator.Ne, 5.4m), row);
        var ap = PredicateRowEvaluator.Matches(LowerNumber(SearchComparator.Ap, 5.4m), row);

        // Assert
        eq.ShouldBeFalse();
        ne.ShouldBeTrue();
        ap.ShouldBeTrue();
    }

    [Fact]
    public void GivenAPointRowAtTheSearchValue_WhenLowered_ThenContainmentAndOverlapCoincide()
    {
        // Arrange — LowValue = HighValue, what a plain valueQuantity or number indexes to
        var row = NumericRow(5.4m, 5.4m);

        // Act
        var eq = PredicateRowEvaluator.Matches(LowerNumber(SearchComparator.Eq, 5.4m), row);
        var ap = PredicateRowEvaluator.Matches(LowerNumber(SearchComparator.Ap, 5.4m), row);

        // Assert
        eq.ShouldBeTrue();
        ap.ShouldBeTrue();
    }

    [Fact]
    public void GivenARowOutsideTheEqWindowButWithinTheApTolerance_WhenLowered_ThenOnlyApMatches()
    {
        // Arrange — 5.4 gives an eq window of [5.35, 5.45] but an ap tolerance of 0.54, so a point at
        // 5.9 misses eq and hits ap: ap is deliberately the looser relation.
        var row = NumericRow(5.9m, 5.9m);

        // Act
        var eq = PredicateRowEvaluator.Matches(LowerNumber(SearchComparator.Eq, 5.4m), row);
        var ap = PredicateRowEvaluator.Matches(LowerNumber(SearchComparator.Ap, 5.4m), row);

        // Assert
        eq.ShouldBeFalse();
        ap.ShouldBeTrue();
    }

    [Fact]
    public void GivenAnyNumericRowMatchedByEq_WhenLoweredWithAp_ThenApAlsoMatchesIt()
    {
        // Arrange — ap widens by max(precisionModifier, 10%), never less than eq's precisionModifier,
        // and relaxes containment to overlap, so eq's match set is a subset of ap's.
        var eqPredicate = LowerNumber(SearchComparator.Eq, 5.4m);
        var apPredicate = LowerNumber(SearchComparator.Ap, 5.4m);

        // Act & Assert
        foreach (var (scenario, low, high) in NumericRows().Select(r => ((string)r[0]!, (decimal)r[1]!, (decimal)r[2]!)))
        {
            var row = NumericRow(low, high);
            if (PredicateRowEvaluator.Matches(eqPredicate, row))
            {
                PredicateRowEvaluator.Matches(apPredicate, row).ShouldBeTrue($"{scenario}: eq matched but ap did not");
            }
        }
    }

    private static DateTimeOffset Instant(int year, int month, int day)
        => new(year, month, day, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeSearchValue Year2023()
        => new(
            new PartialDateTime(Instant(2023, 1, 1)),
            new PartialDateTime(new DateTimeOffset(2023, 12, 31, 23, 59, 59, TimeSpan.Zero)));
}
