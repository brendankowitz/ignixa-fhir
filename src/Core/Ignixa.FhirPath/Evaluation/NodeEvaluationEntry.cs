/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * NodeEvaluationEntry for capturing per-node evaluation details during debug tracing.
 */

using System.Collections.Immutable;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Expressions;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// Captures evaluation details for a single expression node during debug tracing.
/// Used to build debug-trace output matching Firely/HAPI format.
/// </summary>
public sealed class NodeEvaluationEntry
{
    public NodeEvaluationEntry(
        Expression expression,
        ImmutableList<IElement> results,
        ImmutableList<IElement> focusElements,
        IElement? thisElement,
        int? index)
    {
        Expression = expression;
        Results = results;
        FocusElements = focusElements;
        ThisElement = thisElement;
        Index = index;
    }

    /// <summary>The expression node that was evaluated.</summary>
    public Expression Expression { get; }

    /// <summary>The result elements from evaluating this expression node.</summary>
    public ImmutableList<IElement> Results { get; }

    /// <summary>The focus (input) elements when this node was evaluated.</summary>
    public ImmutableList<IElement> FocusElements { get; }

    /// <summary>The $this element at the time of evaluation, if any.</summary>
    public IElement? ThisElement { get; }

    /// <summary>The $index value at the time of evaluation, if any.</summary>
    public int? Index { get; }

    /// <summary>
    /// Gets the key string in the format "position,length,name" matching Firely/HAPI output.
    /// </summary>
    public string GetKey()
    {
        var name = Expression switch
        {
            // These types derive from FunctionCallExpression, so match them first
            ChildExpression child => child.ChildName,
            BinaryExpression bin => bin.Operator,
            IndexerExpression => "[]",
            UnaryExpression unary => unary.Operator,
            PropertyAccessExpression prop => prop.PropertyName,
            FunctionCallExpression func => func.FunctionName,
            TemporalConstantExpression temporal => temporal.Literal,
            ConstantExpression constant => FormatConstantKey(constant.Value),
            ParenthesizedExpression => "()",
            ScopeExpression scope => $"${scope.ScopeName}",
            VariableRefExpression variable => $"%{variable.Name}",
            QuantityExpression => "QUANTITY",
            EmptyExpression => "{}",
            _ => Expression.GetType().Name.Replace("Expression", string.Empty, StringComparison.Ordinal).ToUpperInvariant()
        };

        if (Expression.Location != null)
        {
            return $"{Expression.Location.RawPosition},{Expression.Location.Length},{name}";
        }

        return name;
    }

    /// <summary>
    /// Renders a non-temporal constant's value as Firely and HAPI spell it in a trace key.
    /// </summary>
    /// <remarks>
    /// A string is always quoted here. Rendering an <c>@</c>-prefixed one bare, as the temporal form,
    /// spelled the string literal <c>'@x'</c> as <c>@x</c> - the same sigil sniff this type's caller
    /// stopped relying on, and one that made the trace claim a key parity it did not have. A temporal
    /// literal reaches its own arm in <see cref="GetKey"/> and never arrives here.
    /// </remarks>
    private static string FormatConstantKey(object? value)
    {
        return value switch
        {
            null => "null",
            string s => $"'{s}'",
            bool b => b ? "true" : "false",
            _ => string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}", value)
        };
    }
}
