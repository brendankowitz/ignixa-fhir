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
/// Represents an empty collection literal in a FhirPath expression.
/// Example: {}
/// </summary>
public class EmptyExpression : Expression
{
    public EmptyExpression(ISourcePositionInfo? location = null) : base(location)
    {
    }

    public override string ToString() => "{}";

    public override TOutput AcceptVisitor<TContext, TOutput>(
        IFhirPathExpressionVisitor<TContext, TOutput> visitor,
        TContext context) => visitor.VisitEmpty(this, context);
}
