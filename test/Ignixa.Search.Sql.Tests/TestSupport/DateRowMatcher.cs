using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Lowering.Leaf;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Search.Sql.Tests.TestSupport;

/// <summary>
/// Runs a date search all the way from a comparator and a search value to a yes/no answer about one indexed
/// [StartDateTime, EndDateTime] row, by lowering through the production rule and then interpreting the
/// <see cref="Predicate"/> it emits.
/// </summary>
/// <remarks>
/// The point is that no test using this restates the FHIR prefix table. The verdict comes from
/// <c>DateTimeRangeComparison</c> itself, so a test asserting "this resource is found" stays honest if the
/// prefix semantics are ever changed underneath it, instead of quietly testing a copy of the old rules.
/// The interpreter deliberately models only the comparison node types date lowering can produce and throws
/// on anything else, so a new node shape surfaces as a failure rather than a silently wrong answer.
/// </remarks>
internal static class DateRowMatcher
{
    // ap widens the search value by a tolerance derived from its distance to "now", so it needs a fixed
    // reference instant to be a test rather than a coin flip.
    public static readonly DateTimeOffset ApproximationReference = new(2016, 2, 7, 13, 28, 17, TimeSpan.Zero);

    public static bool Matches(SearchComparator comparator, DateTimeSearchValue searchValue, DateTimeSearchValue row)
        => Evaluate(Lower(comparator, searchValue), row);

    public static Predicate Lower(SearchComparator comparator, DateTimeSearchValue searchValue)
    {
        var parameter = new SearchParameterInfo(
            "date",
            "date",
            SearchParamType.Date,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-date"));
        var context = new LeafContext(
            new SymbolTable(
                new Dictionary<string, short> { [parameter.Url.ToString()] = 203 },
                new Dictionary<string, short>()),
            ApproximationReference);
        var expression = new SearchParameterPredicateExpression(parameter, comparator, modifier: null, searchValue);

        return DateTimeLoweringRule.Lower(expression, searchValue, context, resourceTypeId: 103).Predicate
            .ShouldNotBeNull();
    }

    private static bool Evaluate(Predicate predicate, DateTimeSearchValue row) => predicate switch
    {
        Predicate.And and => Evaluate(and.Left, row) && Evaluate(and.Right, row),
        Predicate.Or or => Evaluate(or.Left, row) || Evaluate(or.Right, row),
        Predicate.LessThan lt => Column(lt.Column, row) < Bound(lt.Value),
        Predicate.LessThanOrEqual le => Column(le.Column, row) <= Bound(le.Value),
        Predicate.GreaterThan gt => Column(gt.Column, row) > Bound(gt.Value),
        Predicate.GreaterThanOrEqual ge => Column(ge.Column, row) >= Bound(ge.Value),
        _ => throw new NotSupportedException($"The row evaluator does not model '{predicate.GetType().Name}'."),
    };

    private static DateTimeOffset Column(SqlColumnRef column, DateTimeSearchValue row) => column.Column switch
    {
        "StartDateTime" => row.Start,
        "EndDateTime" => row.End,
        _ => throw new NotSupportedException($"Unexpected column '{column.Column}'."),
    };

    private static DateTimeOffset Bound(SqlParameterRef parameter) => (DateTimeOffset)parameter.Value;
}
