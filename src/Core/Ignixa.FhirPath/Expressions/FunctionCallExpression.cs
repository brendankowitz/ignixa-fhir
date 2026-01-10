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
/// Represents a function call in a FhirPath expression.
/// Examples: exists(), where($this > 5), count()
/// </summary>
public class FunctionCallExpression : Expression
{
    public FunctionCallExpression(
        Expression? focus,
        string functionName,
        IEnumerable<Expression> arguments,
        ISourcePositionInfo? location = null) : base(location)
    {
        Focus = focus;
        FunctionName = functionName ?? throw new ArgumentNullException(nameof(functionName));
        Arguments = arguments?.ToList() ?? new List<Expression>();
    }

    /// <summary>The expression this function is called on (left of dot), or null for root context</summary>
    public Expression? Focus { get; }

    /// <summary>Name of the function being called</summary>
    public string FunctionName { get; }

    /// <summary>Arguments passed to the function</summary>
    public IReadOnlyList<Expression> Arguments { get; }

    public override string ToString() =>
        Focus != null
            ? $"{Focus}.{FunctionName}({string.Join(", ", Arguments)})"
            : $"{FunctionName}({string.Join(", ", Arguments)})";

    public override TOutput AcceptVisitor<TContext, TOutput>(
        IFhirPathExpressionVisitor<TContext, TOutput> visitor,
        TContext context) => visitor.VisitFunctionCall(this, context);
}
