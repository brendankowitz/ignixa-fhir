/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * AST Normalization Phase 3: PropertyAccessExpression
 * Eliminates ambiguity between property access and function calls.
 */

using Ignixa.FhirPath.Visitors;

namespace Ignixa.FhirPath.Expressions;

/// <summary>
/// Represents a property access in a FhirPath expression.
/// This expression type eliminates semantic ambiguity between property access and function calls.
/// Examples: name, family (when used at root level without parentheses)
/// </summary>
/// <remarks>
/// Previously, the parser emitted FunctionCallExpression for bare identifiers like "name",
/// treating them as name() with Focus = ScopeExpression.That and 0 arguments.
/// This created ambiguity because the evaluator needed special detection logic to
/// distinguish between property access and actual function calls.
///
/// With PropertyAccessExpression, the AST structure now matches FhirPath semantics:
/// - PropertyAccessExpression: property access on the current focus
/// - FunctionCallExpression: actual function calls with parentheses
/// </remarks>
public sealed class PropertyAccessExpression : Expression
{
    public PropertyAccessExpression(
        Expression? focus,
        string propertyName,
        ISourcePositionInfo? location = null) : base(location)
    {
        Focus = focus;
        PropertyName = propertyName ?? throw new ArgumentNullException(nameof(propertyName));
    }

    /// <summary>The expression this property is accessed on (left of dot), or null for implicit focus</summary>
    public Expression? Focus { get; }

    /// <summary>Name of the property being accessed</summary>
    public string PropertyName { get; }

    public override string ToString() =>
        Focus != null ? $"{Focus}.{PropertyName}" : PropertyName;

    public override TOutput AcceptVisitor<TContext, TOutput>(
        IFhirPathExpressionVisitor<TContext, TOutput> visitor,
        TContext context) => visitor.VisitPropertyAccess(this, context);
}
