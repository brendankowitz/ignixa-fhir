// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Ignixa.Search.Expressions;

/// <summary>
/// Represents a union expression that combines multiple independent row-producing legs. Intended for
/// compartment searches; instances are created only through
/// <see cref="Expression.Union(UnionOperator, IReadOnlyList{Expression})"/>, so that factory's call sites
/// are the authoritative answer to which binders, if any, produce one.
/// <para>
/// Every consumer emits a <em>deduplicated</em> union regardless of <see cref="Operator"/>: the SQL lowering,
/// the Entity Framework query builder and the in-memory interpreter all dedupe. That is not a shortcut —
/// a leg yields <c>(ResourceTypeId, ResourceSurrogateId)</c> identities, so a duplicate is the same row
/// admitted by two legs and never two distinct results. <c>UNION ALL</c> would therefore double-count that
/// row in <c>_total</c> and let it consume two slots of a page, which makes the distinct union the only
/// correct emission for either operator.
/// </para>
/// <para>
/// <see cref="Operator"/> is nonetheless load-bearing outside lowering: it participates in
/// <see cref="ValueInsensitiveEquals"/> and <see cref="AddValueInsensitiveHashCode"/>, so it is part of the
/// node's structural identity, and it is rendered by <c>IrProjector</c> and <see cref="ToString"/>. Removing
/// it would collapse two structurally distinct nodes and drop a field from the diagnostics.
/// </para>
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
