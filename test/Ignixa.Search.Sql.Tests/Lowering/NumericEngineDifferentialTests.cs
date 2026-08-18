using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Tests.TestSupport;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

/// <summary>
/// Holds the two numeric search engines to the same answers: the SQL compiler's <c>Predicate</c> tree and the
/// field-level <c>Expression</c> tree that the EF Core backend and the in-memory interpreter consume.
/// </summary>
/// <remarks>
/// <para>
/// The numeric counterpart to <see cref="DateEngineDifferentialTests"/>, and the suite whose absence let the
/// same defect survive one type longer. The prefix table was written out three times — once per engine, plus
/// a rollback copy — and <c>ap</c> had drifted: the compiler asked for the spec's overlap, while the
/// field-level pair widened the window and then asked for containment. The field-level pair is what answers
/// real requests, so production <c>ap</c> was strictly under-matching. All three now render
/// <see cref="NumericRangeComparisonSemantics"/>, so this suite should be incapable of failing; that is the
/// point. It fails the moment someone reintroduces a local copy, which is how the drift happened the first
/// time.
/// </para>
/// <para>
/// Row widths are chosen to be exactly where the two disagreed: a zero-width point row cannot tell
/// containment from overlap, but a row wider than the search value's tolerance window can, and a row that
/// merely straddles the window's edge is the case containment silently drops. Query precisions vary because
/// the precision modifier is half the last represented digit, so <c>5</c>, <c>5.0</c> and <c>5.00</c> are
/// three different windows around the same number.
/// </para>
/// </remarks>
public class NumericEngineDifferentialTests
{
    private static readonly SearchComparator[] AllComparators =
    [
        SearchComparator.Eq,
        SearchComparator.Ne,
        SearchComparator.Lt,
        SearchComparator.Gt,
        SearchComparator.Le,
        SearchComparator.Ge,
        SearchComparator.Sa,
        SearchComparator.Eb,
        SearchComparator.Ap,
    ];

    public static TheoryData<decimal, decimal, decimal> RowsAndQueries()
    {
        var data = new TheoryData<decimal, decimal, decimal>();

        (decimal Low, decimal High)[] rows =
        [
            (5m, 5m),           // a point row: containment and overlap agree here
            (5.0m, 5.1m),       // a narrow range, inside a coarse window
            (1m, 10m),          // a range wider than any tolerance window below: the disagreement case
            (4.9m, 9m),         // a range straddling the top edge of the 5 +/- window
            (-10m, -1m),        // wholly negative
            (-1m, 1m),          // straddling zero
            (0m, 0m),           // the zero point row, where the proportional ap term vanishes
            (99m, 101m),        // a wide range around a coarse query
        ];
        decimal[] queries =
        [
            5m,
            5.0m,
            5.00m,
            0m,
            1m,
            10m,
            100m,
            -5m,
            -0.5m,
        ];

        foreach (var row in rows)
        {
            foreach (var query in queries)
            {
                data.Add(row.Low, row.High, query);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RowsAndQueries))]
    public void GivenTheSameRowAndQuery_WhenLoweredByBothEngines_ThenEveryPrefixAgrees(decimal low, decimal high, decimal query)
    {
        // Arrange
        var row = (Low: low, High: high);

        foreach (var comparator in AllComparators)
        {
            // Act
            var sqlVerdict = NumberRowMatcher.Matches(comparator, query, row);
            var expressionVerdict = ExpressionEngineVerdict(comparator, query, row);

            // Assert
            sqlVerdict.ShouldBe(
                expressionVerdict,
                $"{comparator} disagreed between the engines for row [{low}, {high}] and query '{query}'");
        }
    }

    [Theory]
    [MemberData(nameof(RowsAndQueries))]
    public void GivenTheSameRowAndQuery_WhenEqAndNeAreEvaluated_ThenExactlyOneMatches(decimal low, decimal high, decimal query)
    {
        // eq and ne are defined as exact complements ("fully contains" / "does not fully contain"), so a row
        // must satisfy precisely one of them. This holds independently of the spec text -- no reading of it
        // permits a resource to be both equal and unequal.

        // Arrange
        var row = (Low: low, High: high);

        foreach (var engine in Engines)
        {
            // Act
            var equal = engine.Verdict(SearchComparator.Eq, query, row);
            var notEqual = engine.Verdict(SearchComparator.Ne, query, row);

            // Assert
            equal.ShouldNotBe(
                notEqual,
                $"{engine.Name} returned eq={equal} and ne={notEqual} for row [{low}, {high}] and query '{query}'");
        }
    }

    [Fact]
    public void GivenARowWiderThanTheToleranceWindow_WhenApIsEvaluated_ThenBothEnginesOverlapRatherThanContain()
    {
        // The regression this suite exists for. ap5 widens to [4.5, 5.5]; a row spanning [1, 10] is not
        // CONTAINED by that window but plainly OVERLAPS it, and the spec defines ap as overlap. The old
        // field-level containment answered "no" and dropped the row -- and the field-level tree is the one
        // serving production traffic.

        // Arrange
        var row = (Low: 1m, High: 10m);

        foreach (var engine in Engines)
        {
            // Act & Assert
            engine.Verdict(SearchComparator.Ap, 5m, row).ShouldBeTrue(engine.Name);
        }
    }

    [Fact]
    public void GivenAPointRowFarOutsideTheToleranceWindow_WhenApIsEvaluated_ThenNeitherEngineMatches()
    {
        // The other side of the same coin: overlap must not be so permissive that ap stops discriminating.
        // ap5 widens to [4.5, 5.5] and a point row at 100 is nowhere near it.

        // Arrange
        var row = (Low: 100m, High: 100m);

        foreach (var engine in Engines)
        {
            // Act & Assert
            engine.Verdict(SearchComparator.Ap, 5m, row).ShouldBeFalse(engine.Name);
        }
    }

    [Fact]
    public void GivenApAtZero_WhenEvaluated_ThenTheWindowFallsBackToThePrecisionModifier()
    {
        // The proportional term is |value| * 0.1, which vanishes at zero. Without the precision floor ap0
        // would collapse to exact equality; with it the window is 0 +/- 0.5.

        // Arrange & Act & Assert
        foreach (var engine in Engines)
        {
            engine.Verdict(SearchComparator.Ap, 0m, (Low: 0.4m, High: 0.4m)).ShouldBeTrue(engine.Name);
            engine.Verdict(SearchComparator.Ap, 0m, (Low: 0.6m, High: 0.6m)).ShouldBeFalse(engine.Name);
        }
    }

    private static (string Name, Func<SearchComparator, decimal, (decimal Low, decimal High), bool> Verdict)[] Engines =>
    [
        ("sql", NumberRowMatcher.Matches),
        ("expression", ExpressionEngineVerdict),
    ];

    private static bool ExpressionEngineVerdict(SearchComparator comparator, decimal searchValue, (decimal Low, decimal High) row)
    {
        // LowerToLegacy is the production bridge: SearchExpressionQueryBuilder calls exactly this before
        // handing the tree to EF Core, so this is the shape real requests are answered with.
        var parameter = new SearchParameterInfo(
            "probability",
            "probability",
            SearchParamType.Number,
            new Uri("http://hl7.org/fhir/SearchParameter/RiskAssessment-probability"));

        return Evaluate(
            LegacyExpressionLowerer.LowerToLegacy(
                new SearchParameterPredicateExpression(
                    parameter,
                    comparator,
                    modifier: null,
                    new NumberSearchValue(searchValue))),
            row);
    }

    private static bool Evaluate(Expression expression, (decimal Low, decimal High) row) => expression switch
    {
        MultiaryExpression { MultiaryOperation: MultiaryOperator.And } and => and.Expressions.All(x => Evaluate(x, row)),
        MultiaryExpression { MultiaryOperation: MultiaryOperator.Or } or => or.Expressions.Any(x => Evaluate(x, row)),
        BinaryExpression binary => Compare(binary, row),
        _ => throw new NotSupportedException($"The row evaluator does not model '{expression.GetType().Name}'."),
    };

    private static bool Compare(BinaryExpression binary, (decimal Low, decimal High) row)
    {
        var column = binary.FieldName switch
        {
            FieldName.NumberLow => row.Low,
            FieldName.NumberHigh => row.High,
            _ => throw new NotSupportedException($"Unexpected field '{binary.FieldName}'."),
        };
        var bound = (decimal)binary.Value;

        return binary.BinaryOperator switch
        {
            BinaryOperator.LessThan => column < bound,
            BinaryOperator.LessThanOrEqual => column <= bound,
            BinaryOperator.GreaterThan => column > bound,
            BinaryOperator.GreaterThanOrEqual => column >= bound,
            _ => throw new NotSupportedException($"Unexpected operator '{binary.BinaryOperator}'."),
        };
    }
}
