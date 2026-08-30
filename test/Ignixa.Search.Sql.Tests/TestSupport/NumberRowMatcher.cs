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
/// Runs a number search all the way from a comparator and a search value to a yes/no answer about one indexed
/// [LowValue, HighValue] row, by lowering through the production rule and then interpreting the
/// <see cref="Predicate"/> it emits.
/// </summary>
/// <remarks>
/// The numeric counterpart to <see cref="DateRowMatcher"/>, and it exists for the same reason: no test using
/// this restates the FHIR prefix table. The verdict comes from <c>NumericRangeComparison</c> itself, so a test
/// asserting "this resource is found" stays honest if the prefix semantics are ever changed underneath it,
/// instead of quietly testing a copy of the old rules. The interpreter deliberately models only the comparison
/// node types numeric lowering can produce and throws on anything else, so a new node shape surfaces as a
/// failure rather than a silently wrong answer.
/// </remarks>
internal static class NumberRowMatcher
{
    public static bool Matches(SearchComparator comparator, decimal searchValue, (decimal Low, decimal High) row)
        => Evaluate(Lower(comparator, searchValue), row);

    public static Predicate Lower(SearchComparator comparator, decimal searchValue)
    {
        var parameter = new SearchParameterInfo(
            "probability",
            "probability",
            SearchParamType.Number,
            new Uri("http://hl7.org/fhir/SearchParameter/RiskAssessment-probability"));
        var context = new LeafContext(
            new SymbolTable(
                new Dictionary<string, short> { [parameter.Url.ToString()] = 204 },
                new Dictionary<string, short>()),
            approximationReferenceTime: null);
        var value = new NumberSearchValue(searchValue);
        var expression = new SearchParameterPredicateExpression(parameter, comparator, modifier: null, value);

        return NumberLoweringRule.Lower(expression, value, context, resourceTypeId: 104).Predicate
            .ShouldNotBeNull();
    }

    private static bool Evaluate(Predicate predicate, (decimal Low, decimal High) row) => predicate switch
    {
        Predicate.And and => Evaluate(and.Left, row) && Evaluate(and.Right, row),
        Predicate.Or or => Evaluate(or.Left, row) || Evaluate(or.Right, row),
        Predicate.LessThan lt => Column(lt.Column, row) < Bound(lt.Value),
        Predicate.LessThanOrEqual le => Column(le.Column, row) <= Bound(le.Value),
        Predicate.GreaterThan gt => Column(gt.Column, row) > Bound(gt.Value),
        Predicate.GreaterThanOrEqual ge => Column(ge.Column, row) >= Bound(ge.Value),
        _ => throw new NotSupportedException($"The row evaluator does not model '{predicate.GetType().Name}'."),
    };

    private static decimal Column(SqlColumnRef column, (decimal Low, decimal High) row) => column.Column switch
    {
        "LowValue" => row.Low,
        "HighValue" => row.High,
        _ => throw new NotSupportedException($"Unexpected column '{column.Column}'."),
    };

    private static decimal Bound(SqlParameterRef parameter) => (decimal)parameter.Value;
}
