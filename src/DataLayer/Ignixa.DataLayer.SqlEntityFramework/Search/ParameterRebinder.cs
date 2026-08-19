// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq.Expressions;

namespace Ignixa.DataLayer.SqlEntityFramework.Search;

/// <summary>
/// Replaces every reference to one lambda parameter with another, so two independently built lambda bodies
/// can be combined into a single well-formed lambda.
/// </summary>
internal sealed class ParameterRebinder(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
{
    protected override Expression VisitParameter(ParameterExpression node)
        => node == from ? to : base.VisitParameter(node);
}
