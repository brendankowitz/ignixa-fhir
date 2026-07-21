// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Expressions;
using Ignixa.Specification.ValueSets.Normative;

// Ported from fhir-server in a nullable-oblivious style (declare non-nullable, assign null). Kept
// oblivious rather than rewritten to satisfy this project's nullable context, to preserve the port.
#nullable disable

namespace Ignixa.DataLayer.SqlEntityFramework.Search;

/// <summary>
/// A SQL-index optimization: adds a redundant <c>DateTimeStart &lt;= y</c> bound to a containment-shaped
/// date predicate — <c>And(DateTimeStart &gt;= x, DateTimeEnd &lt;= y)</c> becomes
/// <c>And(DateTimeStart &gt;= x, DateTimeStart &lt;= y, DateTimeEnd &lt;= y)</c> — which constrains the
/// range scan over the <c>(DateTimeStart, DateTimeEnd)</c> index. Only the <c>:ap</c> comparator
/// produces the containment shape today; ordinary date equality uses an overlap shape
/// (<c>DateTimeStart &lt;= end AND DateTimeEnd &gt;= start</c>) that this rewriter deliberately leaves
/// untouched. It lives in the SQL layer, not the shared <see cref="LegacyExpressionLowerer"/> bridge,
/// because it is specific to the SQL search-index schema — other old-shape backends (e.g. CosmosDB) share
/// the bridge but must not inherit this SQL-only rewrite. Runs on the lowered field-level tree.
/// </summary>
public class DateTimeEqualityRewriter : ExpressionRewriterWithInitialContext<object>
{
    public static readonly DateTimeEqualityRewriter Instance = new();

    public override Expression VisitSearchParameter(SearchParameterExpression expression, object context)
    {
        if (expression.Parameter.Type == SearchParamType.Date ||
            expression.Parameter.Type == SearchParamType.Composite && expression.Parameter.Component.Any(c => c.ResolvedSearchParameter.Type == SearchParamType.Date))
            return base.VisitSearchParameter(expression, context);

        return expression;
    }

    public override Expression VisitMultiary(MultiaryExpression expression, object context)
    {
        expression = (MultiaryExpression)base.VisitMultiary(expression, context);
        if (expression.MultiaryOperation != MultiaryOperator.And) return expression;

        List<Expression> newExpressions = null;
        int i = 0;
        for (; i < expression.Expressions.Count - 1; i++)
            switch (MatchPattern(expression.Expressions[i], expression.Expressions[i + 1]))
            {
                case ({ } low, { } high):
                    EnsureAllocatedAndPopulated(ref newExpressions, expression.Expressions, i);

                    newExpressions.Add(low);
                    newExpressions.Add(new BinaryExpression(high.BinaryOperator, low.FieldName, high.ComponentIndex, high.Value));
                    newExpressions.Add(high);

                    i++;
                    break;
                default:
                    newExpressions?.Add(expression.Expressions[i]);
                    break;
            }

        if (newExpressions != null && i < expression.Expressions.Count)
            // add the last entry unless it was matched as a pattern above
            newExpressions.Add(expression.Expressions[^1]);

        return newExpressions == null ? expression : Expression.And(newExpressions);
    }

    private static (BinaryExpression low, BinaryExpression high) MatchPattern(Expression e1, Expression e2)
    {
        if (e1 is not BinaryExpression b1 || e2 is not BinaryExpression b2 || b1.ComponentIndex != b2.ComponentIndex) return default;

        if (b1 is { FieldName: FieldName.DateTimeStart, BinaryOperator: BinaryOperator.GreaterThan or BinaryOperator.GreaterThanOrEqual } &&
            b2 is { FieldName: FieldName.DateTimeEnd, BinaryOperator: BinaryOperator.LessThan or BinaryOperator.LessThanOrEqual })
            return (b1, b2);

        if (b2 is { FieldName: FieldName.DateTimeStart, BinaryOperator: BinaryOperator.GreaterThan or BinaryOperator.GreaterThanOrEqual } &&
            b1 is { FieldName: FieldName.DateTimeEnd, BinaryOperator: BinaryOperator.LessThan or BinaryOperator.LessThanOrEqual })
            return (b2, b1);

        return default;
    }
}
