using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql.Tests.Lowering;

/// <summary>
/// Evaluates a lowered <see cref="Predicate"/> against a single in-memory row so a test can assert the
/// relation a comparator actually denotes, rather than only the AST shape it happens to build. That is
/// what makes a claim like "eq and ne are complements" testable: shape assertions can agree with each
/// other while both describe the wrong set of matching rows.
/// </summary>
internal static class PredicateRowEvaluator
{
    public static bool Matches(Predicate predicate, IReadOnlyDictionary<string, object> row) => predicate switch
    {
        Predicate.And and => Matches(and.Left, row) && Matches(and.Right, row),
        Predicate.Or or => Matches(or.Left, row) || Matches(or.Right, row),
        Predicate.Equal equal => Compare(row, equal.Column, equal.Value) == 0,
        Predicate.LessThan lessThan => Compare(row, lessThan.Column, lessThan.Value) < 0,
        Predicate.LessThanOrEqual lessThanOrEqual => Compare(row, lessThanOrEqual.Column, lessThanOrEqual.Value) <= 0,
        Predicate.GreaterThan greaterThan => Compare(row, greaterThan.Column, greaterThan.Value) > 0,
        Predicate.GreaterThanOrEqual greaterThanOrEqual => Compare(row, greaterThanOrEqual.Column, greaterThanOrEqual.Value) >= 0,
        Predicate.IsNull isNull => !row.ContainsKey(isNull.Column.Column),
        Predicate.False => false,
        _ => throw new NotSupportedException($"Row evaluation is not defined for '{predicate.GetType().Name}'."),
    };

    private static int Compare(IReadOnlyDictionary<string, object> row, SqlColumnRef column, SqlParameterRef parameter)
    {
        if (!row.TryGetValue(column.Column, out var stored))
        {
            throw new KeyNotFoundException($"The row under test has no '{column.Column}' column.");
        }

        return ((IComparable)stored).CompareTo(parameter.Value);
    }
}
