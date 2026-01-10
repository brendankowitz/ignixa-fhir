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
/// Represents a scope reference in a FhirPath expression.
/// Examples: $this, $index, $total
/// </summary>
public class ScopeExpression : Expression
{
    public ScopeExpression(string scopeName, ISourcePositionInfo? location = null) : base(location)
    {
        ScopeName = scopeName ?? throw new ArgumentNullException(nameof(scopeName));
    }

    public string ScopeName { get; }

    public override string ToString() => $"Scope(${ScopeName})";

    // Common scope instances
    public static readonly ScopeExpression This = new("this");
    public static readonly ScopeExpression Index = new("index");
    public static readonly ScopeExpression Total = new("total");
    public static readonly ScopeExpression That = new("that");

    public override TOutput AcceptVisitor<TContext, TOutput>(
        IFhirPathExpressionVisitor<TContext, TOutput> visitor,
        TContext context) => visitor.VisitScope(this, context);
}
