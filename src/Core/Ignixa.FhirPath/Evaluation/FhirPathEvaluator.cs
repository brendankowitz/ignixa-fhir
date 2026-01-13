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

        // Push the root element onto the $this stack so $this resolves correctly throughout evaluation
        context = context.PushThis(input);

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

            "and" => EvaluateAnd(left, right),
            "or" => EvaluateOr(left, right),
            "xor" => EvaluateXor(left, right),
            "implies" => EvaluateImplies(left, right),

            _ => throw new NotSupportedException($"Binary operator '{expression.Operator}' is not yet implemented")
        };
    }


    private IEnumerable<IElement> EvaluateUnion(List<IElement> left, List<IElement> right)
    {
        return FunctionHelpers.EvaluateUnion(left, right);
    }

    /// <summary>
    /// Evaluates the AND operator with FHIRPath three-valued logic.
    /// Returns false if either is false, empty if either is empty and neither is false, otherwise true.
    /// </summary>
    private IEnumerable<IElement> EvaluateAnd(List<IElement> left, List<IElement> right)
    {
        var leftBool = GetBooleanValue(left);
        var rightBool = GetBooleanValue(right);

        // false AND anything = false
        if (leftBool == false || rightBool == false)
            return FunctionHelpers.ReturnBoolean(false);

        // If either is empty (null), result is empty
        if (leftBool == null || rightBool == null)
            return [];

        // Both are true
        return FunctionHelpers.ReturnBoolean(true);
    }

    /// <summary>
    /// Evaluates the OR operator with FHIRPath three-valued logic.
    /// Returns true if either is true, empty if either is empty and neither is true, otherwise false.
    /// </summary>
    private IEnumerable<IElement> EvaluateOr(List<IElement> left, List<IElement> right)
    {
        var leftBool = GetBooleanValue(left);
        var rightBool = GetBooleanValue(right);

        // true OR anything = true
        if (leftBool == true || rightBool == true)
            return FunctionHelpers.ReturnBoolean(true);

        // If either is empty (null), result is empty
        if (leftBool == null || rightBool == null)
            return [];

        // Both are false
        return FunctionHelpers.ReturnBoolean(false);
    }

    /// <summary>
    /// Evaluates the XOR operator with FHIRPath three-valued logic.
    /// Returns empty if either is empty, otherwise true if exactly one is true.
    /// </summary>
    private IEnumerable<IElement> EvaluateXor(List<IElement> left, List<IElement> right)
    {
        var leftBool = GetBooleanValue(left);
        var rightBool = GetBooleanValue(right);

        // If either is empty, result is empty
        if (leftBool == null || rightBool == null)
            return [];

        // XOR: true if exactly one is true
        return FunctionHelpers.ReturnBoolean(leftBool.Value ^ rightBool.Value);
    }

    /// <summary>
    /// Evaluates the IMPLIES operator with FHIRPath three-valued logic.
    /// Returns true if left is false or right is true, empty if cannot determine, otherwise false.
    /// </summary>
    private IEnumerable<IElement> EvaluateImplies(List<IElement> left, List<IElement> right)
    {
        var leftBool = GetBooleanValue(left);
        var rightBool = GetBooleanValue(right);

        // false IMPLIES anything = true
        if (leftBool == false)
            return FunctionHelpers.ReturnBoolean(true);

        // anything IMPLIES true = true
        if (rightBool == true)
            return FunctionHelpers.ReturnBoolean(true);

        // true IMPLIES false = false
        if (leftBool == true && rightBool == false)
            return FunctionHelpers.ReturnBoolean(false);

        // Otherwise empty (cannot determine)
        return [];
    }

    /// <summary>
    /// Converts a collection to a boolean value for use in logical operators (and, or, xor, implies).
    /// Per FHIRPath spec:
    /// - Empty collection returns null (unknown)
    /// - Single boolean element returns that boolean value
    /// - Non-empty collection (including non-boolean values) returns true (truthy/exists)
    /// </summary>
    private static bool? GetBooleanValue(List<IElement> elements)
    {
        if (elements.Count == 0)
            return null;

        if (elements.Count == 1 && elements[0].Value is bool b)
            return b;

        // Non-empty collection (non-boolean or multiple elements) is truthy
        return true;
    }

    private IEnumerable<IElement> EvaluateAddition(List<IElement> left, List<IElement> right)
    {
        if (left.Count != 1 || right.Count != 1)
            return [];

        var leftValue = left[0].Value;
        var rightValue = right[0].Value;

        // Date/DateTime + Quantity
        if (leftValue is string leftStr && rightValue is Types.Quantity qty)
        {
            return EvaluateDateTimeArithmetic(leftStr, qty, add: true);
        }

        // Quantity + Date/DateTime
        if (leftValue is Types.Quantity qty2 && rightValue is string rightStr)
        {
            return EvaluateDateTimeArithmetic(rightStr, qty2, add: true);
        }

        if (leftValue is Types.Quantity || rightValue is Types.Quantity)
        {
            return QuantityEvaluator.EvaluateArithmetic(left, "+", right);
        }

        if (leftValue is string leftStr2 && rightValue is string rightStr2)
        {
            return [CreateString(leftStr2 + rightStr2)];
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

        // Date/DateTime - Quantity
        if (leftValue is string leftStr && rightValue is Types.Quantity qty)
        {
            return EvaluateDateTimeArithmetic(leftStr, qty, add: false);
        }

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

        // Handle qualified type names
        if (typeName.Contains('.', StringComparison.Ordinal))
        {
            var parts = typeName.Split('.');
            if (parts.Length == 2 && (parts[0].Equals("FHIR", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("System", StringComparison.OrdinalIgnoreCase)))
            {
                typeName = parts[1];
            }
        }

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

        // Handle qualified type names
        if (typeName.Contains('.', StringComparison.Ordinal))
        {
            var parts = typeName.Split('.');
            if (parts.Length == 2 && (parts[0].Equals("FHIR", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("System", StringComparison.OrdinalIgnoreCase)))
            {
                typeName = parts[1];
            }
        }

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

        // Handle quantity equivalence with unit conversion
        if (left is Types.Quantity leftQty && right is Types.Quantity rightQty)
        {
            // Try to compare after converting to same unit
            var converter = Types.QuantityUnitConverter.Instance;
            if (!converter.IsCompatible(leftQty.Unit, rightQty.Unit))
                return false;

            var convertedRight = rightQty.ConvertTo(leftQty.Unit, converter);
            if (convertedRight == null)
                return false;

            // For quantities, equivalence (~) uses a tolerance-based comparison
            // Per FHIRPath spec and official test suite:
            // - 4g ~ 4000mg should be true (exact match)
            // - 4g ~ 4040mg should be true (within 1% tolerance)
            // - 4g != 4040mg should be true (equality is stricter)
            //
            // Use a 1% relative tolerance for equivalence
            var diff = Math.Abs(leftQty.Value - convertedRight.Value);
            var maxAbs = Math.Max(Math.Abs(leftQty.Value), Math.Abs(convertedRight.Value));

            if (maxAbs == 0)
                return diff == 0; // Both zero - must be exact

            const decimal relativeTolerance = 0.01m; // 1%
            return (diff / maxAbs) <= relativeTolerance;
        }

        if (left is string leftStr && right is string rightStr)
        {
            // Check if these are datetime strings (start with @ or look like dates/times)
            if (IsDateTimeString(leftStr) && IsDateTimeString(rightStr))
            {
                // Normalize @ prefix and millisecond precision for datetime equivalence
                var normalizedLeft = NormalizeMillisecondPrecision(leftStr.StartsWith('@') ? leftStr.Substring(1) : leftStr);
                var normalizedRight = NormalizeMillisecondPrecision(rightStr.StartsWith('@') ? rightStr.Substring(1) : rightStr);

                // Try to parse and compare as UTC for datetime with timezone info
                if (TryParseFhirDateTime(normalizedLeft, out var leftDt) &&
                    TryParseFhirDateTime(normalizedRight, out var rightDt))
                {
                    return leftDt.ToUniversalTime() == rightDt.ToUniversalTime();
                }

                return normalizedLeft == normalizedRight;
            }

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

    /// <summary>
    /// Determines if a string value appears to be a FHIRPath date/time value.
    /// </summary>
    private static bool IsDateTimeString(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        // Starts with @ prefix
        if (value.StartsWith('@'))
            return true;

        // Time-only format (starts with T)
        if (value.StartsWith('T') && value.Length >= 3 && char.IsDigit(value[1]))
            return true;

        // Date/DateTime format (starts with 4-digit year)
        if (value.Length >= 4 && char.IsDigit(value[0]) && char.IsDigit(value[1]) && char.IsDigit(value[2]) && char.IsDigit(value[3]))
        {
            // Check for date pattern (YYYY or YYYY-MM or YYYY-MM-DD)
            if (value.Length == 4) return true;
            if (value.Length >= 5 && value[4] == '-') return true;
        }

        return false;
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

        if (left.Count == 1 && right.Count == 1)
        {
            var leftVal = left[0].Value;
            var rightVal = right[0].Value;

            if (leftVal is Types.Quantity || rightVal is Types.Quantity)
            {
                var result = QuantityEvaluator.EvaluateComparison(left, equals ? "=" : "!=", right);
                return result;
            }

            // Handle mixed numeric equality (e.g. 1 = 1.0)
            if ((leftVal is int || leftVal is decimal || leftVal is long) &&
                (rightVal is int || rightVal is decimal || rightVal is long))
            {
                if (FunctionHelpers.TryConvertToDecimal(leftVal, out var ld) && FunctionHelpers.TryConvertToDecimal(rightVal, out var rd))
                {
                    return equals ? ld == rd : ld != rd;
                }
            }

#pragma warning disable CA1308 // Normalize strings to uppercase
            var leftType = left[0].InstanceType?.ToLowerInvariant();
            var rightType = right[0].InstanceType?.ToLowerInvariant();
#pragma warning restore CA1308 // Normalize strings to uppercase

            if ((leftType == "date" || leftType == "datetime" || leftType == "instant") &&
                (rightType == "date" || rightType == "datetime" || rightType == "instant"))
            {
                return CompareDateTimeEquality(leftVal, rightVal, equals);
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
        var leftStr = leftValue switch
        {
            string s => s,
            DateTime dt => dt.ToString("o"),
            DateTimeOffset dto => dto.ToString("o"),
            _ => null
        };

        var rightStr = rightValue switch
        {
            string s => s,
            DateTime dt => dt.ToString("o"),
            DateTimeOffset dto => dto.ToString("o"),
            _ => null
        };

        if (leftStr == null || rightStr == null)
            return null;

        leftStr = leftStr.StartsWith('@') ? leftStr.Substring(1) : leftStr;
        rightStr = rightStr.StartsWith('@') ? rightStr.Substring(1) : rightStr;

        // Normalize .0, .00, .000 millisecond suffixes - these represent zero milliseconds
        // and are semantically equivalent to no milliseconds per FHIRPath spec
        leftStr = NormalizeMillisecondPrecision(leftStr);
        rightStr = NormalizeMillisecondPrecision(rightStr);

        var leftPrecision = GetDateTimePrecision(leftStr);
        var rightPrecision = GetDateTimePrecision(rightStr);

        if (leftPrecision == DateTimePrecision.Invalid || rightPrecision == DateTimePrecision.Invalid)
            return null;

        if (leftPrecision != rightPrecision)
        {
            return null;
        }

        // Try to parse as DateTimeOffset to handle timezones
        if (TryParseFhirDateTime(leftStr, out var leftDt) &&
            TryParseFhirDateTime(rightStr, out var rightDt))
        {
            // If both parse successfully, we can compare them properly regardless of timezone representation
            // But we need to be careful about precision. The precision check above ensures we are comparing
            // same granularity (e.g. milliseconds to milliseconds).
            
            // For date/time, if we have hours/minutes/seconds, we should respect timezone equality
            if (leftPrecision >= DateTimePrecision.Hour)
            {
                 var result = leftDt.ToUniversalTime() == rightDt.ToUniversalTime();
                 return equals ? result : !result;
            }
        }

        return equals ? leftStr == rightStr : leftStr != rightStr;
    }

    /// <summary>
    /// Normalizes trailing zero millisecond suffixes (.0, .00, .000) by removing them.
    /// Per FHIRPath spec, @2012-04-15T15:30:31 and @2012-04-15T15:30:31.0 are equivalent.
    /// </summary>
    private static string NormalizeMillisecondPrecision(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        // Handle timezone suffix preservation
        var tzSuffix = string.Empty;
        var workingValue = value;

        // Extract timezone if present (Z, +HH:MM, or -HH:MM)
        if (workingValue.EndsWith('Z'))
        {
            tzSuffix = "Z";
            workingValue = workingValue.Substring(0, workingValue.Length - 1);
        }
        else
        {
            var lastPlus = workingValue.LastIndexOf('+');
            var lastMinus = workingValue.LastIndexOf('-');
            // Timezone offset is after T (not a negative year or month separator)
            var tIndex = workingValue.IndexOf('T', StringComparison.Ordinal);
            var tzIndex = Math.Max(lastPlus, lastMinus);
            if (tzIndex > tIndex && tIndex >= 0)
            {
                tzSuffix = workingValue.Substring(tzIndex);
                workingValue = workingValue.Substring(0, tzIndex);
            }
        }

        // Only normalize if there's a decimal point (milliseconds present)
        var dotIndex = workingValue.LastIndexOf('.');
        if (dotIndex < 0)
            return value;

        // Check if the fractional part is all zeros
        var fractionalPart = workingValue.Substring(dotIndex + 1);
        if (fractionalPart.All(c => c == '0'))
        {
            // Remove the .000 suffix entirely
            return string.Concat(workingValue.AsSpan(0, dotIndex), tzSuffix);
        }

        return value;
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
    
            if ((leftType == "date" || leftType == "datetime" || leftType == "time") &&
                (rightType == "date" || rightType == "datetime" || rightType == "time"))
            {
                return CompareDateTimesWithPrecision(leftValue, rightValue, greater, orEqual);
            }
    
                    if (leftValue is string leftStr && rightValue is string rightStr)
                    {
                        // Try to treat as typed dates first if they look like dates
                        // This handles cases where type info is lost or implicit conversion is expected
                        if (IsDateTimeString(leftStr) && IsDateTimeString(rightStr))
                        {
                             var result = CompareDateTimesWithPrecision(leftValue, rightValue, greater, orEqual);
                             if (result.HasValue) return result;
                        }
            
                        var comparison = string.Compare(leftStr, rightStr, StringComparison.Ordinal);
                        return greater
                            ? (orEqual ? comparison >= 0 : comparison > 0)
                            : (orEqual ? comparison <= 0 : comparison < 0);
                    }    
            // Handle mixed numeric comparison (e.g. 1.5 > 1)
            if ((leftValue is int || leftValue is decimal || leftValue is long) &&
                (rightValue is int || rightValue is decimal || rightValue is long))
            {
                if (FunctionHelpers.TryConvertToDecimal(leftValue, out var ld) && FunctionHelpers.TryConvertToDecimal(rightValue, out var rd))
                {
                    var comparison = ld.CompareTo(rd);
                    return greater
                        ? (orEqual ? comparison >= 0 : comparison > 0)
                        : (orEqual ? comparison <= 0 : comparison < 0);
                }
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
        var leftStr = leftValue switch
        {
            string s => s,
            DateTime dt => dt.ToString("o"),
            DateTimeOffset dto => dto.ToString("o"),
            _ => null
        };

        var rightStr = rightValue switch
        {
            string s => s,
            DateTime dt => dt.ToString("o"),
            DateTimeOffset dto => dto.ToString("o"),
            _ => null
        };

        if (leftStr == null || rightStr == null)
            return null;

        leftStr = leftStr.StartsWith("@", StringComparison.Ordinal) ? leftStr.Substring(1) : leftStr;
        rightStr = rightStr.StartsWith("@", StringComparison.Ordinal) ? rightStr.Substring(1) : rightStr;

        // Normalize .0 millisecond suffixes for consistent precision detection
        leftStr = NormalizeMillisecondPrecision(leftStr);
        rightStr = NormalizeMillisecondPrecision(rightStr);

        var leftPrecision = GetDateTimePrecision(leftStr);
        var rightPrecision = GetDateTimePrecision(rightStr);

        if (leftPrecision == DateTimePrecision.Invalid || rightPrecision == DateTimePrecision.Invalid)
            return null;

        // Per FHIRPath spec: When comparing dates with different precision,
        // the result is null unless one interval completely precedes/follows the other.
        // For ordering (not equality), we use interval comparison semantics.
        var leftLower = GetDateTimeLowerBound(leftStr, leftPrecision);
        var leftUpper = GetDateTimeUpperBound(leftStr, leftPrecision);
        var rightLower = GetDateTimeLowerBound(rightStr, rightPrecision);
        var rightUpper = GetDateTimeUpperBound(rightStr, rightPrecision);

        if (!leftLower.HasValue || !leftUpper.HasValue || !rightLower.HasValue || !rightUpper.HasValue)
            return null;

        // Special case: identical intervals with orEqual operators should return true
        if (orEqual && leftLower == rightLower && leftUpper == rightUpper)
            return true;

        // For strict ordering (< or >), both intervals must be completely separate
        // For non-strict ordering (<= or >=), overlapping intervals return null
        if (greater)
        {
            if (orEqual)
            {
                // >= : true if left is definitely >= right, null if ambiguous
                if (leftLower >= rightUpper) return true;
                if (leftUpper < rightLower) return false;
                return null; // Intervals overlap, result is ambiguous
            }
            else
            {
                // > : true if left is completely after right
                if (leftLower > rightUpper) return true;
                if (leftUpper <= rightLower) return false;
                return null; // Intervals overlap
            }
        }
        else
        {
            if (orEqual)
            {
                // <= : true if left is definitely <= right, null if ambiguous
                if (leftUpper <= rightLower) return true;
                if (leftLower > rightUpper) return false;
                return null; // Intervals overlap
            }
            else
            {
                // < : true if left is completely before right
                if (leftUpper < rightLower) return true;
                if (leftLower >= rightUpper) return false;
                return null; // Intervals overlap
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

        if (value.StartsWith("T", StringComparison.Ordinal))
        {
            var timePart = value.Substring(1);
            var colonCount = timePart.Count(c => c == ':');
            if (colonCount == 0) return DateTimePrecision.Hour;
            if (colonCount == 1) return DateTimePrecision.Minute;

            return timePart.Contains('.', StringComparison.Ordinal) ? DateTimePrecision.Millisecond : DateTimePrecision.Second;
        }

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

    private static bool TryParseFhirDateTime(string value, out DateTimeOffset result)
    {
        if (value.StartsWith("T", StringComparison.Ordinal))
        {
            // Prepend dummy date for parsing
            value = "1900-01-01" + value;
        }
        return DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out result);
    }

        private DateTime? GetDateTimeLowerBound(string value, DateTimePrecision precision)
        {
            try
            {
                return precision switch
                {
                    DateTimePrecision.Year => new DateTime(int.Parse(value), 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    DateTimePrecision.Month => DateTime.ParseExact(value + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal).ToUniversalTime(),
                    _ => TryParseFhirDateTime(value, out var dt) ? dt.UtcDateTime : null
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
                if (precision == DateTimePrecision.Year)
                    return new DateTime(int.Parse(value), 12, 31, 23, 59, 59, 999, DateTimeKind.Utc);
    
                if (precision == DateTimePrecision.Month)
                    return DateTime.ParseExact(value + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal).ToUniversalTime().AddMonths(1).AddMilliseconds(-1);
    
                if (!TryParseFhirDateTime(value, out var dtOffset))
                    return null;
    
                var dt = dtOffset.UtcDateTime;
    
                return precision switch
                {
                    DateTimePrecision.Day => dt.Date.AddDays(1).AddMilliseconds(-1),
                    DateTimePrecision.Hour => dt.AddHours(1).AddMilliseconds(-1),
                    DateTimePrecision.Minute => dt.AddMinutes(1).AddMilliseconds(-1),
                    _ => dt
                };
            }
            catch
            {
                return null;
            }
        }

    private IEnumerable<IElement> EvaluateDateTimeArithmetic(string dateTimeStr, Types.Quantity quantity, bool add)
    {
        // Remove @ prefix if present
        dateTimeStr = dateTimeStr.StartsWith("@", StringComparison.Ordinal) ? dateTimeStr.Substring(1) : dateTimeStr;

        // Determine if this is a date, dateTime, or time
        var isTimeOnly = dateTimeStr.StartsWith("T", StringComparison.Ordinal);
        var precision = GetDateTimePrecision(dateTimeStr);

        if (precision == DateTimePrecision.Invalid)
            return [];

        // Parse the datetime
        if (!TryParseFhirDateTime(dateTimeStr, out var dt))
            return [];

        // Apply the quantity arithmetic based on unit
        // Calendar duration units (year, month, week, day) should be truncated to integers
        // Time-based units (hour, min, s, ms) can use fractional values
        var value = (double)quantity.Value * (add ? 1 : -1);
        DateTimeOffset result;

        try
        {
            result = quantity.Unit switch
            {
                "a" => dt.AddYears((int)Math.Truncate(value)),
                "mo" => dt.AddMonths((int)Math.Truncate(value)),
                "wk" => dt.AddDays(Math.Truncate(value) * 7),
                "d" or "day" or "days" => dt.AddDays(Math.Truncate(value)),
                "h" or "hour" or "hours" => dt.AddHours(value),
                "min" or "minute" or "minutes" => dt.AddMinutes(value),
                "s" or "second" or "seconds" => dt.AddSeconds(value),
                "ms" or "millisecond" or "milliseconds" => dt.AddMilliseconds(value),
                _ => dt // Unknown unit, return original
            };
        }
        catch
        {
            return []; // Overflow or invalid operation
        }

        // Format result to match input precision
        var resultStr = FormatDateTimeWithPrecision(result, precision, dateTimeStr);
        return [new PrimitiveElement("@" + resultStr, isTimeOnly ? "time" : (dateTimeStr.Contains('T', StringComparison.Ordinal) ? "dateTime" : "date"))];
    }

    private string FormatDateTimeWithPrecision(DateTimeOffset dt, DateTimePrecision precision, string originalStr)
    {
        var isTimeOnly = originalStr.StartsWith("T", StringComparison.Ordinal);
        
        // Preserve timezone from original string
        var hasTimeZone = originalStr.Contains('+', StringComparison.Ordinal) ||
                          (originalStr.Contains('-', StringComparison.Ordinal) && originalStr.LastIndexOf('-') > 10) ||
                          originalStr.EndsWith("Z", StringComparison.Ordinal);

        var format = precision switch
        {
            DateTimePrecision.Year => "yyyy",
            DateTimePrecision.Month => "yyyy-MM",
            DateTimePrecision.Day => "yyyy-MM-dd",
            DateTimePrecision.Hour => "yyyy-MM-dd'T'HH",
            DateTimePrecision.Minute => "yyyy-MM-dd'T'HH:mm",
            DateTimePrecision.Second => "yyyy-MM-dd'T'HH:mm:ss",
            DateTimePrecision.Millisecond => "yyyy-MM-dd'T'HH:mm:ss.fff",
            _ => "o"
        };

        var result = dt.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
        
        if (isTimeOnly)
        {
            var tIndex = result.IndexOf('T', StringComparison.Ordinal);
            result = result.Substring(tIndex);
        }
        else if (hasTimeZone && precision >= DateTimePrecision.Hour)
        {
            result += dt.ToString("zzz", System.Globalization.CultureInfo.InvariantCulture);
        }

        return result;
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
