/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath expression evaluator.
 * Executes parsed FhirPath AST against IElement trees.
 */

using Ignixa.FhirPath.Expressions;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation.Functions;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// Evaluates FhirPath expressions against FHIR resources represented as IElement trees.
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
    public IEnumerable<IElement> Evaluate(IElement input, Expression expression, EvaluationContext? context = null)
    {
        context ??= new EvaluationContext();

        return EvaluateExpression([input], expression, context);
    }

    private IEnumerable<IElement> EvaluateExpression(IEnumerable<IElement> focus, Expression expr, EvaluationContext context)
    {
        return expr switch
        {
            // Check specific types before base types (ChildExpression/BinaryExpression/UnaryExpression/IndexerExpression inherit from FunctionCallExpression)
            Expressions.ChildExpression child => EvaluateChildExpression(focus, child, context),
            Expressions.BinaryExpression binary => EvaluateBinaryExpression(focus, binary, context),
            Expressions.UnaryExpression unary => EvaluateUnary(focus, unary, context),
            Expressions.IndexerExpression indexer => EvaluateIndexer(focus, indexer, context),
            FunctionCallExpression func => EvaluateFunctionCall(focus, func, context),
            ConstantExpression constant => EvaluateConstant(constant),
            AxisExpression axis => EvaluateAxis(focus, axis, context),
            IdentifierExpression id => EvaluateIdentifier(focus, id),
            VariableRefExpression var => EvaluateVariable(var, context),
            ParenthesizedExpression paren => EvaluateExpression(focus, paren.InnerExpression, context),
            EmptyExpression => [],
            QuantityExpression quantityExpr => QuantityEvaluator.EvaluateQuantity(quantityExpr),
            _ => throw new NotSupportedException($"Expression type {expr.GetType().Name} is not yet supported")
        };
    }

    private IEnumerable<IElement> EvaluateChildExpression(IEnumerable<IElement> focus, Expressions.ChildExpression child, EvaluationContext context)
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

    private IEnumerable<IElement> EvaluateFunctionCall(IEnumerable<IElement> focus, FunctionCallExpression func, EvaluationContext context)
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
            // Collection functions
            "exists" => CollectionFunctions.Exists(focusElements, func.Arguments, context, EvaluateExpression),
            "empty" => CollectionFunctions.Empty(focusElements),
            "count" => CollectionFunctions.Count(focusElements),
            "distinct" => focusElements.Distinct(),
            "isdistinct" => CollectionFunctions.IsDistinct(focusElements),
            "first" => CollectionFunctions.First(focusElements),
            "last" => CollectionFunctions.Last(focusElements),
            "single" => CollectionFunctions.Single(focusElements),
            "tail" => CollectionFunctions.Tail(focusElements),
            "skip" => CollectionFunctions.Skip(focusElements, func.Arguments, context, EvaluateExpression),
            "take" => CollectionFunctions.Take(focusElements, func.Arguments, context, EvaluateExpression),
            "where" => CollectionFunctions.Where(focusElements, func.Arguments, context, EvaluateExpression),
            "select" => CollectionFunctions.Select(focusElements, func.Arguments, context, EvaluateExpression),
            "all" => CollectionFunctions.All(focusElements, func.Arguments, context, EvaluateExpression),
            "any" => CollectionFunctions.Any(focusElements, func.Arguments, context, EvaluateExpression),
            "repeat" => CollectionFunctions.Repeat(focusElements, func.Arguments, context, EvaluateExpression),
            "oftype" => CollectionFunctions.OfType(focusElements, func.Arguments, context, EvaluateExpression),
            "as" => CollectionFunctions.As(focusElements, func.Arguments),
            "intersect" => CollectionFunctions.Intersect(focusElements, func.Arguments, context, EvaluateExpression),
            "exclude" => CollectionFunctions.Exclude(focusElements, func.Arguments, context, EvaluateExpression),
            "union" => CollectionFunctions.Union(focusElements, func.Arguments, context, EvaluateExpression),
            "combine" => CollectionFunctions.Combine(focusElements, func.Arguments, context, EvaluateExpression),
            "subsetof" => CollectionFunctions.SubsetOf(focusElements, func.Arguments, context, EvaluateExpression),
            "supersetof" => CollectionFunctions.SupersetOf(focusElements, func.Arguments, context, EvaluateExpression),

            // Aggregate functions (Phase 23)
            "sum" => AggregateFunctions.Sum(focusElements),
            "min" => AggregateFunctions.Min(focusElements),
            "max" => AggregateFunctions.Max(focusElements),
            "avg" => AggregateFunctions.Avg(focusElements),

            // Boolean functions
            "alltrue" => BooleanFunctions.AllTrue(focusElements),
            "anytrue" => BooleanFunctions.AnyTrue(focusElements),
            "allfalse" => BooleanFunctions.AllFalse(focusElements),
            "anyfalse" => BooleanFunctions.AnyFalse(focusElements),
            "not" => BooleanFunctions.Not(focusElements),

            // Type conversion functions
            "tointeger" => TypeConversionFunctions.ToInteger(focusElements),
            "todecimal" => TypeConversionFunctions.ToDecimal(focusElements),
            "tostring" => TypeConversionFunctions.ToString(focusElements),
            "toboolean" => TypeConversionFunctions.ToBoolean(focusElements),
            "todate" => TypeConversionFunctions.ToDate(focusElements),
            "todatetime" => TypeConversionFunctions.ToDateTime(focusElements),
            "totime" => TypeConversionFunctions.ToTime(focusElements),
            "toquantity" => TypeConversionFunctions.ToQuantity(focusElements, func.Arguments),
            "convertstointeger" => TypeConversionFunctions.ConvertsToInteger(focusElements),
            "convertstodecimal" => TypeConversionFunctions.ConvertsToDecimal(focusElements),
            "convertstostring" => TypeConversionFunctions.ConvertsToString(focusElements),
            "convertstoboolean" => TypeConversionFunctions.ConvertsToBoolean(focusElements),
            "convertstodate" => TypeConversionFunctions.ConvertsToDate(focusElements),
            "convertstodatetime" => TypeConversionFunctions.ConvertsToDateTime(focusElements),
            "convertstotime" => TypeConversionFunctions.ConvertsToTime(focusElements),
            "convertstoquantity" => TypeConversionFunctions.ConvertsToQuantity(focusElements, func.Arguments),

            // Conditional function
            "iif" => ConditionalFunctions.Iif(focusElements, func.Arguments, context, EvaluateExpression),

            // String manipulation functions
            "indexof" => StringFunctions.IndexOf(focusElements, func.Arguments, context, EvaluateExpression),
            "substring" => StringFunctions.Substring(focusElements, func.Arguments, context, EvaluateExpression),
            "startswith" => StringFunctions.StartsWith(focusElements, func.Arguments, context, EvaluateExpression),
            "endswith" => StringFunctions.EndsWith(focusElements, func.Arguments, context, EvaluateExpression),
            "upper" => StringFunctions.Upper(focusElements),
            "lower" => StringFunctions.Lower(focusElements),
            "length" => StringFunctions.Length(focusElements),
            "replace" => StringFunctions.Replace(focusElements, func.Arguments, context, EvaluateExpression),
            "matches" => StringFunctions.Matches(focusElements, func.Arguments, context, EvaluateExpression),
            "replacematches" => StringFunctions.ReplaceMatches(focusElements, func.Arguments, context, EvaluateExpression),
            "tochars" => StringFunctions.ToChars(focusElements),
            "join" => StringFunctions.Join(focusElements, func.Arguments, context, EvaluateExpression),

            // Boundary functions
            "lowboundary" => BoundaryFunctions.LowBoundary(focusElements),
            "highboundary" => BoundaryFunctions.HighBoundary(focusElements),

            // Tree navigation functions
            "children" => TreeNavigationFunctions.Children(focusElements),
            "descendants" => TreeNavigationFunctions.Descendants(focusElements),

            // FHIR-specific functions
            "extension" => FhirSpecificFunctions.Extension(focusElements, func.Arguments, context, EvaluateExpression),
            "resolve" => FhirSpecificFunctions.Resolve(focusElements, context),
            "getresourcekey" => FhirSpecificFunctions.GetResourceKey(context),
            "getreferencekey" => FhirSpecificFunctions.GetReferenceKey(focusElements, func.Arguments, context, EvaluateExpression),

            // Utility functions
            "trace" => UtilityFunctions.Trace(focusElements, func.Arguments, context),
            "now" => UtilityFunctions.Now(focusElements),
            "today" => UtilityFunctions.Today(focusElements),
            "timeofday" => UtilityFunctions.TimeOfDay(focusElements),

            // Date/Time component extraction functions (Phase 23)
            "year" => DateTimeFunctions.Year(focusElements),
            "month" => DateTimeFunctions.Month(focusElements),
            "day" => DateTimeFunctions.Day(focusElements),
            "hour" => DateTimeFunctions.Hour(focusElements),
            "minute" => DateTimeFunctions.Minute(focusElements),
            "second" => DateTimeFunctions.Second(focusElements),
            "millisecond" => DateTimeFunctions.Millisecond(focusElements),
            "timezone" => DateTimeFunctions.Timezone(focusElements),

            // For bare identifiers (e.g., "Patient"), treat as child navigation
            _ when func.Arguments.Count == 0 && func.Focus == AxisExpression.That
                => EvaluateIdentifier(focus, new IdentifierExpression(func.FunctionName)),

            _ => throw new NotSupportedException($"Function '{func.FunctionName}' is not yet implemented")
        };
    }

    private IEnumerable<IElement> EvaluateIdentifier(IEnumerable<IElement> focus, IdentifierExpression id)
    {
        // Identifiers navigate to child elements, with special handling for resource type names
        foreach (var element in focus)
        {
            // Check if identifier starts with uppercase (resource/type names are capitalized)
            if (id.Name.Length > 0 && char.IsUpper(id.Name[0]))
            {
                // If we are at a resource, we should match a path that is possibly not rooted in the resource
                // (e.g. doing "name.family" on a Patient is equivalent to "Patient.name.family")
                // Also we do some poor polymorphism here: Resource.meta.lastUpdated is also allowed.
                string[] baseClasses = ["Resource", "DomainResource"];
                if (element.InstanceType == id.Name || baseClasses.Contains(id.Name))
                {
                    yield return element;
                    continue;
                }
            }

            // Navigate to child elements with this name
            foreach (var child in element.Children(id.Name))
            {
                yield return child;
            }
        }
    }

    private IEnumerable<IElement> EvaluateBinaryExpression(IEnumerable<IElement> focus, Expressions.BinaryExpression binary, EvaluationContext context)
    {
        var left = EvaluateExpression(focus, binary.Left, context).ToList();
        var right = EvaluateExpression(focus, binary.Right, context).ToList();

        // FhirPath operators are case-insensitive, ToLowerInvariant is intentional
#pragma warning disable CA1308 // Normalize strings to uppercase
        return binary.Operator.ToLowerInvariant() switch
#pragma warning restore CA1308 // Normalize strings to uppercase
        {
            // Collection operators (return collections)
            "|" => EvaluateUnion(left, right),

            // Math operators (return numeric values)
            "+" => EvaluateAddition(left, right),
            "-" => EvaluateSubtraction(left, right),
            "*" => EvaluateMultiplication(left, right),
            "/" => EvaluateDivision(left, right),
            "div" => EvaluateIntegerDivision(left, right),
            "mod" => EvaluateModulo(left, right),

            // String concatenation (returns string)
            "&" => EvaluateStringConcatenation(left, right),

            // Type operators (special handling for identifiers)
            "is" => EvaluateTypeIs(left, binary.Right),
            "as" => EvaluateTypeAs(left, binary.Right),

            // Membership operators (return boolean)
            "in" => FunctionHelpers.ReturnBoolean(EvaluateMembership(left, right, isIn: true)),
            "contains" => FunctionHelpers.ReturnBoolean(EvaluateMembership(left, right, isIn: false)),

            // Comparison operators (return boolean)
            "=" => FunctionHelpers.ReturnBoolean(CompareEquality(left, right, equals: true)),
            "!=" => FunctionHelpers.ReturnBoolean(CompareEquality(left, right, equals: false)),
            "~" => FunctionHelpers.ReturnBoolean(CompareEquivalence(left, right, equivalent: true)),
            "!~" => FunctionHelpers.ReturnBoolean(CompareEquivalence(left, right, equivalent: false)),
            ">" => FunctionHelpers.ReturnBoolean(CompareOrder(left, right, greater: true, orEqual: false)),
            ">=" => FunctionHelpers.ReturnBoolean(CompareOrder(left, right, greater: true, orEqual: true)),
            "<" => FunctionHelpers.ReturnBoolean(CompareOrder(left, right, greater: false, orEqual: false)),
            "<=" => FunctionHelpers.ReturnBoolean(CompareOrder(left, right, greater: false, orEqual: true)),

            // Logical operators (return boolean)
            "and" => FunctionHelpers.ReturnBoolean(FunctionHelpers.IsTrue(left) && FunctionHelpers.IsTrue(right)),
            "or" => FunctionHelpers.ReturnBoolean(FunctionHelpers.IsTrue(left) || FunctionHelpers.IsTrue(right)),
            "xor" => FunctionHelpers.ReturnBoolean(FunctionHelpers.IsTrue(left) ^ FunctionHelpers.IsTrue(right)),
            "implies" => FunctionHelpers.ReturnBoolean(!FunctionHelpers.IsTrue(left) || FunctionHelpers.IsTrue(right)),

            _ => throw new NotSupportedException($"Binary operator '{binary.Operator}' is not yet implemented")
        };
    }


    // Union operator: Merge collections, eliminate duplicates
    private IEnumerable<IElement> EvaluateUnion(List<IElement> left, List<IElement> right)
    {
        return FunctionHelpers.EvaluateUnion(left, right);
    }

    // Math operators
    private IEnumerable<IElement> EvaluateAddition(List<IElement> left, List<IElement> right)
    {
        if (left.Count != 1 || right.Count != 1)
            return [];

        var leftValue = left[0].Value;
        var rightValue = right[0].Value;

        // Try quantity arithmetic first
        if (leftValue is Types.Quantity || rightValue is Types.Quantity)
        {
            return QuantityEvaluator.EvaluateArithmetic(left, "+", right);
        }

        // Try numeric addition with implicit Integer->Decimal conversion
        if (FunctionHelpers.TryConvertToDecimal(leftValue, out var leftDecimal) && FunctionHelpers.TryConvertToDecimal(rightValue, out var rightDecimal))
        {
            var result = leftDecimal + rightDecimal;
            // Return Integer if both were Integer, otherwise Decimal
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

        // Try quantity arithmetic first
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

        // Try quantity arithmetic first
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

        // Try quantity arithmetic first
        if (leftValue is Types.Quantity || rightValue is Types.Quantity)
        {
            return QuantityEvaluator.EvaluateArithmetic(left, "/", right);
        }

        if (FunctionHelpers.TryConvertToDecimal(leftValue, out var leftDecimal) && FunctionHelpers.TryConvertToDecimal(rightValue, out var rightDecimal))
        {
            if (rightDecimal == 0)
                return []; // Division by zero returns empty

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

    // String concatenation
    private IEnumerable<IElement> EvaluateStringConcatenation(List<IElement> left, List<IElement> right)
    {
        if (left.Count != 1 || right.Count != 1)
            return [];

        var leftStr = left[0].Value?.ToString() ?? string.Empty;
        var rightStr = right[0].Value?.ToString() ?? string.Empty;

        return [new PrimitiveElement(leftStr + rightStr, "string")];
    }

    // Type operators
    private IEnumerable<IElement> EvaluateTypeIs(List<IElement> left, Expression typeExpr)
    {
        if (left.Count != 1)
            return [];

        // Extract type name from identifier or function call expression
        // NOTE: Parser treats bare identifiers as function calls (e.g., "integer" = "integer()")
        string? typeName = null;
        if (typeExpr is IdentifierExpression idExpr)
        {
            typeName = idExpr.Name;
        }
        else if (typeExpr is FunctionCallExpression funcExpr)
        {
            typeName = funcExpr.FunctionName;
        }

        if (typeName == null)
            return [];

        // FhirPath type names are lowercase, ToLowerInvariant is intentional
#pragma warning disable CA1308 // Normalize strings to uppercase
        typeName = typeName.ToLowerInvariant();
        var elementType = left[0].InstanceType?.ToLowerInvariant() ?? string.Empty;
#pragma warning restore CA1308 // Normalize strings to uppercase

        // Simple type checking (can be enhanced for inheritance)
        return FunctionHelpers.ReturnBoolean(elementType == typeName);
    }

    private IEnumerable<IElement> EvaluateTypeAs(List<IElement> left, Expression typeExpr)
    {
        if (left.Count != 1)
            return [];

        // Extract type name from identifier or function call expression
        // NOTE: Parser treats bare identifiers as function calls (e.g., "integer" = "integer()")
        string? typeName = null;
        if (typeExpr is IdentifierExpression idExpr)
        {
            typeName = idExpr.Name;
        }
        else if (typeExpr is FunctionCallExpression funcExpr)
        {
            typeName = funcExpr.FunctionName;
        }

        if (typeName == null)
            return [];

        // FhirPath type names are lowercase, ToLowerInvariant is intentional
#pragma warning disable CA1308 // Normalize strings to uppercase
        typeName = typeName.ToLowerInvariant();
        var elementType = left[0].InstanceType?.ToLowerInvariant() ?? string.Empty;
#pragma warning restore CA1308 // Normalize strings to uppercase

        // Return element if type matches, empty otherwise
        return elementType == typeName ? [left[0]] : [];
    }

    // Membership operators
    private bool? EvaluateMembership(List<IElement> left, List<IElement> right, bool isIn)
    {
        // 'in' operator: left operand must be single item
        // 'contains' operator: right operand must be single item
        var singleItem = isIn ? left : right;
        var collection = isIn ? right : left;

        if (singleItem.Count == 0)
            return null; // Empty -> empty result

        if (singleItem.Count != 1)
            return null; // More than one item -> error (return null for now, should signal error)

        if (collection.Count == 0)
            return false; // Item not in empty collection

        var itemValue = singleItem[0].Value;
        return collection.Any(c => FunctionHelpers.AreEqual(c.Value, itemValue));
    }

    // Equivalence comparison
    private bool? CompareEquivalence(List<IElement> left, List<IElement> right, bool equivalent)
    {
        // Empty collections are equivalent
        if (left.Count == 0 && right.Count == 0)
            return equivalent;

        // Different counts are not equivalent
        if (left.Count != right.Count)
            return !equivalent;

        // For single items, compare with normalization
        if (left.Count == 1 && right.Count == 1)
        {
            var isEquiv = AreEquivalent(left[0].Value, right[0].Value);
            return isEquiv == equivalent;
        }

        // For multiple items, order-independent comparison
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

        // String equivalence: case-insensitive, whitespace-normalized
        if (left is string leftStr && right is string rightStr)
        {
            return string.Equals(
                NormalizeWhitespace(leftStr),
                NormalizeWhitespace(rightStr),
                StringComparison.OrdinalIgnoreCase);
        }

        // Numeric equivalence with rounding to least precise
        if (left is decimal || right is decimal || left is int || right is int)
        {
            if (FunctionHelpers.TryConvertToDecimal(left, out var leftDec) && FunctionHelpers.TryConvertToDecimal(right, out var rightDec))
                return leftDec == rightDec;
        }

        return left.Equals(right);
    }

    private string NormalizeWhitespace(string str)
    {
        // Normalize all whitespace characters to single space
        return System.Text.RegularExpressions.Regex.Replace(str.Trim(), @"\s+", " ");
    }


    private IEnumerable<IElement> EvaluateAxis(IEnumerable<IElement> focus, AxisExpression axis, EvaluationContext context)
    {
        // FhirPath axis names are case-insensitive, ToLowerInvariant is intentional
#pragma warning disable CA1308 // Normalize strings to uppercase
        return axis.AxisName.ToLowerInvariant() switch
#pragma warning restore CA1308 // Normalize strings to uppercase
        {
            "this" => context.GetEnvironmentVariable("this") is IElement thisElement
                ? [thisElement]
                : focus,
            "that" => focus,
            _ => throw new NotSupportedException($"Axis '${axis.AxisName}' is not yet implemented")
        };
    }

    private IEnumerable<IElement> EvaluateVariable(VariableRefExpression var, EvaluationContext context)
    {
        var value = context.GetEnvironmentVariable(var.Name);

        // Special handling for predefined variables that may not exist
        if (var.Name is "this" or "index")
        {
            // These variables are optional and may not be defined
            if (value == null)
                return [];
            if (value is IElement element)
                return [element];
            if (value is IEnumerable<IElement> elements)
                return elements;
            return [];
        }

        // Per FHIRPath specification, accessing undefined variables returns empty collection
        // (not an error) - allows for defensive expressions like %resource.where(...)
        if (value == null)
        {
            return [];
        }

        // Handle both single element and collection returns
        if (value is IElement element2)
            return [element2];
        if (value is IEnumerable<IElement> elements2)
            return elements2;

        // If it's neither, return empty
        return [];
    }

    private IEnumerable<IElement> EvaluateConstant(ConstantExpression constant)
    {
        return constant.Value switch
        {
            int i => [CreateInteger(i)],
            decimal d => [CreateDecimal(d)],
            bool b => [CreateBoolean(b)],
            string s => [CreateDateTimeOrString(s)],
            _ => [CreateConstant(constant.Value)]
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

        // Check for date/time literal prefix
        if (!value.StartsWith("@", StringComparison.Ordinal))
            return CreateString(value);

        // Remove @ prefix for type detection
        var dateTimeValue = value.Substring(1);

        // Time literal: @THH:MM:SS
        if (dateTimeValue.StartsWith("T", StringComparison.Ordinal))
        {
            return new PrimitiveElement(value, "time");
        }

        // DateTime literal: @YYYY-MM-DDTHH:MM:SS
        if (dateTimeValue.Contains('T', StringComparison.Ordinal))
        {
            return new PrimitiveElement(value, "dateTime");
        }

        // Date literal: @YYYY, @YYYY-MM, @YYYY-MM-DD
        return new PrimitiveElement(value, "date");
    }

    private IEnumerable<IElement> EvaluateIndexer(IEnumerable<IElement> focus, Expressions.IndexerExpression indexer, EvaluationContext context)
    {
        var collection = EvaluateExpression(focus, indexer.Collection, context).ToList();
        var indexResults = EvaluateExpression(focus, indexer.Index, context).ToList();

        if (indexResults.Count == 1 && indexResults[0].Value is int index)
        {
            if (index >= 0 && index < collection.Count)
            {
                return [collection[index]];
            }
        }

        return [];
    }

    private IEnumerable<IElement> EvaluateUnary(IEnumerable<IElement> focus, Expressions.UnaryExpression unary, EvaluationContext context)
    {
        var operand = EvaluateExpression(focus, unary.Operand, context).ToList();

        if (unary.Operator == "-" && operand.Count == 1)
        {
            var value = operand[0].Value;
            try
            {
                // Preserve integer type if possible
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
                // Ignore conversion errors
            }
        }

        return operand;
    }


    private bool? CompareEquality(List<IElement> left, List<IElement> right, bool equals)
    {
        // Empty collections: return empty (null means empty result)
        if (left.Count == 0 || right.Count == 0)
            return null;

        if (left.Count != right.Count)
            return !equals;

        // Special handling for Quantity comparisons with unit conversion
        if (left.Count == 1 && right.Count == 1 &&
            (left[0].Value is Types.Quantity || right[0].Value is Types.Quantity))
        {
            var result = QuantityEvaluator.EvaluateComparison(left, equals ? "=" : "!=", right);
            return result;
        }

        for (int i = 0; i < left.Count; i++)
        {
            var isEqual = FunctionHelpers.AreEqual(left[i].Value, right[i].Value);
            if (isEqual != equals) return false;
        }

        return true;
    }

    private bool? CompareOrder(List<IElement> left, List<IElement> right, bool greater, bool orEqual)
    {
        // Empty collections: return empty (null means empty result)
        if (left.Count == 0 || right.Count == 0)
            return null;

        if (left.Count != 1 || right.Count != 1)
            return null; // Multiple items: undefined

        var leftValue = left[0].Value;
        var rightValue = right[0].Value;

        // Special handling for Quantity comparisons with unit conversion
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


    // Factory methods for creating primitive IElement instances
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
            _ => "string" // Default fallback
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
