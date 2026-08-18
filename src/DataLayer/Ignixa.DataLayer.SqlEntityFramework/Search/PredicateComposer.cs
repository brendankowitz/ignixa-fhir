// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq.Expressions;

namespace Ignixa.DataLayer.SqlEntityFramework.Search;

/// <summary>
/// Combines two predicate lambdas over the same entity into one.
/// <para>
/// Chaining <c>IQueryable.Where</c> already conjoins predicates, so this exists for the cases chaining cannot
/// express: a disjunction, and a conjunction that has to be nested inside one. Each operand is built as its
/// own lambda so the values it compares against stay captured variables, which is what makes EF Core
/// parameterize them instead of inlining literals into the SQL.
/// </para>
/// </summary>
internal static class PredicateComposer
{
    public static Expression<Func<T, bool>> And<T>(Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
        => Compose(left, right, Expression.AndAlso);

    public static Expression<Func<T, bool>> Or<T>(Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
        => Compose(left, right, Expression.OrElse);

    private static Expression<Func<T, bool>> Compose<T>(
        Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right,
        Func<Expression, Expression, BinaryExpression> combine)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        // The two lambdas were built independently and so declare distinct parameter instances. A combined
        // body may only reference one of them, or the resulting lambda is unbound and EF Core cannot translate it.
        ParameterExpression parameter = left.Parameters[0];
        Expression reboundRight = new ParameterRebinder(right.Parameters[0], parameter).Visit(right.Body);

        return Expression.Lambda<Func<T, bool>>(combine(left.Body, reboundRight), parameter);
    }
}
