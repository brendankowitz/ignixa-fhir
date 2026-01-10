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
/// Represents a variable reference in a FhirPath expression.
/// Examples: %context, %resource, %ext-id
/// </summary>
public class VariableRefExpression : Expression
{
    public VariableRefExpression(string name, ISourcePositionInfo? location = null) : base(location)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public string Name { get; }

    public override string ToString() => $"Variable(%{Name})";

    public override TOutput AcceptVisitor<TContext, TOutput>(
        IFhirPathExpressionVisitor<TContext, TOutput> visitor,
        TContext context) => visitor.VisitVariable(this, context);
}
