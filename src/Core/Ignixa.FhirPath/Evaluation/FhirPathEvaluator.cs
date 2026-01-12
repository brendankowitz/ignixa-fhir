/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath expression evaluator.
 * Executes parsed FhirPath AST against IElement trees.
 * Uses immutable EvaluationContext for pure functional evaluation.
 */

using System.Collections.Immutable;
using Ignixa.FhirPath.Expressions;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation.Functions;
using Ignixa.FhirPath.Visitors;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// Evaluates FhirPath expressions against FHIR resources represented as IElement trees.
/// </summary>
/// <remarks>
/// <para>
/// This class is partial - the <see cref="DispatchFunctionCall"/> method is auto-generated
/// by <c>FhirPathFunctionGenerator</c> based on <c>[FhirPathFunction]</c> attributes.
/// </para>
/// <para>
/// <b>Immutable Context Pattern:</b>
/// All visitor methods are pure functions with respect to context. The <see cref="EvaluationContext"/>
/// is immutable, and each method creates new context instances as needed via fluent methods
/// like <see cref="EvaluationContext.WithFocus"/> and <see cref="EvaluationContext.PushThis"/>.
/// </para>
/// </remarks>
public partial class FhirPathEvaluator : IFhirPathExpressionVisitor<EvaluationContext, IEnumerable<IElement>>
{
    /// <summary>
    /// Creates a new FhirPath evaluator.
    /// </summary>
    public FhirPathEvaluator()
    {
    }

    /// <summary>
    /// Evaluates a FhirPath expression against an input element and returns matching elements.
    /// </summary>
    /// <param name="input">The root element to evaluate against</param>
    /// <param name="expression">The parsed FhirPath expression</param>
    /// <param name="context">Optional evaluation context</param>
    /// <returns>Collection of elements that match the expression</returns>
    /// <remarks>
    /// For best performance, use a <see cref="Parser.FhirPathParser"/> with <see cref="Parsing.CompilationOptions.Optimize"/>
    /// set to true to optimize expressions at parse-time rather than evaluation-time.
    /// </remarks>
    public IEnumerable<IElement> Evaluate(IElement input, Expression expression, EvaluationContext? context = null)
    {
        context ??= new EvaluationContext();
        return EvaluateExpression([input], expression, context);
    }

    private IEnumerable<IElement> EvaluateExpression(IEnumerable<IElement> focus, Expression expr, EvaluationContext context)
    {
        // Optimization: Skip context creation if focus hasn't changed
        // This is common in indexer/child/binary expressions where we evaluate sub-expressions with the same focus
        if (ReferenceEquals(focus, context.Focus))
        {
            return expr.AcceptVisitor(this, context);
        }

        var newContext = context.WithFocus(focus);
        return expr.AcceptVisitor(this, newContext);
    }

    public IEnumerable<IElement> VisitChild(ChildExpression expression, EvaluationContext context)
    {
        var focusElements = expression.Focus != null
            ? EvaluateExpression(context.Focus, expression.Focus, context)
            : context.Focus;

        foreach (var element in focusElements)
        {
            foreach (var childElement in element.Children(expression.ChildName))
            {
                yield return childElement;
            }
        }
    }

    public IEnumerable<IElement> VisitFunctionCall(FunctionCallExpression expression, EvaluationContext context)
    {
        var focusElements = expression.Focus != null
            ? EvaluateExpression(context.Focus, expression.Focus, context)
            : context.Focus;

        if (expression.FunctionName.Equals("defineVariable", StringComparison.OrdinalIgnoreCase))
        {
            return EvaluateDefineVariable(expression, focusElements, context);
        }

        return DispatchFunctionCall(expression.FunctionName, focusElements, expression.Arguments, context);
    }

    /// <summary>
    /// Evaluates defineVariable() function - defines a variable that can be referenced later.
    /// Per FHIRPath 2.0 spec, the variable is available for the remainder of the expression.
    /// Uses a mutable dictionary in the context to allow side effects while keeping context immutable.
    /// </summary>
    private IEnumerable<IElement> EvaluateDefineVariable(FunctionCallExpression expression, IEnumerable<IElement> focus, EvaluationContext context)
    {
        if (expression.Arguments.Count is < 1 or > 2)
        {
            throw new InvalidOperationException("defineVariable requires 1 or 2 arguments: variable name and optional value expression");
        }

        var nameExpr = expression.Arguments[0];
        string? variableName = null;

        if (nameExpr is ConstantExpression constExpr && constExpr.Value is string str)
        {
            variableName = str;
        }
        else if (nameExpr is IdentifierExpression idExpr)
        {
            variableName = idExpr.Name;
        }
        else
        {
            var nameResult = EvaluateExpression(focus, nameExpr, context).ToList();
            if (nameResult.Count == 1 && nameResult[0].Value is string evaluatedName)
            {
                variableName = evaluatedName;
            }
        }

        if (string.IsNullOrEmpty(variableName))
        {
            throw new InvalidOperationException("defineVariable requires a string as the first argument (literal, identifier, or expression that evaluates to a string)");
        }

        ImmutableList<IElement> value;
        if (expression.Arguments.Count == 2)
        {
            var valueExpr = expression.Arguments[1];
            value = EvaluateExpression(focus, valueExpr, context).ToImmutableList();
        }
        else
        {
            value = focus.ToImmutableList();
        }

        context.DefinedVariables[variableName] = value;

        return focus;
    }

    public IEnumerable<IElement> VisitPropertyAccess(PropertyAccessExpression expression, EvaluationContext context)
    {
        var focusElements = expression.Focus != null
            ? EvaluateExpression(context.Focus, expression.Focus, context)
            : context.Focus;

        foreach (var element in focusElements)
        {
            if (expression.PropertyName.Length > 0 && char.IsUpper(expression.PropertyName[0]))
            {
                string[] baseClasses = ["Resource", "DomainResource"];
                if (element.InstanceType == expression.PropertyName || baseClasses.Contains(expression.PropertyName))
                {
                    yield return element;
                    continue;
                }
            }

            foreach (var child in element.Children(expression.PropertyName))
            {
                yield return child;
            }
        }
    }

    public IEnumerable<IElement> VisitIdentifier(IdentifierExpression expression, EvaluationContext context)
    {
        foreach (var element in context.Focus)
        {
            if (expression.Name.Length > 0 && char.IsUpper(expression.Name[0]))
            {
                string[] baseClasses = ["Resource", "DomainResource"];
                if (element.InstanceType == expression.Name || baseClasses.Contains(expression.Name))
                {
                    yield return element;
                    continue;
                }
            }

            foreach (var child in element.Children(expression.Name))
            {
                yield return child;
            }
        }
    }

    public IEnumerable<IElement> VisitBinary(BinaryExpression expression, EvaluationContext context)
    {
        var left = EvaluateExpression(context.Focus, expression.Left, context).ToList();
        var right = EvaluateExpression(context.Focus, expression.Right, context).ToList();

#pragma warning disable CA1308 // Normalize strings to uppercase
        return expression.Operator.ToLowerInvariant() switch
#pragma warning restore CA1308 // Normalize strings to uppercase
        {
            "|" => EvaluateUnion(left, right),

            "+" => EvaluateAddition(left, right),
            "-" => EvaluateSubtraction(left, right),
            "*" => EvaluateMultiplication(left, right),
            "/" => EvaluateDivision(left, right),
            "div" => EvaluateIntegerDivision(left, right),
            "mod" => EvaluateModulo(left, right),

            "&" => EvaluateStringConcatenation(left, right),

            "is" => EvaluateTypeIs(left, expression.Right),
            "as" => EvaluateTypeAs(left, expression.Right),

            "in" => FunctionHelpers.ReturnBoolean(EvaluateMembership(left, right, isIn: true)),
            "contains" => FunctionHelpers.ReturnBoolean(EvaluateMembership(left, right, isIn: false)),

            "=" => FunctionHelpers.ReturnBoolean(CompareEquality(left, right, equals: true)),
            "!=" => FunctionHelpers.ReturnBoolean(CompareEquality(left, right, equals: false)),
            "~" => FunctionHelpers.ReturnBoolean(CompareEquivalence(left, right, equivalent: true)),
            "!~" => FunctionHelpers.ReturnBoolean(CompareEquivalence(left, right, equivalent: false)),
            ">" => FunctionHelpers.ReturnBoolean(CompareOrder(left, right, greater: true, orEqual: false)),
            ">=" => FunctionHelpers.ReturnBoolean(CompareOrder(left, right, greater: true, orEqual: true)),
            "<" => FunctionHelpers.ReturnBoolean(CompareOrder(left, right, greater: false, orEqual: false)),
            "<=" => FunctionHelpers.ReturnBoolean(CompareOrder(left, right, greater: false, orEqual: true)),

            "and" => FunctionHelpers.ReturnBoolean(FunctionHelpers.IsTrue(left) && FunctionHelpers.IsTrue(right)),
            "or" => FunctionHelpers.ReturnBoolean(FunctionHelpers.IsTrue(left) || FunctionHelpers.IsTrue(right)),
            "xor" => FunctionHelpers.ReturnBoolean(FunctionHelpers.IsTrue(left) ^ FunctionHelpers.IsTrue(right)),
            "implies" => FunctionHelpers.ReturnBoolean(!FunctionHelpers.IsTrue(left) || FunctionHelpers.IsTrue(right)),

            _ => throw new NotSupportedException($"Binary operator '{expression.Operator}' is not yet implemented")
        };
    }


    private IEnumerable<IElement> EvaluateUnion(List<IElement> left, List<IElement> right)
    {
        return FunctionHelpers.EvaluateUnion(left, right);
    }

    private IEnumerable<IElement> EvaluateAddition(List<IElement> left, List<IElement> right)
    {
        if (left.Count != 1 || right.Count != 1)
            return [];

        var leftValue = left[0].Value;
        var rightValue = right[0].Value;

        if (leftValue is Types.Quantity || rightValue is Types.Quantity)
        {
            return QuantityEvaluator.EvaluateArithmetic(left, "+", right);
        }

        if (leftValue is string leftStr && rightValue is string rightStr)
        {
            return [CreateString(leftStr + rightStr)];
        }

        if (FunctionHelpers.TryConvertToDecimal(leftValue, out var leftDecimal) && FunctionHelpers.TryConvertToDecimal(rightValue, out var rightDecimal))
        {
            var result = leftDecimal + rightDecimal;
            return leftValue is int && rightValue is int && result == Math.Floor(result)
                ? [CreateInteger((int)result)]
                : [CreateDecimal(result)];
        }

        return [];
    }

    private IEnumerable<IElement> EvaluateSubtraction(List<IElement> left, List<IElement> right)
    {
        if (left.Count != 1 || right.Count != 1)
            return [];

        var leftValue = left[0].Value;
        var rightValue = right[0].Value;

        if (leftValue is Types.Quantity || rightValue is Types.Quantity)
        {
            return QuantityEvaluator.EvaluateArithmetic(left, "-", right);
        }

        if (FunctionHelpers.TryConvertToDecimal(leftValue, out var leftDecimal) && FunctionHelpers.TryConvertToDecimal(rightValue, out var rightDecimal))
        {
            var result = leftDecimal - rightDecimal;
            return leftValue is int && rightValue is int && result == Math.Floor(result)
                ? [CreateInteger((int)result)]
                : [CreateDecimal(result)];
        }

        return [];
    }

    private IEnumerable<IElement> EvaluateMultiplication(List<IElement> left, List<IElement> right)
    {
        if (left.Count != 1 || right.Count != 1)
            return [];

        var leftValue = left[0].Value;
        var rightValue = right[0].Value;

        if (leftValue is Types.Quantity || rightValue is Types.Quantity)
        {
            return QuantityEvaluator.EvaluateArithmetic(left, "*", right);
        }

        if (FunctionHelpers.TryConvertToDecimal(leftValue, out var leftDecimal) && FunctionHelpers.TryConvertToDecimal(rightValue, out var rightDecimal))
        {
            var result = leftDecimal * rightDecimal;
            return leftValue is int && rightValue is int && result == Math.Floor(result)
                ? [CreateInteger((int)result)]
                : [CreateDecimal(result)];
        }

        return [];
    }

    private IEnumerable<IElement> EvaluateDivision(List<IElement> left, List<IElement> right)
    {
        if (left.Count != 1 || right.Count != 1)
            return [];

        var leftValue = left[0].Value;
        var rightValue = right[0].Value;

        if (leftValue is Types.Quantity || rightValue is Types.Quantity)
        {
            return QuantityEvaluator.EvaluateArithmetic(left, "/", right);
        }

        if (FunctionHelpers.TryConvertToDecimal(leftValue, out var leftDecimal) && FunctionHelpers.TryConvertToDecimal(rightValue, out var rightDecimal))
        {
            if (rightDecimal == 0)
                return [];

            return [CreateDecimal(leftDecimal / rightDecimal)];
        }

        return [];
    }

    private IEnumerable<IElement> EvaluateIntegerDivision(List<IElement> left, List<IElement> right)
    {
        if (left.Count != 1 || right.Count != 1)
            return [];

        if (FunctionHelpers.TryConvertToDecimal(left[0].Value, out var leftDecimal) && FunctionHelpers.TryConvertToDecimal(right[0].Value, out var rightDecimal))
        {
            if (rightDecimal == 0)
                return [];

            return [CreateInteger((int)Math.Truncate(leftDecimal / rightDecimal))];
        }

        return [];
    }

    private IEnumerable<IElement> EvaluateModulo(List<IElement> left, List<IElement> right)
    {
        if (left.Count != 1 || right.Count != 1)
            return [];

        if (FunctionHelpers.TryConvertToDecimal(left[0].Value, out var leftDecimal) && FunctionHelpers.TryConvertToDecimal(right[0].Value, out var rightDecimal))
        {
            if (rightDecimal == 0)
                return [];

            return [CreateDecimal(leftDecimal % rightDecimal)];
        }

        return [];
    }

    private IEnumerable<IElement> EvaluateStringConcatenation(List<IElement> left, List<IElement> right)
    {
        if (left.Count != 1 || right.Count != 1)
            return [];

        var leftStr = left[0].Value?.ToString() ?? string.Empty;
        var rightStr = right[0].Value?.ToString() ?? string.Empty;

        return [new PrimitiveElement(leftStr + rightStr, "string")];
    }

    private IEnumerable<IElement> EvaluateTypeIs(List<IElement> left, Expression typeExpr)
    {
        if (left.Count != 1)
            return [];

        string? typeName = typeExpr switch
        {
            IdentifierExpression idExpr => idExpr.Name,
            PropertyAccessExpression propExpr => propExpr.PropertyName,
            FunctionCallExpression funcExpr => funcExpr.FunctionName,
            ConstantExpression constExpr => constExpr.Value?.ToString(),
            _ => null
        };

        if (typeName == null)
            return [];

#pragma warning disable CA1308 // Normalize strings to uppercase
        typeName = typeName.ToLowerInvariant();
        var elementType = left[0].InstanceType?.ToLowerInvariant() ?? string.Empty;
#pragma warning restore CA1308 // Normalize strings to uppercase

        return FunctionHelpers.ReturnBoolean(elementType == typeName);
    }

    private IEnumerable<IElement> EvaluateTypeAs(List<IElement> left, Expression typeExpr)
    {
        if (left.Count != 1)
            return [];

        string? typeName = typeExpr switch
        {
            IdentifierExpression idExpr => idExpr.Name,
            PropertyAccessExpression propExpr => propExpr.PropertyName,
            FunctionCallExpression funcExpr => funcExpr.FunctionName,
            ConstantExpression constExpr => constExpr.Value?.ToString(),
            _ => null
        };

        if (typeName == null)
            return [];

#pragma warning disable CA1308 // Normalize strings to uppercase
        typeName = typeName.ToLowerInvariant();
        var elementType = left[0].InstanceType?.ToLowerInvariant() ?? string.Empty;
#pragma warning restore CA1308 // Normalize strings to uppercase

        return elementType == typeName ? [left[0]] : [];
    }

    private bool? EvaluateMembership(List<IElement> left, List<IElement> right, bool isIn)
    {
        var singleItem = isIn ? left : right;
        var collection = isIn ? right : left;

        if (singleItem.Count == 0)
            return null;

        if (singleItem.Count != 1)
            return null;

        if (collection.Count == 0)
            return false;

        var itemValue = singleItem[0].Value;
        return collection.Any(c => FunctionHelpers.AreEqual(c.Value, itemValue));
    }

    private bool? CompareEquivalence(List<IElement> left, List<IElement> right, bool equivalent)
    {
        if (left.Count == 0 && right.Count == 0)
            return equivalent;

        if (left.Count != right.Count)
            return !equivalent;

        if (left.Count == 1 && right.Count == 1)
        {
            var isEquiv = AreEquivalent(left[0].Value, right[0].Value);
            return isEquiv == equivalent;
        }

        var leftSorted = left.OrderBy(e => e.Value?.ToString() ?? string.Empty).ToList();
        var rightSorted = right.OrderBy(e => e.Value?.ToString() ?? string.Empty).ToList();

        for (int i = 0; i < leftSorted.Count; i++)
        {
            if (!AreEquivalent(leftSorted[i].Value, rightSorted[i].Value))
                return !equivalent;
        }

        return equivalent;
    }

    private bool AreEquivalent(object? left, object? right)
    {
        if (left == null && right == null) return true;
        if (left == null || right == null) return false;

        if (left is string leftStr && right is string rightStr)
        {
            return string.Equals(
                NormalizeWhitespace(leftStr),
                NormalizeWhitespace(rightStr),
                StringComparison.OrdinalIgnoreCase);
        }

        if (left is decimal || right is decimal || left is int || right is int)
        {
            if (FunctionHelpers.TryConvertToDecimal(left, out var leftDec) && FunctionHelpers.TryConvertToDecimal(right, out var rightDec))
                return leftDec == rightDec;
        }

        return left.Equals(right);
    }

    private string NormalizeWhitespace(string str)
    {
        return System.Text.RegularExpressions.Regex.Replace(str.Trim(), @"\s+", " ");
    }


    public IEnumerable<IElement> VisitScope(ScopeExpression expression, EvaluationContext context)
    {
#pragma warning disable CA1308 // Normalize strings to uppercase
        return expression.ScopeName.ToLowerInvariant() switch
#pragma warning restore CA1308 // Normalize strings to uppercase
        {
            "this" => context.GetThis() is IElement thisElement
                ? [thisElement]
                : context.Focus,
            "that" => context.Focus,
            _ => throw new NotSupportedException($"Scope '${expression.ScopeName}' is not yet implemented")
        };
    }

    public IEnumerable<IElement> VisitVariable(VariableRefExpression expression, EvaluationContext context)
    {
        var value = context.GetEnvironmentVariable(expression.Name);

        if (expression.Name is "this" or "index")
        {
            if (value == null)
                return [];
            if (value is IElement element)
                return [element];
            if (value is IEnumerable<IElement> elements)
                return elements;
            return [];
        }

        if (value == null)
        {
            return [];
        }

        if (value is IElement element2)
            return [element2];
        if (value is IEnumerable<IElement> elements2)
            return elements2;

        return [];
    }

    public IEnumerable<IElement> VisitConstant(ConstantExpression expression, EvaluationContext context)
    {
        return expression.Value switch
        {
            int i => [CreateInteger(i)],
            decimal d => [CreateDecimal(d)],
            bool b => [CreateBoolean(b)],
            string s => [CreateDateTimeOrString(s)],
            _ => [CreateConstant(expression.Value)]
        };
    }

    /// <summary>
    /// Creates a typed element from a string value.
    /// Detects date/time literals (@YYYY, @YYYY-MM-DD, @YYYY-MM-DDTHH:MM:SS, @THH:MM:SS)
    /// and creates elements with appropriate types (date, dateTime, time).
    /// </summary>
    private IElement CreateDateTimeOrString(string value)
    {
        if (string.IsNullOrEmpty(value))
            return CreateString(value);

        if (!value.StartsWith("@", StringComparison.Ordinal))
            return CreateString(value);

        var dateTimeValue = value.Substring(1);

        if (dateTimeValue.StartsWith("T", StringComparison.Ordinal))
        {
            return new PrimitiveElement(value, "time");
        }

        if (dateTimeValue.Contains('T', StringComparison.Ordinal))
        {
            return new PrimitiveElement(value, "dateTime");
        }

        return new PrimitiveElement(value, "date");
    }

    public IEnumerable<IElement> VisitIndexer(IndexerExpression expression, EvaluationContext context)
    {
        // Optimization: Fast path for constant integer indexes
        // Avoids creating IElement wrapper and context allocation for index evaluation
        if (expression.Index is ConstantExpression { Value: int constantIndex })
        {
            var collection = EvaluateExpression(context.Focus, expression.Collection, context).ToList();

            if (constantIndex >= 0 && constantIndex < collection.Count)
            {
                return [collection[constantIndex]];
            }

            return [];
        }

        // General case: evaluate index expression dynamically
        var collection2 = EvaluateExpression(context.Focus, expression.Collection, context).ToList();
        var indexResults = EvaluateExpression(context.Focus, expression.Index, context).ToList();

        if (indexResults.Count == 1 && indexResults[0].Value is int index)
        {
            if (index >= 0 && index < collection2.Count)
            {
                return [collection2[index]];
            }
        }

        return [];
    }

    public IEnumerable<IElement> VisitUnary(UnaryExpression expression, EvaluationContext context)
    {
        var operand = EvaluateExpression(context.Focus, expression.Operand, context).ToList();

        if (expression.Operator == "-" && operand.Count == 1)
        {
            var value = operand[0].Value;
            try
            {
                if (value is int i)
                {
                    return [CreateInteger(-i)];
                }
                if (value is long l && l >= int.MinValue && l <= int.MaxValue)
                {
                    return [CreateInteger(-(int)l)];
                }
                if (value is IConvertible)
                {
                    var numeric = Convert.ToDecimal(value);
                    return [CreateDecimal(-numeric)];
                }
            }
            catch
            {
            }
        }

        return operand;
    }


    private bool? CompareEquality(List<IElement> left, List<IElement> right, bool equals)
    {
        if (left.Count == 0 || right.Count == 0)
            return null;

        if (left.Count != right.Count)
            return !equals;

        if (left.Count == 1 && right.Count == 1 &&
            (left[0].Value is Types.Quantity || right[0].Value is Types.Quantity))
        {
            var result = QuantityEvaluator.EvaluateComparison(left, equals ? "=" : "!=", right);
            return result;
        }

        if (left.Count == 1 && right.Count == 1)
        {
#pragma warning disable CA1308 // Normalize strings to uppercase
            var leftType = left[0].InstanceType?.ToLowerInvariant();
            var rightType = right[0].InstanceType?.ToLowerInvariant();
#pragma warning restore CA1308 // Normalize strings to uppercase

            if ((leftType == "date" || leftType == "datetime" || leftType == "instant") &&
                (rightType == "date" || rightType == "datetime" || rightType == "instant"))
            {
                return CompareDateTimeEquality(left[0].Value, right[0].Value, equals);
            }
        }

        for (int i = 0; i < left.Count; i++)
        {
            var isEqual = FunctionHelpers.AreEqual(left[i].Value, right[i].Value);
            if (isEqual != equals) return false;
        }

        return true;
    }

    private bool? CompareDateTimeEquality(object? leftValue, object? rightValue, bool equals)
    {
        if (leftValue is not string leftStr || rightValue is not string rightStr)
            return null;

        leftStr = leftStr.StartsWith('@') ? leftStr.Substring(1) : leftStr;
        rightStr = rightStr.StartsWith('@') ? rightStr.Substring(1) : rightStr;

        var leftPrecision = GetDateTimePrecision(leftStr);
        var rightPrecision = GetDateTimePrecision(rightStr);

        if (leftPrecision == DateTimePrecision.Invalid || rightPrecision == DateTimePrecision.Invalid)
            return null;

        if (leftPrecision != rightPrecision)
        {
            return null;
        }

        return equals ? leftStr == rightStr : leftStr != rightStr;
    }

    private bool? CompareOrder(List<IElement> left, List<IElement> right, bool greater, bool orEqual)
    {
        if (left.Count == 0 || right.Count == 0)
            return null;

        if (left.Count != 1 || right.Count != 1)
            return null;

        var leftValue = left[0].Value;
        var rightValue = right[0].Value;

        if (leftValue is Types.Quantity || rightValue is Types.Quantity)
        {
            var op = (greater, orEqual) switch
            {
                (true, false) => ">",
                (true, true) => ">=",
                (false, false) => "<",
                (false, true) => "<="
            };
            return QuantityEvaluator.EvaluateComparison(left, op, right);
        }

#pragma warning disable CA1308 // Normalize strings to uppercase
        var leftType = left[0].InstanceType?.ToLowerInvariant();
        var rightType = right[0].InstanceType?.ToLowerInvariant();
#pragma warning restore CA1308 // Normalize strings to uppercase

        if ((leftType == "date" || leftType == "datetime") && (rightType == "date" || rightType == "datetime"))
        {
            return CompareDateTimesWithPrecision(leftValue, rightValue, greater, orEqual);
        }

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
                return null;
            }
        }

        return null;
    }

    private bool? CompareDateTimesWithPrecision(object? leftValue, object? rightValue, bool greater, bool orEqual)
    {
        if (leftValue is not string leftStr || rightValue is not string rightStr)
            return null;

        leftStr = leftStr.StartsWith("@", StringComparison.Ordinal) ? leftStr.Substring(1) : leftStr;
        rightStr = rightStr.StartsWith("@", StringComparison.Ordinal) ? rightStr.Substring(1) : rightStr;

        var leftPrecision = GetDateTimePrecision(leftStr);
        var rightPrecision = GetDateTimePrecision(rightStr);

        if (leftPrecision == DateTimePrecision.Invalid || rightPrecision == DateTimePrecision.Invalid)
            return null;

        var leftLower = GetDateTimeLowerBound(leftStr, leftPrecision);
        var leftUpper = GetDateTimeUpperBound(leftStr, leftPrecision);
        var rightLower = GetDateTimeLowerBound(rightStr, rightPrecision);
        var rightUpper = GetDateTimeUpperBound(rightStr, rightPrecision);

        if (!leftLower.HasValue || !leftUpper.HasValue || !rightLower.HasValue || !rightUpper.HasValue)
            return null;

        if (greater)
        {
            if (orEqual)
            {
                return leftUpper >= rightLower;
            }
            else
            {
                return leftLower > rightUpper;
            }
        }
        else
        {
            if (orEqual)
            {
                return leftLower <= rightUpper;
            }
            else
            {
                return leftUpper < rightLower;
            }
        }
    }

    private enum DateTimePrecision
    {
        Invalid,
        Year,
        Month,
        Day,
        Hour,
        Minute,
        Second,
        Millisecond
    }

    private DateTimePrecision GetDateTimePrecision(string value)
    {
        if (string.IsNullOrEmpty(value))
            return DateTimePrecision.Invalid;

        if (value.Length >= 4 && value.Length <= 10)
        {
            var parts = value.Split('-');
            return parts.Length switch
            {
                1 => DateTimePrecision.Year,
                2 => DateTimePrecision.Month,
                3 => DateTimePrecision.Day,
                _ => DateTimePrecision.Invalid
            };
        }

        if (value.Contains('T', StringComparison.Ordinal))
        {
            var timePart = value.Split('T')[1];
            timePart = timePart.TrimEnd('Z');
            if (timePart.Contains('+', StringComparison.Ordinal) || timePart.Contains('-', StringComparison.Ordinal))
            {
                var tzIndex = Math.Max(timePart.LastIndexOf('+'), timePart.LastIndexOf('-'));
                timePart = timePart.Substring(0, tzIndex);
            }

            var colonCount = timePart.Count(c => c == ':');
            if (colonCount == 0) return DateTimePrecision.Hour;
            if (colonCount == 1) return DateTimePrecision.Minute;

            return timePart.Contains('.', StringComparison.Ordinal) ? DateTimePrecision.Millisecond : DateTimePrecision.Second;
        }

        return DateTimePrecision.Invalid;
    }

    private DateTime? GetDateTimeLowerBound(string value, DateTimePrecision precision)
    {
        try
        {
            return precision switch
            {
                DateTimePrecision.Year => new DateTime(int.Parse(value), 1, 1),
                DateTimePrecision.Month => DateTime.ParseExact(value + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                _ => DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture)
            };
        }
        catch
        {
            return null;
        }
    }

    private DateTime? GetDateTimeUpperBound(string value, DateTimePrecision precision)
    {
        try
        {
            return precision switch
            {
                DateTimePrecision.Year => new DateTime(int.Parse(value), 12, 31, 23, 59, 59, 999),
                DateTimePrecision.Month => DateTime.ParseExact(value + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).AddMonths(1).AddMilliseconds(-1),
                DateTimePrecision.Day => DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture).Date.AddDays(1).AddMilliseconds(-1),
                DateTimePrecision.Hour => DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture).AddHours(1).AddMilliseconds(-1),
                DateTimePrecision.Minute => DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture).AddMinutes(1).AddMilliseconds(-1),
                _ => DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture)
            };
        }
        catch
        {
            return null;
        }
    }


    public IEnumerable<IElement> VisitParenthesized(ParenthesizedExpression expression, EvaluationContext context)
    {
        return EvaluateExpression(context.Focus, expression.InnerExpression, context);
    }

    public IEnumerable<IElement> VisitQuantity(QuantityExpression expression, EvaluationContext context)
    {
        return QuantityEvaluator.EvaluateQuantity(expression);
    }

    public IEnumerable<IElement> VisitEmpty(EmptyExpression expression, EvaluationContext context)
    {
        return [];
    }

    private IElement CreateBoolean(bool value) => new PrimitiveElement(value, "boolean");
    private IElement CreateInteger(int value) => new PrimitiveElement(value, "integer");
    private IElement CreateDecimal(decimal value) => new PrimitiveElement(value, "decimal");
    private IElement CreateString(string value) => new PrimitiveElement(value, "string");
    private IElement CreateConstant(object value) => new PrimitiveElement(value, GetFhirPathTypeName(value));

    /// <summary>
    /// Converts a .NET primitive value to its FHIRPath type name.
    /// Centralized logic for type name conversion.
    /// </summary>
    internal static string GetFhirPathTypeName(object value)
    {
        return value switch
        {
            string => "string",
            int or long => "integer",
            decimal => "decimal",
            bool => "boolean",
            DateTime or DateTimeOffset => "dateTime",
            _ => "string"
        };
    }

    /// <summary>
    /// Simple implementation of IElement for primitive values.
    /// </summary>
    private class PrimitiveElement : IElement
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
        public IType? Type => null;

        public IReadOnlyList<IElement> Children(string? name = null) => [];

        public T? Meta<T>() where T : class => null;
    }
}
