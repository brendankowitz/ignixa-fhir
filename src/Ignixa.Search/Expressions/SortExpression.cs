// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Ignixa.Search.Models;

namespace Ignixa.Search.Expressions;

/// <summary>
/// Represents a sort parameter expression.
/// </summary>
public class SortExpression : SearchParameterExpressionBase
{
    public SortExpression(SearchParameterInfo searchParameter, SortOrder sortOrder = SortOrder.Ascending)
        : base(searchParameter)
    {
        SortOrder = sortOrder;
    }

    /// <summary>
    /// Gets the sort order (ascending or descending).
    /// </summary>
    public SortOrder SortOrder { get; }

    public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
    {
        EnsureArg.IsNotNull(visitor, nameof(visitor));
        return visitor.VisitSortParameter(this, context);
    }

    public override string ToString()
    {
        string prefix = SortOrder == SortOrder.Descending ? "-" : "";
        return $"(Sort Param: {prefix}{Parameter.Code})";
    }

    public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
    {
        hashCode.Add(typeof(SortExpression));
        hashCode.Add(Parameter);
        hashCode.Add(SortOrder);
    }

    public override bool ValueInsensitiveEquals(Expression other)
    {
        return other is SortExpression sortExpression
            && sortExpression.Parameter.Equals(Parameter)
            && sortExpression.SortOrder == SortOrder;
    }
}

/// <summary>
/// Specifies the sort order for a sort parameter.
/// </summary>
public enum SortOrder
{
    /// <summary>
    /// Sort in ascending order (a-z, 0-9, oldest-newest).
    /// </summary>
    Ascending,

    /// <summary>
    /// Sort in descending order (z-a, 9-0, newest-oldest).
    /// </summary>
    Descending
}
