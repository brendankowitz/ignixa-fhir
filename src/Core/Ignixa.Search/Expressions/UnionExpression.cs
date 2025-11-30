// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Ignixa.Search.Expressions;

/// <summary>
/// Represents a union expression that combines multiple expressions with UNION ALL semantics.
/// Used for compartment searches to generate efficient SQL UNION queries.
/// </summary>
public class UnionExpression : Expression
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnionExpression"/> class.
    /// </summary>
    /// <param name="unionOperator">The union operator (All or Distinct).</param>
    /// <param name="expressions">The expressions to union.</param>
    public UnionExpression(UnionOperator unionOperator, IReadOnlyList<Expression> expressions)
    {
        EnsureArg.IsNotNull(expressions, nameof(expressions));
        EnsureArg.IsTrue(expressions.Count > 0, nameof(expressions));
        EnsureArg.IsTrue(expressions.All(o => o != null), nameof(expressions));

        Operator = unionOperator;
        Expressions = expressions;
    }

    /// <summary>
    /// Gets the union operator (All or Distinct).
    /// </summary>
    public UnionOperator Operator { get; }

    /// <summary>
    /// Gets the expressions being unioned.
    /// </summary>
    public IReadOnlyList<Expression> Expressions { get; }

    public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
    {
        EnsureArg.IsNotNull(visitor, nameof(visitor));
        return visitor.VisitUnion(this, context);
    }

    public override string ToString()
    {
        return $"(Union {Operator} {string.Join(" ", Expressions)})";
    }

    public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
    {
        hashCode.Add(typeof(UnionExpression));
        hashCode.Add(Operator);
        foreach (Expression expression in Expressions)
        {
            expression.AddValueInsensitiveHashCode(ref hashCode);
        }
    }

    public override bool ValueInsensitiveEquals(Expression other)
    {
        if (other is not UnionExpression union || union.Operator != Operator || union.Expressions.Count != Expressions.Count)
        {
            return false;
        }

        for (int i = 0; i < Expressions.Count; i++)
        {
            if (!Expressions[i].ValueInsensitiveEquals(union.Expressions[i]))
            {
                return false;
            }
        }

        return true;
    }
}
