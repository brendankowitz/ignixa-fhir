/*
 * Copyright (c) 2015, Firely (info@fire.ly) and contributors
 * Copyright (c) 2025, Ignixa Contributors
 *
 * This file is based on the Firely .NET SDK.
 * Licensed under the BSD 3-Clause license.
 */

using Ignixa.FhirPath.Visitors;

namespace Ignixa.FhirPath.Expressions;

/// <summary>
/// Represents member access / child navigation in a FhirPath expression.
/// Examples: Patient.name, name.given
/// </summary>
public class ChildExpression : FunctionCallExpression
{
    public ChildExpression(Expression? focus, string childName, ISourcePositionInfo? location = null)
        : base(focus, "builtin.children", new[] { new ConstantExpression(childName) }, location)
    {
        ChildName = childName;
    }

    public string ChildName { get; }

    public override string ToString() =>
        Focus != null ? $"{Focus}.{ChildName}" : ChildName;

    public override TOutput AcceptVisitor<TContext, TOutput>(
        IFhirPathExpressionVisitor<TContext, TOutput> visitor,
        TContext context) => visitor.VisitChild(this, context);
}
