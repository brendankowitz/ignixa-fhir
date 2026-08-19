using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

/// <summary>
/// Holds the two date search engines to the same answers: the SQL compiler's <c>Predicate</c> tree and the
/// field-level <c>Expression</c> tree that the EF Core backend and the in-memory interpreter consume.
/// </summary>
/// <remarks>
/// <para>
/// The prefix table used to be written out three times — once per engine, plus a rollback copy — and two of
/// the nine prefixes had drifted apart: <c>eq</c> was a containment in SQL and an overlap in the expression
/// tree, and <c>ap</c> was the reverse. Both now render
/// <see cref="DateRangeComparisonSemantics"/>, so this suite should be incapable of failing; that is the
/// point. It fails the moment someone reintroduces a local copy, which is how the drift happened the
/// first time.
/// </para>
/// <para>
/// Row widths are chosen to be exactly where the two disagreed: a zero-width instant row cannot tell
/// containment from overlap, a day-wide row can, and a month-wide Timing or Period row — now common, since
/// Timing indexes its whole extent — makes the difference impossible to miss.
/// </para>
/// </remarks>
public class DateEngineDifferentialTests
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

    public static TheoryData<string, string> RowsAndQueries()
    {
        var data = new TheoryData<string, string>();

        string[] rows =
        [
            "2015-02-07T13:28:17Z",  // an instant-like point row
            "2015-02-07",            // a day-wide row
            "2015-02",               // a month-wide row, the Timing/Period shape
            "2015",                  // a year-wide row
        ];
        string[] queries =
        [
            "2015-02-07T13:28:17Z",
            "2015-02-07T13:28:17.5Z",
            "2015-02-07",
            "2015-02-20",
            "2015-02",
            "2015",
            "2016",
        ];

        foreach (var row in rows)
        {
            foreach (var query in queries)
            {
                data.Add(row, query);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RowsAndQueries))]
    public void GivenTheSameRowAndQuery_WhenLoweredByBothEngines_ThenEveryPrefixAgrees(string rowLiteral, string queryLiteral)
    {
        // Arrange
        var row = DateTimeSearchValue.Parse(rowLiteral);
        var searchValue = DateTimeSearchValue.Parse(queryLiteral);

        foreach (var comparator in AllComparators)
        {
            // Act
            var sqlVerdict = DateRowMatcher.Matches(comparator, searchValue, row);
            var expressionVerdict = ExpressionEngineVerdict(comparator, searchValue, row);

            // Assert
            sqlVerdict.ShouldBe(
                expressionVerdict,
                $"{comparator} disagreed between the engines for row '{rowLiteral}' and query '{queryLiteral}'");
        }
    }

    [Theory]
    [MemberData(nameof(RowsAndQueries))]
    public void GivenTheSameRowAndQuery_WhenEqAndNeAreEvaluated_ThenExactlyOneMatches(string rowLiteral, string queryLiteral)
    {
        // eq and ne are defined as exact complements ("fully contains" / "does not fully contain"), so a row
        // must satisfy precisely one of them. The old overlap-shaped eq broke this: a wide row overlapped a
        // narrow search value without being contained by it, and so came back for both queries. This holds
        // independently of the spec text -- no reading of it permits a resource to be both equal and unequal.

        // Arrange
        var row = DateTimeSearchValue.Parse(rowLiteral);
        var searchValue = DateTimeSearchValue.Parse(queryLiteral);

        foreach (var engine in Engines)
        {
            // Act
            var equal = engine.Verdict(SearchComparator.Eq, searchValue, row);
            var notEqual = engine.Verdict(SearchComparator.Ne, searchValue, row);

            // Assert
            equal.ShouldNotBe(
                notEqual,
                $"{engine.Name} returned eq={equal} and ne={notEqual} for row '{rowLiteral}' and query '{queryLiteral}'");
        }
    }

    [Fact]
    public void GivenAMonthWideRowAndAOneDayQuery_WhenEvaluated_ThenNeitherEngineTreatsItAsEqual()
    {
        // The case Timing made common and the regression this suite exists for: a month-long extent is not
        // "equal to" a single day within it, because the day's range does not contain the month's.

        // Arrange
        var row = new DateTimeSearchValue(PartialDateTime.Parse("2015-02-07"), PartialDateTime.Parse("2015-03-07"));
        var searchValue = DateTimeSearchValue.Parse("2015-02-20");

        foreach (var engine in Engines)
        {
            // Act & Assert
            engine.Verdict(SearchComparator.Eq, searchValue, row).ShouldBeFalse(engine.Name);
            engine.Verdict(SearchComparator.Ne, searchValue, row).ShouldBeTrue(engine.Name);
        }
    }

    private static (string Name, Func<SearchComparator, DateTimeSearchValue, DateTimeSearchValue, bool> Verdict)[] Engines =>
    [
        ("sql", DateRowMatcher.Matches),
        ("expression", ExpressionEngineVerdict),
    ];

    private static bool ExpressionEngineVerdict(SearchComparator comparator, DateTimeSearchValue searchValue, DateTimeSearchValue row)
    {
        // LowerToLegacy is the production bridge: SearchExpressionQueryBuilder calls exactly this before
        // handing the tree to EF Core, so this is the shape real requests are answered with.
        var parameter = new SearchParameterInfo(
            "date",
            "date",
            SearchParamType.Date,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-date"));

        return Evaluate(
            LegacyExpressionLowerer.LowerToLegacy(
                new SearchParameterPredicateExpression(parameter, comparator, modifier: null, searchValue)),
            row);
    }

    private static bool Evaluate(Expression expression, DateTimeSearchValue row) => expression switch
    {
        MultiaryExpression { MultiaryOperation: MultiaryOperator.And } and => and.Expressions.All(x => Evaluate(x, row)),
        MultiaryExpression { MultiaryOperation: MultiaryOperator.Or } or => or.Expressions.Any(x => Evaluate(x, row)),
        BinaryExpression binary => Compare(binary, row),
        _ => throw new NotSupportedException($"The row evaluator does not model '{expression.GetType().Name}'."),
    };

    private static bool Compare(BinaryExpression binary, DateTimeSearchValue row)
    {
        var column = binary.FieldName switch
        {
            FieldName.DateTimeStart => row.Start,
            FieldName.DateTimeEnd => row.End,
            _ => throw new NotSupportedException($"Unexpected field '{binary.FieldName}'."),
        };
        var bound = (DateTimeOffset)binary.Value;

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
