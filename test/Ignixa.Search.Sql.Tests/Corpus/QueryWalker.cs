using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Ignixa.Search.Sql.Tests.Corpus;

/// <summary>
/// Recursive descent over a query expression, recording tables, semantic filters, and set operations
/// into a <see cref="QueryScope"/>. Subqueries (EXISTS bodies, NOT IN bodies) fold into the same scope
/// with a "sub:" marker, because that is where the shipping engine puts semantics the compiler puts in
/// a CTE of its own.
/// </summary>
internal static class QueryWalker
{
    public static void Walk(QueryExpression query, QueryScope scope)
    {
        switch (query)
        {
            case QueryParenthesisExpression parenthesis:
                Walk(parenthesis.QueryExpression, scope);
                break;

            case BinaryQueryExpression binary:
                var setOperator = binary.BinaryQueryExpressionType switch
                {
                    BinaryQueryExpressionType.Union => "union",
                    BinaryQueryExpressionType.Except => "except",
                    BinaryQueryExpressionType.Intersect => "intersect",
                    _ => "set-op",
                };
                scope.Operations.Add(binary.All ? setOperator + "-all" : setOperator);
                Walk(binary.FirstQueryExpression, scope);
                Walk(binary.SecondQueryExpression, scope);
                break;

            case QuerySpecification specification:
                WalkSpecification(specification, scope);
                break;
        }
    }

    private static void WalkSpecification(QuerySpecification specification, QueryScope scope)
    {
        if (specification.UniqueRowFilter == UniqueRowFilter.Distinct)
        {
            scope.Operations.Add("distinct");
        }

        if (specification.TopRowFilter is not null)
        {
            scope.Operations.Add("top");
        }

        if (specification.OrderByClause is not null)
        {
            scope.Operations.Add("order-by");
        }

        foreach (var element in specification.SelectElements)
        {
            WalkSelectElement(element, scope);
        }

        if (specification.FromClause is not null)
        {
            foreach (var reference in specification.FromClause.TableReferences)
            {
                WalkTableReference(reference, scope);
            }
        }

        if (specification.WhereClause is not null)
        {
            WalkBoolean(specification.WhereClause.SearchCondition, scope, nested: false);
        }
    }

    private static void WalkSelectElement(SelectElement element, QueryScope scope)
    {
        if (element is not SelectScalarExpression scalar)
        {
            return;
        }

        foreach (var function in Descendants<FunctionCall>(scalar))
        {
            if (AggregateName(function) is { } name)
            {
                scope.Operations.Add(name);
            }
        }
    }

    private static void WalkTableReference(TableReference reference, QueryScope scope)
    {
        switch (reference)
        {
            case NamedTableReference named:
                scope.AddTableOrDependency(Name(named.SchemaObject));
                break;

            case QualifiedJoin join:
                scope.Operations.Add(join.QualifiedJoinType == QualifiedJoinType.Inner ? "inner-join" : "outer-join");
                WalkTableReference(join.FirstTableReference, scope);
                WalkTableReference(join.SecondTableReference, scope);
                if (join.SearchCondition is not null)
                {
                    WalkBoolean(join.SearchCondition, scope, nested: false);
                }

                break;

            case QueryDerivedTable derived:
                Walk(derived.QueryExpression, scope);
                break;

            case JoinParenthesisTableReference parenthesis:
                WalkTableReference(parenthesis.Join, scope);
                break;
        }
    }

    private static void WalkBoolean(BooleanExpression expression, QueryScope scope, bool nested)
    {
        switch (expression)
        {
            case BooleanParenthesisExpression parenthesis:
                WalkBoolean(parenthesis.Expression, scope, nested);
                break;

            case BooleanBinaryExpression binary:
                if (binary.BinaryExpressionType == BooleanBinaryExpressionType.Or)
                {
                    scope.Operations.Add("or");
                }

                WalkBoolean(binary.FirstExpression, scope, nested);
                WalkBoolean(binary.SecondExpression, scope, nested);
                break;

            case BooleanNotExpression not:
                scope.Operations.Add("not");
                WalkBoolean(not.Expression, scope, nested);
                break;

            case BooleanComparisonExpression comparison:
                AddComparison(comparison, scope, nested);
                break;

            case BooleanIsNullExpression isNull:
                scope.Filters.Add(Prefix(nested) + $"{Operand(isNull.Expression)} is{(isNull.IsNot ? "-not" : string.Empty)}-null");
                break;

            case ExistsPredicate exists:
                scope.Operations.Add("exists");
                WalkSubquery(exists.Subquery, scope);
                break;

            case InPredicate inPredicate:
                scope.Operations.Add(inPredicate.NotDefined ? "not-in" : "in");
                if (inPredicate.Subquery is not null)
                {
                    WalkSubquery(inPredicate.Subquery, scope);
                }

                break;

            case LikePredicate like:
                scope.Filters.Add(Prefix(nested) + $"{Operand(like.FirstExpression)} {(like.NotDefined ? "not-like" : "like")} {Operand(like.SecondExpression)}");
                break;

            case BooleanTernaryExpression ternary:
                scope.Filters.Add(Prefix(nested) + $"{Operand(ternary.FirstExpression)} between");
                break;
        }
    }

    private static void WalkSubquery(ScalarSubquery subquery, QueryScope scope)
        => WalkSubquery(subquery.QueryExpression, scope);

    private static void WalkSubquery(QueryExpression query, QueryScope scope)
    {
        var inner = new QueryScope();
        Walk(query, inner);

        scope.Tables.AddRange(inner.Tables.Select(t => "sub:" + t));
        scope.Dependencies.AddRange(inner.Dependencies);
        scope.Filters.AddRange(inner.Filters.Select(f => f.StartsWith("sub:", StringComparison.Ordinal) ? f : "sub:" + f));
        scope.Operations.AddRange(inner.Operations.Where(o => o is not ("distinct" or "top" or "order-by")));
    }

    private static void AddComparison(BooleanComparisonExpression comparison, QueryScope scope, bool nested)
    {
        var left = Operand(comparison.FirstExpression);
        var right = Operand(comparison.SecondExpression);

        // Column-to-column equality is how a query stitches rows of one relation to another. The two
        // dialects do that differently for identical semantics, so it is plumbing, not a filter.
        if (left.StartsWith("col:", StringComparison.Ordinal) && right.StartsWith("col:", StringComparison.Ordinal))
        {
            scope.Operations.Add("correlate");
            return;
        }

        var column = left.StartsWith("col:", StringComparison.Ordinal) ? left["col:".Length..] : left;
        scope.Filters.Add(Prefix(nested) + $"{column} {Symbol(comparison.ComparisonType)} {right}");
    }

    private static string Prefix(bool nested) => nested ? "sub:" : string.Empty;

    /// <summary>Names the aggregates worth reporting, case-insensitively -- the two dialects differ in casing.</summary>
    private static string? AggregateName(FunctionCall function)
    {
        var name = function.FunctionName.Value;
        if (string.Equals(name, "count_big", StringComparison.OrdinalIgnoreCase))
        {
            return "count-big";
        }

        if (string.Equals(name, "count", StringComparison.OrdinalIgnoreCase))
        {
            return "count";
        }

        return string.Equals(name, "row_number", StringComparison.OrdinalIgnoreCase) ? "row-number" : null;
    }

    private static string FunctionOperand(FunctionCall function)
        => "fn:" + (AggregateName(function) ?? function.FunctionName.Value);

    private static string Operand(ScalarExpression expression) => expression switch
    {
        ColumnReferenceExpression column => "col:" + column.MultiPartIdentifier.Identifiers[^1].Value,
        IntegerLiteral or NumericLiteral or RealLiteral => "<n>",
        StringLiteral => "<s>",
        BinaryLiteral => "<b>",
        NullLiteral => "<null>",
        VariableReference => "@p",
        FunctionCall function => FunctionOperand(function),
        ConvertCall => "fn:convert",
        CastCall => "fn:cast",
        UnaryExpression unary => Operand(unary.Expression),
        BinaryExpression binary => $"({Operand(binary.FirstExpression)} op {Operand(binary.SecondExpression)})",
        _ => "<expr>",
    };

    private static string Symbol(BooleanComparisonType type) => type switch
    {
        BooleanComparisonType.Equals => "=",
        BooleanComparisonType.NotEqualToBrackets or BooleanComparisonType.NotEqualToExclamation => "<>",
        BooleanComparisonType.GreaterThan => ">",
        BooleanComparisonType.GreaterThanOrEqualTo => ">=",
        BooleanComparisonType.LessThan => "<",
        BooleanComparisonType.LessThanOrEqualTo => "<=",
        _ => type.ToString(),
    };

    private static string Name(SchemaObjectName name) => name.SchemaIdentifier is null
        ? name.BaseIdentifier.Value
        : $"{name.SchemaIdentifier.Value}.{name.BaseIdentifier.Value}";

    private static IEnumerable<T> Descendants<T>(TSqlFragment fragment)
        where T : TSqlFragment
    {
        var found = new List<T>();
        fragment.Accept(new Collector<T>(found));
        return found;
    }

    private sealed class Collector<T>(List<T> found) : TSqlFragmentVisitor
        where T : TSqlFragment
    {
        public override void Visit(TSqlFragment node)
        {
            if (node is T typed)
            {
                found.Add(typed);
            }
        }
    }
}
