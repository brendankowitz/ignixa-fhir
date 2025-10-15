/*
 * Copyright (c) 2025, Sparky Contributors
 *
 * FhirPath expression evaluator.
 * Executes parsed FhirPath AST against ITypedElement trees.
 */

using Sparky.FhirPath.Expressions;
using Sparky.SourceNodeSerialization.ElementModel;
using Sparky.SourceNodeSerialization.Specification;

namespace Sparky.FhirPath.Evaluation;

/// <summary>
/// Evaluates FhirPath expressions against FHIR resources represented as ITypedElement trees.
/// </summary>
public class FhirPathEvaluator
{
    /// <summary>
    /// Evaluates a FhirPath expression against an input element and returns matching elements.
    /// </summary>
    /// <param name="input">The root element to evaluate against</param>
    /// <param name="expression">The parsed FhirPath expression</param>
    /// <param name="context">Optional evaluation context</param>
    /// <returns>Collection of elements that match the expression</returns>
    public IEnumerable<ITypedElement> Evaluate(ITypedElement input, Expression expression, EvaluationContext? context = null)
    {
        context ??= new EvaluationContext();

        return EvaluateExpression(new[] { input }, expression, context);
    }

    private IEnumerable<ITypedElement> EvaluateExpression(IEnumerable<ITypedElement> focus, Expression expr, EvaluationContext context)
    {
        return expr switch
        {
            // Check specific types before base types (ChildExpression/BinaryExpression/UnaryExpression/IndexerExpression inherit from FunctionCallExpression)
            ChildExpression child => EvaluateChildExpression(focus, child, context),
            BinaryExpression binary => EvaluateBinaryExpression(focus, binary, context),
            UnaryExpression unary => EvaluateUnary(focus, unary, context),
            IndexerExpression indexer => EvaluateIndexer(focus, indexer, context),
            FunctionCallExpression func => EvaluateFunctionCall(focus, func, context),
            ConstantExpression constant => EvaluateConstant(constant),
            AxisExpression axis => EvaluateAxis(focus, axis, context),
            IdentifierExpression id => EvaluateIdentifier(focus, id),
            VariableRefExpression var => EvaluateVariable(var, context),
            ParenthesizedExpression paren => EvaluateExpression(focus, paren.InnerExpression, context),
            EmptyExpression => Enumerable.Empty<ITypedElement>(),
            QuantityExpression => throw new NotImplementedException("Quantity literals not yet supported in evaluation"),
            _ => throw new NotSupportedException($"Expression type {expr.GetType().Name} is not yet supported")
        };
    }

    private IEnumerable<ITypedElement> EvaluateChildExpression(IEnumerable<ITypedElement> focus, ChildExpression child, EvaluationContext context)
    {
        // First evaluate the focus expression if present
        var focusElements = child.Focus != null
            ? EvaluateExpression(focus, child.Focus, context)
            : focus;

        // Then navigate to children with the specified name
        foreach (var element in focusElements)
        {
            foreach (var childElement in element.Children(child.ChildName))
            {
                yield return childElement;
            }
        }
    }

    private IEnumerable<ITypedElement> EvaluateFunctionCall(IEnumerable<ITypedElement> focus, FunctionCallExpression func, EvaluationContext context)
    {
        // Evaluate focus first
        var focusElements = func.Focus != null
            ? EvaluateExpression(focus, func.Focus, context)
            : focus;

        // Handle built-in functions
        // FhirPath function names are case-insensitive, ToLowerInvariant is intentional
#pragma warning disable CA1308 // Normalize strings to uppercase
        return func.FunctionName.ToLowerInvariant() switch
#pragma warning restore CA1308 // Normalize strings to uppercase
        {
            "exists" => EvaluateExists(focusElements, func.Arguments, context),
            "empty" => EvaluateEmpty(focusElements),
            "count" => EvaluateCount(focusElements),
            "first" => EvaluateFirst(focusElements),
            "last" => EvaluateLast(focusElements),
            "where" => EvaluateWhere(focusElements, func.Arguments, context),
            "select" => EvaluateSelect(focusElements, func.Arguments, context),
            "all" => EvaluateAll(focusElements, func.Arguments, context),
            "any" => EvaluateAny(focusElements, func.Arguments, context),
            "distinct" => focusElements.Distinct(),

            // For bare identifiers (e.g., "Patient"), treat as child navigation
            _ when func.Arguments.Count == 0 && func.Focus == AxisExpression.That
                => EvaluateIdentifier(focus, new IdentifierExpression(func.FunctionName)),

            _ => throw new NotSupportedException($"Function '{func.FunctionName}' is not yet implemented")
        };
    }

    private IEnumerable<ITypedElement> EvaluateIdentifier(IEnumerable<ITypedElement> focus, IdentifierExpression id)
    {
        // Identifiers navigate to child elements
        foreach (var element in focus)
        {
            foreach (var child in element.Children(id.Name))
            {
                yield return child;
            }
        }
    }

    private IEnumerable<ITypedElement> EvaluateExists(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        var hasCriteria = arguments.Count > 0;
        bool exists;

        if (hasCriteria)
        {
            // exists(criteria): returns true if any element matches the criteria
            exists = focus.Any(element =>
            {
                var result = EvaluateExpression(new[] { element }, arguments[0], context);
                return result.Any() && IsTrue(result);
            });
        }
        else
        {
            // exists(): returns true if collection is not empty
            exists = focus.Any();
        }

        return exists ? new[] { CreateBoolean(true) } : Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateEmpty(IEnumerable<ITypedElement> focus)
    {
        var isEmpty = !focus.Any();
        return isEmpty ? new[] { CreateBoolean(true) } : Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateCount(IEnumerable<ITypedElement> focus)
    {
        var count = focus.Count();
        return new[] { CreateInteger(count) };
    }

    private IEnumerable<ITypedElement> EvaluateFirst(IEnumerable<ITypedElement> focus)
    {
        var first = focus.FirstOrDefault();
        return first != null ? new[] { first } : Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateLast(IEnumerable<ITypedElement> focus)
    {
        var last = focus.LastOrDefault();
        return last != null ? new[] { last } : Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateWhere(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("where() requires a criteria argument");

        var criteria = arguments[0];

        foreach (var element in focus)
        {
            // Evaluate criteria with $this bound to current element
            var oldThis = context.GetEnvironmentVariable("this");
            context.SetEnvironmentVariable("this", element);

            try
            {
                var result = EvaluateExpression(new[] { element }, criteria, context);
                if (result.Any() && IsTrue(result))
                {
                    yield return element;
                }
            }
            finally
            {
                if (oldThis != null)
                    context.SetEnvironmentVariable("this", oldThis);
                else
                    context.RemoveEnvironmentVariable("this");
            }
        }
    }

    private IEnumerable<ITypedElement> EvaluateSelect(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("select() requires a projection argument");

        var projection = arguments[0];

        foreach (var element in focus)
        {
            foreach (var result in EvaluateExpression(new[] { element }, projection, context))
            {
                yield return result;
            }
        }
    }

    private IEnumerable<ITypedElement> EvaluateAll(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("all() requires a criteria argument");

        var criteria = arguments[0];
        var allMatch = focus.All(element =>
        {
            var result = EvaluateExpression(new[] { element }, criteria, context);
            return result.Any() && IsTrue(result);
        });

        return allMatch ? new[] { CreateBoolean(true) } : Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateAny(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments, EvaluationContext context)
    {
        if (arguments.Count == 0)
        {
            // any() without criteria: returns true if collection is not empty
            return focus.Any() ? new[] { CreateBoolean(true) } : Enumerable.Empty<ITypedElement>();
        }

        var criteria = arguments[0];
        var anyMatch = focus.Any(element =>
        {
            var result = EvaluateExpression(new[] { element }, criteria, context);
            return result.Any() && IsTrue(result);
        });

        return anyMatch ? new[] { CreateBoolean(true) } : Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateBinaryExpression(IEnumerable<ITypedElement> focus, BinaryExpression binary, EvaluationContext context)
    {
        var left = EvaluateExpression(focus, binary.Left, context).ToList();
        var right = EvaluateExpression(focus, binary.Right, context).ToList();

        // FhirPath operators are case-insensitive, ToLowerInvariant is intentional
#pragma warning disable CA1308 // Normalize strings to uppercase
        bool result = binary.Operator.ToLowerInvariant() switch
#pragma warning restore CA1308 // Normalize strings to uppercase
        {
            "=" => CompareEquality(left, right, equals: true),
            "!=" => CompareEquality(left, right, equals: false),
            ">" => CompareOrder(left, right, greater: true, orEqual: false),
            ">=" => CompareOrder(left, right, greater: true, orEqual: true),
            "<" => CompareOrder(left, right, greater: false, orEqual: false),
            "<=" => CompareOrder(left, right, greater: false, orEqual: true),
            "and" => IsTrue(left) && IsTrue(right),
            "or" => IsTrue(left) || IsTrue(right),
            _ => throw new NotSupportedException($"Binary operator '{binary.Operator}' is not yet implemented")
        };

        return result ? new[] { CreateBoolean(true) } : Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateAxis(IEnumerable<ITypedElement> focus, AxisExpression axis, EvaluationContext context)
    {
        // FhirPath axis names are case-insensitive, ToLowerInvariant is intentional
#pragma warning disable CA1308 // Normalize strings to uppercase
        return axis.AxisName.ToLowerInvariant() switch
#pragma warning restore CA1308 // Normalize strings to uppercase
        {
            "this" => context.GetEnvironmentVariable("this") is ITypedElement thisElement
                ? new[] { thisElement }
                : focus,
            "that" => focus,
            _ => throw new NotSupportedException($"Axis '${axis.AxisName}' is not yet implemented")
        };
    }

    private IEnumerable<ITypedElement> EvaluateVariable(VariableRefExpression var, EvaluationContext context)
    {
        var value = context.GetEnvironmentVariable(var.Name);
        return value is ITypedElement element ? new[] { element } : Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateConstant(ConstantExpression constant)
    {
        return new[] { CreateConstant(constant.Value) };
    }

    private IEnumerable<ITypedElement> EvaluateIndexer(IEnumerable<ITypedElement> focus, IndexerExpression indexer, EvaluationContext context)
    {
        var collection = EvaluateExpression(focus, indexer.Collection, context).ToList();
        var indexResults = EvaluateExpression(focus, indexer.Index, context).ToList();

        if (indexResults.Count == 1 && indexResults[0].Value is int index)
        {
            if (index >= 0 && index < collection.Count)
            {
                return new[] { collection[index] };
            }
        }

        return Enumerable.Empty<ITypedElement>();
    }

    private IEnumerable<ITypedElement> EvaluateUnary(IEnumerable<ITypedElement> focus, UnaryExpression unary, EvaluationContext context)
    {
        var operand = EvaluateExpression(focus, unary.Operand, context).ToList();

        if (unary.Operator == "-" && operand.Count == 1 && operand[0].Value is IConvertible value)
        {
            try
            {
                var numeric = Convert.ToDecimal(value);
                return new[] { CreateDecimal(-numeric) };
            }
            catch
            {
                // Ignore conversion errors
            }
        }

        return operand;
    }

    // Helper methods for type conversions and comparisons
    private bool IsTrue(IEnumerable<ITypedElement> elements)
    {
        var list = elements.ToList();
        return list.Count == 1 && list[0].Value is bool b && b;
    }

    private bool CompareEquality(List<ITypedElement> left, List<ITypedElement> right, bool equals)
    {
        if (left.Count != right.Count) return !equals;
        if (left.Count == 0) return !equals; // Empty collections are not equal

        for (int i = 0; i < left.Count; i++)
        {
            var isEqual = AreEqual(left[i].Value, right[i].Value);
            if (isEqual != equals) return false;
        }

        return true;
    }

    private bool CompareOrder(List<ITypedElement> left, List<ITypedElement> right, bool greater, bool orEqual)
    {
        if (left.Count != 1 || right.Count != 1) return false;

        var leftValue = left[0].Value;
        var rightValue = right[0].Value;

        if (leftValue is IComparable leftComparable && rightValue is IComparable rightComparable)
        {
            try
            {
                var comparison = leftComparable.CompareTo(rightComparable);
                return greater
                    ? (orEqual ? comparison >= 0 : comparison > 0)
                    : (orEqual ? comparison <= 0 : comparison < 0);
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private bool AreEqual(object? left, object? right)
    {
        if (left == null && right == null) return true;
        if (left == null || right == null) return false;
        return left.Equals(right);
    }

    // Factory methods for creating primitive ITypedElement instances
    private ITypedElement CreateBoolean(bool value) => new PrimitiveElement(value, "boolean");
    private ITypedElement CreateInteger(int value) => new PrimitiveElement(value, "integer");
    private ITypedElement CreateDecimal(decimal value) => new PrimitiveElement(value, "decimal");
    // FHIR type names are lowercase in FhirPath, ToLowerInvariant is intentional
#pragma warning disable CA1308 // Normalize strings to uppercase
    private ITypedElement CreateConstant(object value) => new PrimitiveElement(value, value.GetType().Name.ToLowerInvariant());
#pragma warning restore CA1308 // Normalize strings to uppercase

    /// <summary>
    /// Simple implementation of ITypedElement for primitive values.
    /// </summary>
    private class PrimitiveElement : ITypedElement
    {
        public PrimitiveElement(object value, string type)
        {
            Value = value;
            InstanceType = type;
        }

        public string Name => string.Empty;
        public string InstanceType { get; }
        public object Value { get; }
        public string Location => string.Empty;
        public IElementDefinitionSummary? Definition => null;

        public IEnumerable<ITypedElement> Children(string? name = null) => Enumerable.Empty<ITypedElement>();
    }
}
