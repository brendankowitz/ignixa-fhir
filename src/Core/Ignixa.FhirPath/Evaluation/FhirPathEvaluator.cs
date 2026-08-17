/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath expression evaluator.
 * Executes parsed FhirPath AST against IElement trees.
 * Uses immutable EvaluationContext for pure functional evaluation.
 */

using System.Collections.Frozen;
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
    /// <param name="input">The node to evaluate against - not necessarily a resource</param>
    /// <param name="expression">The parsed FhirPath expression</param>
    /// <param name="context">Optional evaluation context</param>
    /// <returns>Collection of elements that match the expression</returns>
    /// <remarks>
    /// <para>
    /// <b><c>%context</c> is filled in from <paramref name="input"/>; <c>%resource</c> is not.</b> The FHIRPath
    /// specification defines <c>%context</c> as "the original node that was passed to the evaluation engine",
    /// which is exactly this argument, whereas FHIR defines <c>%resource</c> as "the resource that contains the
    /// original node that is in %context" - a node this method has no way to find, because
    /// <see cref="IElement"/> carries no parent link. The host binds it or it resolves to empty.
    /// </para>
    /// <para>
    /// This is why <see cref="TypedElementExtensions.Select"/> defaults <c>%resource</c> and this method does
    /// not: that overload's contract names its input "the root element", while this one's callers routinely
    /// pass a sub-element. See the remarks on that method for the full reasoning; the two are deliberately
    /// different, not accidentally inconsistent.
    /// </para>
    /// <para>
    /// For best performance, use a <see cref="Parser.FhirPathParser"/> with <see cref="Parsing.CompilationOptions.Optimize"/>
    /// set to true to optimize expressions at parse-time rather than evaluation-time.
    /// </para>
    /// </remarks>
    public IEnumerable<IElement> Evaluate(IElement input, Expression expression, EvaluationContext? context = null)
    {
        context ??= new EvaluationContext();

        // %context is "the original node that was passed to the evaluation engine", so this is the only
        // place that can know it. A caller that set it explicitly keeps its own choice.
        if (context.ContextNode is null)
        {
            context = context.WithContextNode(input);
        }

        // Push the root element onto the $this stack so $this resolves correctly throughout evaluation
        context = context.PushThis(input);

        return EvaluateExpression([input], expression, context);
    }

    private IEnumerable<IElement> EvaluateExpression(IEnumerable<IElement> focus, Expression expr, EvaluationContext context)
    {
        // Optimization: Skip context creation if focus hasn't changed
        // This is common in indexer/child/binary expressions where we evaluate sub-expressions with the same focus
        EvaluationContext effectiveContext;
        if (ReferenceEquals(focus, context.Focus))
        {
            effectiveContext = context;
        }
        else
        {
            effectiveContext = context.WithFocus(focus);
        }

        var results = expr.AcceptVisitor(this, effectiveContext);

        // If a node evaluation handler is set, materialize results and notify
        if (context.NodeEvaluationHandler != null)
        {
            // Optimization: Check if results are already materialized to avoid redundant enumeration
            var materializedResults = results as ImmutableList<IElement> ?? results.ToImmutableList();
            var entry = new NodeEvaluationEntry(
                expr,
                materializedResults,
                effectiveContext.Focus,
                effectiveContext.GetThis(),
                effectiveContext.GetIndex());
            context.NodeEvaluationHandler(entry);
            return materializedResults;
        }

        return results;
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
        if (IsPositionalFunction(expression.FunctionName))
        {
            var unorderedSource = GetUnorderedNavigationSource(expression.Focus);
            if (unorderedSource != null)
            {
                // Result is undefined per FHIRPath spec. Return empty rather than throw;
                // FhirPathAnalyzer surfaces this as a design-time error.
                return [];
            }
        }

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
            throw new FhirPathEvaluationException("defineVariable requires 1 or 2 arguments: variable name and optional value expression");
        }

        var nameExpr = expression.Arguments[0];
        string? variableName = null;

        if (nameExpr is ConstantExpression constExpr && constExpr.Value is string str)
        {
            variableName = str;
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
            throw new FhirPathEvaluationException("defineVariable requires a string as the first argument (literal, identifier, or expression that evaluates to a string)");
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

        if (DefineVariableRules.ReservedVariableNames.Contains(variableName))
        {
            throw new FhirPathEvaluationException(
                $"defineVariable cannot redefine the system variable '%{variableName}'");
        }

        if (DefineVariableRules.IsAlreadyDefinedEarlierInSameChain(expression, variableName))
        {
            throw new FhirPathEvaluationException($"Variable '%{variableName}' is already defined");
        }

        context.Variables.Define(variableName, value);

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
        // For union operator, each branch should have isolated variable scope
        // Variables defined in left branch should NOT be visible in right branch
        if (expression.Operator == "|")
        {
            // For union operator, each branch should have isolated variable scope
            // Variables defined in one branch should NOT be visible in sibling branches
            var leftContext = context.ForkVariableScope();
            var rightContext = context.ForkVariableScope();

            var left = EvaluateExpression(context.Focus, expression.Left, leftContext).ToList();
            var right = EvaluateExpression(context.Focus, expression.Right, rightContext).ToList();

            return EvaluateUnion(left, right);
        }

#pragma warning disable CA1308 // Normalize strings to uppercase
        var operatorName = expression.Operator.ToLowerInvariant();
#pragma warning restore CA1308 // Normalize strings to uppercase

        if (operatorName is "and" or "or" or "implies")
        {
            return EvaluateShortCircuitingLogic(operatorName, expression, context);
        }

        var leftResult = EvaluateExpression(context.Focus, expression.Left, context).ToList();
        var rightResult = EvaluateExpression(context.Focus, expression.Right, context).ToList();

        return operatorName switch
        {
            "+" => EvaluateAddition(leftResult, rightResult),
            "-" => EvaluateSubtraction(leftResult, rightResult),
            "*" => EvaluateMultiplication(leftResult, rightResult),
            "/" => EvaluateDivision(leftResult, rightResult),
            "div" => EvaluateIntegerDivision(leftResult, rightResult),
            "mod" => EvaluateModulo(leftResult, rightResult),

            "&" => EvaluateStringConcatenation(leftResult, rightResult),

            "is" => EvaluateTypeIs(leftResult, expression.Right, context),
            "as" => EvaluateTypeAs(leftResult, expression.Right, context),

            "in" => FunctionHelpers.ReturnBoolean(EvaluateMembership(leftResult, rightResult, isIn: true)),
            "contains" => FunctionHelpers.ReturnBoolean(EvaluateMembership(leftResult, rightResult, isIn: false)),

            "=" => FunctionHelpers.ReturnBoolean(CompareEquality(leftResult, rightResult, equals: true)),
            "!=" => FunctionHelpers.ReturnBoolean(CompareEquality(leftResult, rightResult, equals: false)),
            "~" => FunctionHelpers.ReturnBoolean(CompareEquivalence(leftResult, rightResult, equivalent: true)),
            "!~" => FunctionHelpers.ReturnBoolean(CompareEquivalence(leftResult, rightResult, equivalent: false)),
            ">" => FunctionHelpers.ReturnBoolean(CompareOrder(leftResult, rightResult, greater: true, orEqual: false)),
            ">=" => FunctionHelpers.ReturnBoolean(CompareOrder(leftResult, rightResult, greater: true, orEqual: true)),
            "<" => FunctionHelpers.ReturnBoolean(CompareOrder(leftResult, rightResult, greater: false, orEqual: false)),
            "<=" => FunctionHelpers.ReturnBoolean(CompareOrder(leftResult, rightResult, greater: false, orEqual: true)),

            "xor" => EvaluateXor(GetBooleanValue(leftResult), GetBooleanValue(rightResult)),

            _ => throw new NotSupportedException($"Binary operator '{expression.Operator}' is not yet implemented")
        };
    }

    /// <summary>
    /// Evaluates <c>and</c>, <c>or</c> and <c>implies</c>, evaluating the right operand only when the
    /// left one has not already decided the answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every operand of a FHIRPath expression can signal an error, so whether an operand is evaluated at
    /// all is observable behaviour, not an optimisation. R4's <c>tim-9</c> is the case that forces this:
    /// <c>offset.empty() or (when.exists() and ((when in (…)).not()))</c> is written so that the guard on
    /// the left makes the ill-formed right side unreachable, and evaluating both operands eagerly defeats
    /// the guard.
    /// </para>
    /// <para>
    /// Only the three cells that hold across the whole row of the spec's truth tables (§6.5 Boolean logic)
    /// are short-circuited - see <see cref="DecideFromLeftOperand"/>. Empty is a distinct third state and
    /// never decides a result on its own, so it always evaluates the right operand and falls through to
    /// the full three-valued tables below.
    /// </para>
    /// </remarks>
    private IEnumerable<IElement> EvaluateShortCircuitingLogic(string operatorName, BinaryExpression expression, EvaluationContext context)
    {
        var left = GetBooleanValue(EvaluateExpression(context.Focus, expression.Left, context).ToList());

        if (DecideFromLeftOperand(operatorName, left) is { } decided)
        {
            return FunctionHelpers.ReturnBoolean(decided);
        }

        var right = GetBooleanValue(EvaluateExpression(context.Focus, expression.Right, context).ToList());

        return operatorName switch
        {
            "and" => EvaluateAnd(left, right),
            "or" => EvaluateOr(left, right),
            _ => EvaluateImplies(left, right)
        };
    }

    /// <summary>
    /// Returns the result the left operand alone determines, or null when the right operand is still needed.
    /// </summary>
    /// <remarks>
    /// The three cases are exactly the rows of the spec's truth tables whose cells are constant across
    /// <c>true</c>, <c>false</c> and empty: <c>false and *</c> is always false, <c>true or *</c> is always
    /// true, and <c>false implies *</c> is always true. No other row is constant, and in particular no
    /// row keyed by an empty left operand is.
    /// </remarks>
    private static bool? DecideFromLeftOperand(string operatorName, bool? left) => (operatorName, left) switch
    {
        ("and", false) => false,
        ("or", true) => true,
        ("implies", false) => true,
        _ => null
    };


    private IEnumerable<IElement> EvaluateUnion(List<IElement> left, List<IElement> right)
    {
        return FunctionHelpers.EvaluateUnion(left, right);
    }

    /// <summary>
    /// Evaluates the AND operator with FHIRPath three-valued logic.
    /// Returns false if either is false, empty if either is empty and neither is false, otherwise true.
    /// </summary>
    private static IEnumerable<IElement> EvaluateAnd(bool? leftBool, bool? rightBool)
    {
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
    private static IEnumerable<IElement> EvaluateOr(bool? leftBool, bool? rightBool)
    {
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
    private static IEnumerable<IElement> EvaluateXor(bool? leftBool, bool? rightBool)
    {
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
    private static IEnumerable<IElement> EvaluateImplies(bool? leftBool, bool? rightBool)
    {
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

    private static bool IsTemporalInstanceType(string? instanceType)
        => instanceType is "date" or "dateTime" or "instant" or "time";

    private static bool IsTemporalElement(IElement element)
        => IsTemporalInstanceType(element.InstanceType) || element.Value is FhirTemporal;

    /// <summary>
    /// Enforces the FHIRPath rule that the other operand of <c>+</c>/<c>-</c> on a Date, DateTime or Time
    /// must be a Quantity with a time-valued unit; anything else signals an error rather than producing a
    /// value (FHIRPath 3.0 "Date/Time Arithmetic", official test <c>testPlus6</c>: <c>@1974-12-25 + 7</c>).
    /// </summary>
    /// <remarks>
    /// Only reached once both operands are known to be single items, so the spec's empty-propagation rule
    /// (<c>1 + {}</c> is empty, not an error) is already satisfied by the arity guard in the callers.
    /// </remarks>
    private static void ThrowIfTemporalWithoutQuantity(IElement left, IElement right, string operatorSymbol)
    {
        if (!IsTemporalElement(left) && !IsTemporalElement(right))
        {
            return;
        }

        var temporal = IsTemporalElement(left) ? left : right;
        var other = ReferenceEquals(temporal, left) ? right : left;

        throw new FhirPathEvaluationException(
            $"Operator '{operatorSymbol}' on a {temporal.InstanceType} requires a Quantity with a time-valued unit, " +
            $"but the other operand was of type '{other.InstanceType ?? "unknown"}'.");
    }

    /// <summary>
    /// Builds the error for a math operator applied to operands whose types it is not defined for. FHIRPath's
    /// Math preamble requires both operands to be of the same or compatible types and makes anything else an
    /// error, not an empty result (official test <c>testMinus4</c>: <c>'a' - 'b'</c>).
    /// </summary>
    /// <remarks>
    /// Only reached once both operands are known to be single items, so the spec's empty-propagation rule
    /// (<c>1 * {}</c> is empty) and its divide-by-zero rule (<c>1 / 0</c> is empty) are already satisfied by
    /// the guards above every call site.
    /// </remarks>
    private static FhirPathEvaluationException UndefinedForOperandTypes(IElement left, IElement right, string operatorSymbol)
        => new($"Operator '{operatorSymbol}' is not defined for operands of type " +
               $"'{DescribeOperandType(left)}' and '{DescribeOperandType(right)}'.");

    private static string DescribeOperandType(IElement element)
        => element.InstanceType ?? element.Value?.GetType().Name ?? "unknown";

    private IEnumerable<IElement> EvaluateAddition(List<IElement> left, List<IElement> right)
    {
        if (left.Count != 1 || right.Count != 1)
            return [];

        var leftValue = left[0].Value;
        var rightValue = right[0].Value;

        // Date/DateTime/Time + Quantity
        if (WireValue.AsWireString(leftValue) is { } leftDateStr && rightValue is Types.Quantity rightQty)
        {
            return EvaluateDateTimeArithmetic(leftDateStr, rightQty, add: true, left[0].InstanceType);
        }

        // Quantity + Date/DateTime/Time
        if (leftValue is Types.Quantity leftQty && WireValue.AsWireString(rightValue) is { } rightDateStr)
        {
            return EvaluateDateTimeArithmetic(rightDateStr, leftQty, add: true, right[0].InstanceType);
        }

        if (leftValue is Types.Quantity || rightValue is Types.Quantity)
        {
            return QuantityEvaluator.EvaluateArithmetic(left, "+", right);
        }

        ThrowIfTemporalWithoutQuantity(left[0], right[0], "+");

        // String concatenation via + operator
        if (WireValue.AsWireString(leftValue) is { } leftStringVal && WireValue.AsWireString(rightValue) is { } rightStringVal)
        {
            return [CreateString(leftStringVal + rightStringVal)];
        }

        if (FunctionHelpers.TryConvertToDecimal(leftValue, out var leftDecimal) && FunctionHelpers.TryConvertToDecimal(rightValue, out var rightDecimal))
        {
            var result = leftDecimal + rightDecimal;
            return leftValue is int && rightValue is int && result == Math.Floor(result)
                ? [CreateInteger((int)result)]
                : [CreateDecimal(result)];
        }

        throw UndefinedForOperandTypes(left[0], right[0], "+");
    }

    private IEnumerable<IElement> EvaluateSubtraction(List<IElement> left, List<IElement> right)
    {
        if (left.Count != 1 || right.Count != 1)
            return [];

        var leftValue = left[0].Value;
        var rightValue = right[0].Value;

        // Date/DateTime/Time - Quantity
        if (WireValue.AsWireString(leftValue) is { } leftStr && rightValue is Types.Quantity qty)
        {
            return EvaluateDateTimeArithmetic(leftStr, qty, add: false, left[0].InstanceType);
        }

        if (leftValue is Types.Quantity || rightValue is Types.Quantity)
        {
            return QuantityEvaluator.EvaluateArithmetic(left, "-", right);
        }

        ThrowIfTemporalWithoutQuantity(left[0], right[0], "-");

        if (FunctionHelpers.TryConvertToDecimal(leftValue, out var leftDecimal) && FunctionHelpers.TryConvertToDecimal(rightValue, out var rightDecimal))
        {
            var result = leftDecimal - rightDecimal;
            return leftValue is int && rightValue is int && result == Math.Floor(result)
                ? [CreateInteger((int)result)]
                : [CreateDecimal(result)];
        }

        throw UndefinedForOperandTypes(left[0], right[0], "-");
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

        throw UndefinedForOperandTypes(left[0], right[0], "*");
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

        throw UndefinedForOperandTypes(left[0], right[0], "/");
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

        throw UndefinedForOperandTypes(left[0], right[0], "div");
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

        throw UndefinedForOperandTypes(left[0], right[0], "mod");
    }

    private IEnumerable<IElement> EvaluateStringConcatenation(List<IElement> left, List<IElement> right)
    {
        // FHIRPath spec: Empty collections are treated as empty strings for concatenation
        // '1' & {} = '1', {} & 'b' = 'b'
        if (left.Count > 1 || right.Count > 1)
        {
            // Official test testConcatenate4: (1 | 2 | 3) & 'b' is an error, not an empty result.
            throw new FhirPathEvaluationException(
                $"Operator '&' requires singleton operands, but was given {left.Count} item(s) on the left and {right.Count} item(s) on the right.");
        }

        var leftStr = left.Count == 1 ? (left[0].Value?.ToString() ?? string.Empty) : string.Empty;
        var rightStr = right.Count == 1 ? (right[0].Value?.ToString() ?? string.Empty) : string.Empty;

        return [new PrimitiveElement(leftStr + rightStr, "string")];
    }

    private IEnumerable<IElement> EvaluateTypeIs(List<IElement> left, Expression typeExpr, EvaluationContext context)
    {
        var typeName = TypeMatcher.ExtractTypeName(typeExpr);
        if (string.IsNullOrEmpty(typeName))
            return [];

        // Both of these are mandated by the same two sentences of the spec, and both are checked before
        // the empty-input exit for the same reason 'as' checks them first: whether the identifier names a
        // type, and whether the operand is a collection, are facts about the expression, so hiding them
        // behind the data would let a nonsense type sit undetected until a resource happened to populate
        // the path. See TypeMatcher.EnsureSingletonTypeTestInput for why this one is not version gated
        // while the matching rule on 'as' is.
        TypeMatcher.EnsureTypeIdentifierResolves(typeName, context.Schema, "operator 'is'");
        TypeMatcher.EnsureSingletonTypeTestInput(left.Count, "operator 'is'");

        // Empty input yields empty, NOT false. The specs disagree here and the disagreement is real:
        // FHIRPath N1 (2.0.0), which every published FHIR version normatively references, ends the
        // paragraph "In all other cases this operator returns the empty collection", while the 3.0.0
        // build changed that sentence to "returns false". Empty is deliberate - it matches N1 and both
        // reference engines. Do not "fix" it to false without also deciding to target 3.0.0.
        if (left.Count == 0)
            return [];

        return FunctionHelpers.ReturnBoolean(
            TypeMatcher.IsTypeMatch(left[0], typeName, TypeMatchMode.TypeTest));
    }

    private IEnumerable<IElement> EvaluateTypeAs(List<IElement> left, Expression typeExpr, EvaluationContext context)
    {
        var typeName = TypeMatcher.ExtractTypeName(typeExpr);
        if (string.IsNullOrEmpty(typeName))
            return [];

        // Whether the identifier names a type does not depend on the data, so this is checked before the
        // cardinality guard below - otherwise 'as' would quietly accept a nonsense type on empty input.
        TypeMatcher.EnsureTypeIdentifierResolves(typeName, context.Schema, "operator 'as'");

        TypeMatcher.EnsureSingletonInput(left.Count, context.Schema, "operator 'as'");

        // Reached with more than one item only below R5, where EnsureSingletonInput does not throw.
        // Filtering element-wise rather than returning empty is what makes the operator agree with the
        // as() function, with Firely on every version, and with what HL7 meant by 'as' in the R4
        // SearchParameter expressions - which they rewrote to ofType() in R5, the version where the
        // singleton rule starts being enforced here. Returning empty instead made
        // Observation.component.value as Quantity yield nothing for a blood pressure, so
        // combo-value-quantity and component-value-quantity silently indexed no values at all.
        //
        // Calling the same helper CollectionFunctions.As uses, rather than reimplementing its body, is
        // what stops the operator and the function drifting apart again - which is the defect this line
        // exists to fix. FilterByType is subclass-aware over complex types, so this now agrees with
        // 'is' on an Age: both see a Quantity. It stays exact over primitives; see TypeMatcher.
        return TypeMatcher.FilterByType(left, typeName);
    }

    private bool? EvaluateMembership(List<IElement> left, List<IElement> right, bool isIn)
    {
        var singleItem = isIn ? left : right;
        var collection = isIn ? right : left;

        if (singleItem.Count == 0)
            return null;

        if (singleItem.Count != 1)
        {
            // 'in' explicitly errors on a non-singleton left operand (official test testIn5:
            // ('a' | 'c' | 'd') in 'b'). 'contains' shares this helper but its singleton is the right
            // operand and the spec states no such rule for it, so it keeps returning empty.
            if (isIn)
            {
                throw new FhirPathEvaluationException(
                    $"The left operand of 'in' must be a single item, but was a collection of {singleItem.Count} items.");
            }

            return null;
        }

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
            // Try to extract quantities from elements (handles FHIR Quantity complex types)
            var leftQty = TryExtractQuantity(left[0]);
            var rightQty = TryExtractQuantity(right[0]);

            if (leftQty != null && rightQty != null)
            {
                var isEquiv = AreEquivalent(leftQty, rightQty);
                return isEquiv == equivalent;
            }

            var isEquivValue = AreEquivalent(left[0].Value, right[0].Value);
            return isEquivValue == equivalent;
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

    /// <summary>
    /// Extracts a Quantity from an IElement, handling both FhirPath Quantity literals
    /// and FHIR Quantity elements (which have value/unit/code children).
    /// </summary>
    private Types.Quantity? TryExtractQuantity(IElement element)
    {
        // If the value is already a Quantity (FhirPath literal), return it directly
        if (element.Value is Types.Quantity qty)
            return qty;

        // If it's a FHIR Quantity element, extract value and unit from children
#pragma warning disable CA1308 // Normalize strings to uppercase - FHIR type names are case-insensitive
        var instanceType = element.InstanceType?.ToLowerInvariant();
#pragma warning restore CA1308 // Normalize strings to uppercase
        if (instanceType == "quantity" || instanceType == "age" || instanceType == "distance" || 
            instanceType == "duration" || instanceType == "count" || instanceType == "simplequantity" ||
            instanceType == "moneyquantity")
        {
            return ExtractQuantityFromFhirElement(element);
        }

        return null;
    }

    /// <summary>
    /// Extracts value and unit from a FHIR Quantity element's children.
    /// </summary>
    private static Types.Quantity? ExtractQuantityFromFhirElement(IElement element)
    {
        decimal? value = null;
        string? unit = null;

        var children = element.Children();
        foreach (var child in children)
        {
            if (child.Name == "value" && child.Value != null)
            {
                if (child.Value is decimal d)
                    value = d;
                else if (child.Value is int i)
                    value = i;
                else if (child.Value is long l)
                    value = l;
                else if (child.Value is double dbl)
                    value = (decimal)dbl;
                else if (child.Value is string s && decimal.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                    value = parsed;
            }
            else if (child.Name == "code" && child.Value is string code)
            {
                // Prefer 'code' over 'unit' as it's the UCUM code
                unit = code;
            }
            else if (child.Name == "unit" && child.Value is string unitVal && unit == null)
            {
                // Fall back to 'unit' if 'code' not present
                unit = unitVal;
            }
        }

        if (value.HasValue)
        {
            return new Types.Quantity(value.Value, unit ?? "1");
        }

        return null;
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

            // For quantities, equivalence (~) uses precision-based comparison per FHIRPath 3.0 spec:
            // "For Quantity values, equivalence compares values with respect to their stated precision."
            // Compare values when rounded to the lesser precision (fewer decimal places).
            int precision1 = GetDecimalPrecision(leftQty.Value);
            int precision2 = GetDecimalPrecision(convertedRight.Value);
            int minPrecision = Math.Min(Math.Min(precision1, precision2), 28); // Clamp to max decimal precision

            decimal rounded1 = Math.Round(leftQty.Value, minPrecision, MidpointRounding.AwayFromZero);
            decimal rounded2 = Math.Round(convertedRight.Value, minPrecision, MidpointRounding.AwayFromZero);
            return rounded1 == rounded2;
        }

        if (WireValue.AsWireString(left) is { } leftStr && WireValue.AsWireString(right) is { } rightStr)
        {
            // Check if these are datetime strings (start with @ or look like dates/times)
            if (IsDateTimeString(leftStr) && IsDateTimeString(rightStr))
            {
                // Normalize @ prefix and millisecond precision for datetime equivalence
                var normalizedLeft = NormalizeMillisecondPrecision(leftStr.StartsWith('@') ? leftStr.Substring(1) : leftStr);
                var normalizedRight = NormalizeMillisecondPrecision(rightStr.StartsWith('@') ? rightStr.Substring(1) : rightStr);

                // Per FHIRPath §6.5: For Date, DateTime, and Time values, comparison is done
                // at the precision of the least precise operand. Trailing components are ignored.
                // Only truncate when both operands are the same category (both Date or both DateTime).
                var leftPrecision = FhirTemporal.GetLiteralPrecision(normalizedLeft);
                var rightPrecision = FhirTemporal.GetLiteralPrecision(normalizedRight);

                // Date type has precision ≤ Day; DateTime/Time has precision ≥ Hour.
                // Date vs DateTime are different types and never equivalent.
                bool leftIsDateOnly = leftPrecision <= FhirTemporalPrecision.Day;
                bool rightIsDateOnly = rightPrecision <= FhirTemporalPrecision.Day;

                if (leftIsDateOnly != rightIsDateOnly)
                {
                    return false;
                }

                if (leftPrecision != FhirTemporalPrecision.Invalid && rightPrecision != FhirTemporalPrecision.Invalid
                    && leftPrecision != rightPrecision)
                {
                    // Treat Millisecond as Second — fractional seconds are part of the second value,
                    // not a separate trailing component per the FHIRPath spec.
                    var effectiveLeft = leftPrecision == FhirTemporalPrecision.Millisecond ? FhirTemporalPrecision.Second : leftPrecision;
                    var effectiveRight = rightPrecision == FhirTemporalPrecision.Millisecond ? FhirTemporalPrecision.Second : rightPrecision;

                    if (effectiveLeft != effectiveRight)
                    {
                        var minPrecision = (FhirTemporalPrecision)Math.Min((int)effectiveLeft, (int)effectiveRight);
                        normalizedLeft = TruncateToDateTimePrecision(normalizedLeft, minPrecision);
                        normalizedRight = TruncateToDateTimePrecision(normalizedRight, minPrecision);
                    }
                }

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
            {
                // For decimal equivalence, round to the precision of the least precise value
                // The precision is determined by the number of decimal places in the operands
                var leftPrecision = GetDecimalPrecision(left);
                var rightPrecision = GetDecimalPrecision(right);
                var minPrecision = Math.Min(leftPrecision, rightPrecision);

                // Round both values to the minimum precision
                leftDec = Math.Round(leftDec, minPrecision, MidpointRounding.AwayFromZero);
                rightDec = Math.Round(rightDec, minPrecision, MidpointRounding.AwayFromZero);

                return leftDec == rightDec;
            }
        }

        return left.Equals(right);
    }

    /// <summary>
    /// Gets the number of decimal places in a numeric value.
    /// For integers, returns 0. For decimals, returns the number of significant decimal places.
    /// For division results that have infinite precision, returns a high number.
    /// </summary>
    private static int GetDecimalPrecision(object value)
    {
        if (value is int or long) return 0;

        if (value is decimal d)
        {
            // Convert to string and count decimal places
            var str = d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var decimalPointIndex = str.IndexOf('.', StringComparison.Ordinal);
            if (decimalPointIndex < 0) return 0;
            return str.Length - decimalPointIndex - 1;
        }

        if (value is double dbl)
        {
            // Double values from division may have many decimal places
            // Use a reasonable maximum precision
            var str = dbl.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);
            var decimalPointIndex = str.IndexOf('.', StringComparison.Ordinal);
            if (decimalPointIndex < 0) return 0;
            return Math.Min(str.Length - decimalPointIndex - 1, 15);
        }

        return 0;
    }

    /// <summary>
    /// Determines if a string value appears to be a FHIRPath date/time value.
    /// </summary>
    private static bool IsDateTimeString(string value)
    {
        return FhirTemporal.IsTemporalLiteral(value);
    }

    private string NormalizeWhitespace(string str)
    {
        return System.Text.RegularExpressions.Regex.Replace(str.Trim(), @"\s+", " ");
    }


    public IEnumerable<IElement> VisitScope(ScopeExpression expression, EvaluationContext context)
    {
#pragma warning disable CA1308 // Normalize strings to uppercase
        var scopeName = expression.ScopeName.ToLowerInvariant();
#pragma warning restore CA1308 // Normalize strings to uppercase

        switch (scopeName)
        {
            case "this":
                return context.GetThis() is IElement thisElement
                    ? [thisElement]
                    : context.Focus;

            case "that":
                return context.Focus;

            case "total":
                // $total is used in aggregate() - retrieve from environment
                var totalValue = context.GetEnvironmentVariable("total");
                if (totalValue is IEnumerable<IElement> totalElements)
                    return totalElements;
                if (totalValue is IElement totalElement)
                    return [totalElement];
                return [];

            case "index":
                // $index is used in select() and where() - retrieve from environment
                var indexValue = context.GetEnvironmentVariable("index");
                if (indexValue is IElement indexElement)
                    return [indexElement];
                if (indexValue is int idx)
                    return [CreateInteger(idx)];
                return [];

            default:
                throw new NotSupportedException($"Scope '${expression.ScopeName}' is not yet implemented");
        }
    }

    /// <summary>
    /// Resolves <c>%name</c>, signalling an error when nothing defines the name.
    /// </summary>
    /// <remarks>
    /// FHIRPath §1.9 makes reading an undefined environment variable an error, and this is the check the
    /// official <c>defineVariable</c> scope cases assert. A bound name whose value is empty is not an error
    /// and still yields an empty collection, which is why this asks
    /// <see cref="EvaluationContext.TryGetEnvironmentVariable"/> rather than testing the value for null.
    /// <see cref="Analysis.FhirPathAnalyzer"/> already reports the same condition statically.
    /// </remarks>
    public IEnumerable<IElement> VisitVariable(VariableRefExpression expression, EvaluationContext context)
    {
        if (!context.TryGetEnvironmentVariable(expression.Name, out var value))
        {
            throw new FhirPathEvaluationException(
                $"Attempting to access an undefined environment variable: {expression.Name}");
        }

        if (value is null)
            return [];

        return value switch
        {
            IElement singleElement => [singleElement],
            IEnumerable<IElement> elementCollection => elementCollection,
            _ => []
        };
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
            // Strip T prefix - it's FHIRPath syntax, not part of the value
            // FHIR time format is HH:mm:ss, not THH:mm:ss
            // This matches Firely SDK and fhirpath.js behavior
            return new PrimitiveElement(dateTimeValue.Substring(1), "time");
        }

        if (dateTimeValue.Contains('T', StringComparison.Ordinal))
        {
            return new PrimitiveElement(dateTimeValue, "dateTime");
        }

        return new PrimitiveElement(dateTimeValue, "date");
    }

    public IEnumerable<IElement> VisitIndexer(IndexerExpression expression, EvaluationContext context)
    {
        var unorderedSource = GetUnorderedNavigationSource(expression.Collection);
        if (unorderedSource != null)
        {
            // Result is undefined per FHIRPath spec. Return empty rather than throw;
            // FhirPathAnalyzer surfaces this as a design-time error.
            return [];
        }

        var collectionElements = EvaluateExpression(context.Focus, expression.Collection, context).ToList();

        // Optimization: Fast path for constant integer indexes
        // Avoids creating IElement wrapper and context allocation for index evaluation
        if (expression.Index is ConstantExpression { Value: int constantIndex })
        {
            if (constantIndex >= 0 && constantIndex < collectionElements.Count)
            {
                return [collectionElements[constantIndex]];
            }

            return [];
        }

        // General case: evaluate index expression dynamically
        var indexResults = EvaluateExpression(context.Focus, expression.Index, context).ToList();

        if (indexResults.Count == 1 && indexResults[0].Value is int index)
        {
            if (index >= 0 && index < collectionElements.Count)
            {
                return [collectionElements[index]];
            }
        }

        return [];
    }

    /// <summary>
    /// Evaluates unary <c>+</c>/<c>-</c>. A non-numeric operand of unary <c>-</c> is an error
    /// (official tests <c>testLiteralIntegerNegative1Invalid</c>, <c>testPrecedence1</c>,
    /// <c>testLiteralDecimalNegative01Invalid</c>, all of which parse as <c>-(&lt;boolean&gt;)</c>).
    /// </summary>
    /// <remarks>
    /// This arm previously returned empty, on the premise that the spec left a non-numeric operand
    /// undefined. It does not: the Unary Operators clause types the operand, and the worked example
    /// <c>-7.combine(3) // ERROR</c> says the same. Firely and HAPI both error. Note the clause is new in
    /// 3.0.0-ballot and marked STU - under 2.0.0 only the worked example backs it - but no engine reads it
    /// the lenient way. The typed switch itself must stay: before it, <c>Convert.ToDecimal(true)</c> made
    /// <c>-true</c> evaluate to <c>-1</c>, which is worse than either empty or an error.
    /// </remarks>
    public IEnumerable<IElement> VisitUnary(UnaryExpression expression, EvaluationContext context)
    {
        var operand = EvaluateExpression(context.Focus, expression.Operand, context).ToList();

        if (expression.Operator != "-" || operand.Count != 1)
            return operand;

        var value = operand[0].Value;
        try
        {
            return value switch
            {
                int i => [CreateInteger(checked(-i))],
                long l when l >= int.MinValue && l <= int.MaxValue => [CreateInteger(checked(-(int)l))],
                long l => [CreateDecimal(-(decimal)l)],
                decimal d => [CreateDecimal(-d)],
                double d => [CreateDecimal(-(decimal)d)],
                float f => [CreateDecimal(-(decimal)f)],
                Types.Quantity q => [FunctionHelpers.CreateQuantity(new Types.Quantity(-q.Value, q.Unit))],
                _ => throw new FhirPathEvaluationException(
                    $"Unary '-' is only defined for Integer, Decimal and Quantity, but the operand was of type '{DescribeOperandType(operand[0])}'.")
            };
        }
        catch (OverflowException)
        {
            return [];
        }
    }


    /// <summary>
    /// Applies FHIRPath <c>=</c> / <c>!=</c> semantics to two evaluated operand collections.
    /// </summary>
    /// <remarks>
    /// Internal rather than private because <see cref="FhirPathDelegateCompiler"/> calls it. The compiled
    /// fast path once carried its own ordinal string comparison, which answered <c>false</c> for
    /// <c>birthDate = @1974-12-25</c> while this method answered <c>true</c>. Sharing the one
    /// implementation is what makes the two evaluation paths incapable of drifting apart again.
    /// </remarks>
    internal bool? CompareEquality(List<IElement> left, List<IElement> right, bool equals)
    {
        // Per FHIRPath official tests: {} = {} and {} != {} both return empty
        // Any comparison involving empty collection returns empty (three-valued logic)
        if (left.Count == 0 || right.Count == 0)
            return null;

        if (left.Count != right.Count)
            return !equals;

        if (left.Count == 1 && right.Count == 1)
        {
            var leftVal = left[0].Value;
            var rightVal = right[0].Value;

#pragma warning disable CA1308 // Normalize strings to uppercase
            var leftType = left[0].InstanceType?.ToLowerInvariant();
            var rightType = right[0].InstanceType?.ToLowerInvariant();
#pragma warning restore CA1308 // Normalize strings to uppercase

            // Handle quantity comparisons (both FhirPath literals and FHIR Quantity elements)
            if (leftVal is Types.Quantity || rightVal is Types.Quantity ||
                IsQuantityType(leftType) || IsQuantityType(rightType))
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

            if (IsTemporalOperand(leftVal, leftType) && IsTemporalOperand(rightVal, rightType))
            {
                return CompareDateTimeEquality(leftVal, rightVal, leftType, rightType, equals);
            }
        }

        for (int i = 0; i < left.Count; i++)
        {
            var isEqual = FunctionHelpers.AreElementsEqual(left[i], right[i]);
            if (isEqual != equals) return false;
        }

        return true;
    }

    private bool? CompareDateTimeEquality(object? leftValue, object? rightValue, string? leftType, string? rightType, bool equals)
    {
        // Prefer the typed FhirTemporal path whenever both operands resolve to a temporal. It now
        // expresses the FHIRPath indeterminacy of a timezone-bearing value compared to a timezone-less
        // one (e.g. @...T15:00:00Z = @...T10:00:00, official testEquality23/testNEquality17): AsTemporal
        // routes string operands through FhirTemporal.TryParse, which records timezone presence from the
        // literal, and FhirTemporal.Compare returns null on a timezone mismatch. This agrees with the
        // string fallback below, so it is safe for all operands, not only when one is already a
        // FhirTemporal.
        if (AsTemporal(leftValue, leftType) is { } leftTemporal
            && AsTemporal(rightValue, rightType) is { } rightTemporal)
        {
            // A time of day and a calendar value are different types, not overlapping intervals, so
            // equality is definitely false where ordering is indeterminate (official testDateNotEqualTime*).
            // FhirTemporal.Compare cannot express that distinction: it returns null for both.
            if ((leftTemporal.Kind == FhirPrimitive.Time) != (rightTemporal.Kind == FhirPrimitive.Time))
            {
                return !equals;
            }

            return FhirTemporal.Compare(leftTemporal, rightTemporal) switch
            {
                null => null,
                0 => equals,
                _ => !equals
            };
        }

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

        var leftPrecision = FhirTemporal.GetLiteralPrecision(leftStr);
        var rightPrecision = FhirTemporal.GetLiteralPrecision(rightStr);

        if (leftPrecision == FhirTemporalPrecision.Invalid || rightPrecision == FhirTemporalPrecision.Invalid)
            return null;

        // Per FHIRPath spec: when comparing dates with different precision, the result is uncertain
        // unless we can definitively prove they are unequal based on the specified components
        if (leftPrecision != rightPrecision)
        {
            // Make left the less precise one for easier comparison
            if (leftPrecision > rightPrecision)
            {
                (leftStr, rightStr) = (rightStr, leftStr);
                (leftPrecision, rightPrecision) = (rightPrecision, leftPrecision);
            }
            
            // Remove timezone info for structural comparison of the date/time components
            var leftNormalized = RemoveTimezoneForComparison(leftStr);
            var rightNormalized = RemoveTimezoneForComparison(rightStr);
            
            // Check if the more precise value starts with the less precise value
            // For example: "2018-03-01T10:30" and "2018-03-01T10:30:00"
            // If they match in all specified components, the result is uncertain (null)
            // If they differ in a component that's specified in both, they're definitely unequal (false)
            if (!rightNormalized.StartsWith(leftNormalized, StringComparison.Ordinal))
            {
                // They differ in a component specified in both - definitely not equal
                return equals ? false : true;
            }
            
            // They match in all specified components up to the less precise value's precision.
            // Now check if the additional precision components in the more precise value are non-zero,
            // but ONLY if both values are of the same general type (both DateTime or both have 'T').
            // For Date vs DateTime comparisons (like @1974-12-25 vs @1974-12-25T12:34:00), 
            // the result is uncertain per FHIRPath spec.
            var additionalPart = rightNormalized.Substring(leftNormalized.Length);
            if (!string.IsNullOrEmpty(additionalPart) && !additionalPart.StartsWith('T'))
            {
                // Additional precision in same type (e.g., seconds vs milliseconds)
                // If non-zero, they're definitely different
                if (HasNonZeroAdditionalPrecision(additionalPart))
                {
                    return equals ? false : true;
                }
            }
            
            // They match in all specified components but have different precision - result is uncertain
            // Per FHIRPath spec, return null for uncertain comparisons
            return null;
        }

        // Same precision - check timezone handling
        var leftHasTz = HasTimezone(leftStr);
        var rightHasTz = HasTimezone(rightStr);

        // Try to parse as DateTimeOffset to handle timezones
        if (TryParseFhirDateTime(leftStr, out var leftDt) &&
            TryParseFhirDateTime(rightStr, out var rightDt))
        {
            // For date/time with at least hour precision
            if (leftPrecision >= FhirTemporalPrecision.Hour)
            {
                // If both have explicit timezones, compare in UTC
                if (leftHasTz && rightHasTz)
                {
                    var result = leftDt.UtcDateTime == rightDt.UtcDateTime;
                    return equals ? result : !result;
                }
                
                // If one has timezone and one doesn't, per FHIRPath spec the result is uncertain
                // because we don't know what timezone to assume for the one without
                if (leftHasTz != rightHasTz)
                {
                    // Per spec: return null for uncertain timezone comparisons
                    return null;
                }
                
                // Both have no timezone - compare the datetime values directly
                var localResult = leftDt.DateTime == rightDt.DateTime;
                return equals ? localResult : !localResult;
            }
        }

        // For dates without time component, or if parsing failed, use string comparison
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

        /// <summary>
        /// Applies FHIRPath ordering semantics to two evaluated operand collections.
        /// </summary>
        /// <remarks>
        /// Internal for the same reason as <see cref="CompareEquality"/>: the compiled path shares it.
        /// The <see cref="Nullable{T}"/> return is load-bearing — partial precision makes some orderings
        /// undecidable (<c>@2012 &gt; @2012-01</c>), and FHIRPath requires that to surface as an empty
        /// collection rather than as <c>false</c>.
        /// </remarks>
        internal bool? CompareOrder(List<IElement> left, List<IElement> right, bool greater, bool orEqual)
        {
            if (left.Count == 0 || right.Count == 0)
                return null;
    
            if (left.Count != 1 || right.Count != 1)
                return null;
    
            var leftValue = left[0].Value;
            var rightValue = right[0].Value;
    
#pragma warning disable CA1308 // Normalize strings to uppercase
            var leftType = left[0].InstanceType?.ToLowerInvariant();
            var rightType = right[0].InstanceType?.ToLowerInvariant();
#pragma warning restore CA1308 // Normalize strings to uppercase

            // Handle quantity comparisons (both FhirPath literals and FHIR Quantity elements)
            if (leftValue is Types.Quantity || rightValue is Types.Quantity ||
                IsQuantityType(leftType) || IsQuantityType(rightType))
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
    
            if (IsTemporalOperand(leftValue, leftType) && IsTemporalOperand(rightValue, rightType))
            {
                return CompareDateTimesWithPrecision(leftValue, rightValue, leftType, rightType, greater, orEqual);
            }

                    if (WireValue.AsWireString(leftValue) is { } leftStr && WireValue.AsWireString(rightValue) is { } rightStr)
                    {
                        // Try to treat as typed dates first if they look like dates
                        // This handles cases where type info is lost or implicit conversion is expected
                        if (IsDateTimeString(leftStr) && IsDateTimeString(rightStr))
                        {
                             // Date comparison - if result is null (uncertain), don't fall through to string comparison
                             return CompareDateTimesWithPrecision(leftValue, rightValue, null, null, greater, orEqual);
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
                int comparison;
                try
                {
                    comparison = leftComparable.CompareTo(rightComparable);
                }
                catch (ArgumentException ex)
                {
                    // CompareTo rejects the pair only when the operands are of genuinely different
                    // primitive types (e.g. decimal vs string). FHIRPath requires comparison operands to be
                    // of the same type and signals an error otherwise - swallowing this into an empty
                    // collection hid a real type error (official testLiteralDecimalLessThanInvalid:
                    // Observation.value.value < 'test'). Undecidable-but-well-typed comparisons, such as
                    // partial-precision dates, are handled above and still return null.
                    throw new FhirPathEvaluationException(
                        $"Cannot compare '{left[0].InstanceType ?? "unknown"}' with '{right[0].InstanceType ?? "unknown"}': " +
                        "comparison operands must be of the same type.", ex);
                }

                return greater
                    ? (orEqual ? comparison >= 0 : comparison > 0)
                    : (orEqual ? comparison <= 0 : comparison < 0);
            }

            return null;
        }
    private static FhirTemporal? AsTemporal(object? value, string? instanceType)
    {
        switch (value)
        {
            case FhirTemporal temporal:
                return temporal;
            case string text:
                return FhirTemporal.TryParse(text, InferTemporalKind(text, instanceType), out var parsed) ? parsed : null;
            case DateTime dateTime:
                return FhirTemporal.TryParse(dateTime.ToString("o"), FhirPrimitive.DateTime, out var fromDateTime) ? fromDateTime : null;
            case DateTimeOffset dateTimeOffset:
                return FhirTemporal.TryParse(dateTimeOffset.ToString("o"), FhirPrimitive.DateTime, out var fromOffset) ? fromOffset : null;
            default:
                return null;
        }
    }

    private static FhirPrimitive InferTemporalKind(string literal, string? instanceType)
    {
        if (string.Equals(instanceType, "date", StringComparison.OrdinalIgnoreCase))
            return FhirPrimitive.Date;
        if (string.Equals(instanceType, "datetime", StringComparison.OrdinalIgnoreCase))
            return FhirPrimitive.DateTime;
        if (string.Equals(instanceType, "instant", StringComparison.OrdinalIgnoreCase))
            return FhirPrimitive.Instant;
        if (string.Equals(instanceType, "time", StringComparison.OrdinalIgnoreCase))
            return FhirPrimitive.Time;

        var wire = literal.Length > 0 && literal[0] == '@' ? literal[1..] : literal;
        if (wire.StartsWith('T') || (wire.Contains(':', StringComparison.Ordinal) && !wire.Contains('-', StringComparison.Ordinal)))
            return FhirPrimitive.Time;

        return wire.Contains('T', StringComparison.Ordinal) ? FhirPrimitive.DateTime : FhirPrimitive.Date;
    }

    private bool? CompareDateTimesWithPrecision(object? leftValue, object? rightValue, string? leftType, string? rightType, bool greater, bool orEqual)
    {
        // Prefer the typed path whenever both operands resolve to a temporal, for the same reason as
        // CompareDateTimeEquality: FhirTemporal.Compare now returns null on timezone-vs-no-timezone, so
        // the typed path agrees with the string fallback and no longer needs to be gated to a FhirTemporal
        // operand.
        if (AsTemporal(leftValue, leftType) is { } leftTemporal
            && AsTemporal(rightValue, rightType) is { } rightTemporal)
        {
            return FhirTemporal.Compare(leftTemporal, rightTemporal) switch
            {
                null => null,
                var comparison => greater
                    ? (orEqual ? comparison >= 0 : comparison > 0)
                    : (orEqual ? comparison <= 0 : comparison < 0)
            };
        }

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

        // Prepend T for time values to normalize for parsing (time values stored as HH:mm:ss)
        if (leftType == "time" && !leftStr.StartsWith("T", StringComparison.Ordinal))
            leftStr = "T" + leftStr;
        if (rightType == "time" && !rightStr.StartsWith("T", StringComparison.Ordinal))
            rightStr = "T" + rightStr;

        // Normalize .0 millisecond suffixes for consistent precision detection
        leftStr = NormalizeMillisecondPrecision(leftStr);
        rightStr = NormalizeMillisecondPrecision(rightStr);

        var leftPrecision = FhirTemporal.GetLiteralPrecision(leftStr);
        var rightPrecision = FhirTemporal.GetLiteralPrecision(rightStr);

        if (leftPrecision == FhirTemporalPrecision.Invalid || rightPrecision == FhirTemporalPrecision.Invalid)
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

        // Special case: identical intervals
        if (leftLower == rightLower && leftUpper == rightUpper)
        {
            // For <= or >=, identical values satisfy the condition
            // For < or >, identical values definitely do NOT satisfy (not less/greater than itself)
            return orEqual;
        }

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

    private static FhirTemporalPrecision MaxPrecision(FhirTemporalPrecision left, FhirTemporalPrecision right)
        => left >= right ? left : right;

    /// <summary>
    /// Every unit FHIRPath accepts as time-valued, in both the calendar-keyword and UCUM-code spellings.
    /// A unit outside this set (<c>'cm'</c>, <c>'kg'</c>, ...) is not a duration at all, which is a
    /// different failure from a duration that is merely too fine for the operand's precision.
    /// </summary>
    private static readonly FrozenSet<string> _timeValuedUnits = new[]
    {
        "a", "year", "years",
        "mo", "month", "months",
        "wk", "week", "weeks",
        "d", "day", "days",
        "h", "hour", "hours",
        "min", "minute", "minutes",
        "s", "second", "seconds",
        "ms", "millisecond", "milliseconds"
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// The UCUM definite-duration units that have no calendar equivalent: <c>'a'</c> is a fixed 365.25 days
    /// and <c>'mo'</c> a fixed twelfth of that, so neither lands where the calendar keywords
    /// <c>year</c>/<c>month</c> do. Every other UCUM time unit ('wk', 'd', 'h', 'min', 's', 'ms') is both
    /// definite and unambiguous, so only these two are rejected.
    /// </summary>
    private static readonly FrozenSet<string> _calendarIncompatibleDurationUnits = new[] { "a", "mo" }
        .ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Enforces the FHIRPath rule that <c>'a'</c> and <c>'mo'</c> signal an error in Date, DateTime and Time
    /// arithmetic (FHIRPath 3.0 "Date/Time Arithmetic": the year row reads "using 'a' will signal an error"
    /// and the month row "using 'mo' will signal an error"; official cases <c>testPlusDate14</c>,
    /// <c>testPlusDate16</c> and <c>testPlusDate17</c>).
    /// </summary>
    private static void ThrowIfCalendarIncompatibleUnit(string instanceType, Types.Quantity quantity)
    {
        if (!IsTemporalInstanceType(instanceType) || !_calendarIncompatibleDurationUnits.Contains(quantity.Unit))
        {
            return;
        }

        var keyword = quantity.Unit == "a" ? "year" : "month";
        throw new FhirPathEvaluationException(
            $"'{quantity}' uses the UCUM definite-duration unit '{quantity.Unit}', which has no calendar " +
            $"equivalent, so it cannot be used in {instanceType} arithmetic; use the calendar keyword " +
            $"'{keyword}' instead.");
    }

    private static FhirTemporalPrecision? GetDateTimeArithmeticUnitPrecision(string instanceType, string unit)
        => (instanceType, unit) switch
        {
            ("date", "year" or "years") => FhirTemporalPrecision.Year,
            ("date", "month" or "months") => FhirTemporalPrecision.Month,
            ("date", "wk" or "week" or "weeks") => FhirTemporalPrecision.Day,
            ("date", "d" or "day" or "days") => FhirTemporalPrecision.Day,
            ("dateTime", "year" or "years") => FhirTemporalPrecision.Year,
            ("dateTime", "month" or "months") => FhirTemporalPrecision.Month,
            ("dateTime", "wk" or "week" or "weeks") => FhirTemporalPrecision.Day,
            ("dateTime", "d" or "day" or "days") => FhirTemporalPrecision.Day,
            ("dateTime", "h" or "hour" or "hours") => FhirTemporalPrecision.Hour,
            ("dateTime", "min" or "minute" or "minutes") => FhirTemporalPrecision.Minute,
            ("dateTime", "s" or "second" or "seconds") => FhirTemporalPrecision.Second,
            ("dateTime", "ms" or "millisecond" or "milliseconds") => FhirTemporalPrecision.Millisecond,
            ("time", "h" or "hour" or "hours") => FhirTemporalPrecision.Hour,
            ("time", "min" or "minute" or "minutes") => FhirTemporalPrecision.Minute,
            ("time", "s" or "second" or "seconds") => FhirTemporalPrecision.Second,
            ("time", "ms" or "millisecond" or "milliseconds") => FhirTemporalPrecision.Millisecond,
            _ => null
        };

    private static string TruncateToDateTimePrecision(string value, FhirTemporalPrecision precision)
    {
        if (string.IsNullOrEmpty(value) || precision == FhirTemporalPrecision.Invalid)
            return value;

        // Extract timezone suffix if present (for DateTime values)
        string tzSuffix = string.Empty;
        string workingValue = value;
        if (value.Contains('T', StringComparison.Ordinal))
        {
            var timePart = value.Substring(value.IndexOf('T', StringComparison.Ordinal) + 1);
            if (timePart.EndsWith("Z", StringComparison.Ordinal))
            {
                tzSuffix = "Z";
            }
            else
            {
                var plusIdx = timePart.LastIndexOf('+');
                var minusIdx = timePart.LastIndexOf('-');
                var tzIdx = Math.Max(plusIdx, minusIdx);
                if (tzIdx >= 0)
                    tzSuffix = timePart.Substring(tzIdx);
            }
        }

        // Split into date and time parts
        var tIndex = value.IndexOf('T', StringComparison.Ordinal);
        var datePart = tIndex >= 0 ? value.Substring(0, tIndex) : value;
        var dateComponents = datePart.Split('-');

        return precision switch
        {
            FhirTemporalPrecision.Year => dateComponents[0],
            FhirTemporalPrecision.Month => dateComponents.Length >= 2
                ? $"{dateComponents[0]}-{dateComponents[1]}"
                : datePart,
            FhirTemporalPrecision.Day => dateComponents.Length >= 3
                ? $"{dateComponents[0]}-{dateComponents[1]}-{dateComponents[2]}"
                : datePart,
            _ when tIndex < 0 => datePart,
            _ => TruncateTimePortion(value, datePart, tzSuffix, precision),
        };
    }

    private static string TruncateTimePortion(string value, string datePart, string tzSuffix, FhirTemporalPrecision precision)
    {
        var tIndex = value.IndexOf('T', StringComparison.Ordinal);
        var rawTime = value.Substring(tIndex + 1);
        // Strip timezone from time for component extraction
        if (!string.IsNullOrEmpty(tzSuffix))
            rawTime = rawTime.Substring(0, rawTime.Length - tzSuffix.Length);

        var timeComponents = rawTime.Split(':');
        var result = precision switch
        {
            FhirTemporalPrecision.Hour => $"{datePart}T{timeComponents[0]}",
            FhirTemporalPrecision.Minute when timeComponents.Length >= 2
                => $"{datePart}T{timeComponents[0]}:{timeComponents[1]}",
            FhirTemporalPrecision.Second when timeComponents.Length >= 3
                => $"{datePart}T{timeComponents[0]}:{timeComponents[1]}:{timeComponents[2].Split('.')[0]}",
            _ => value,
        };
        return string.IsNullOrEmpty(tzSuffix) ? result : result + tzSuffix;
    }

    /// <summary>
    /// Checks whether an operand should be compared as a temporal value.
    /// </summary>
    /// <remarks>
    /// The declared type is authoritative for FHIRPath literals, whose values are plain strings, but it is
    /// not sufficient on its own: an element carrying a <see cref="FhirTemporal"/> is a temporal regardless
    /// of what its instance type says. Testing the value as well keeps the two comparison paths from
    /// disagreeing when a typed value arrives with a type name the gate does not enumerate.
    /// </remarks>
    private static bool IsTemporalOperand(object? value, string? typeName)
    {
        return value is FhirTemporal
            || typeName is "date" or "datetime" or "instant" or "time";
    }

    /// <summary>
    /// Checks if a type name represents a FHIR Quantity type (or subtype).
    /// </summary>
    private static bool IsQuantityType(string? typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return false;
        
        return typeName == "quantity" || typeName == "age" || typeName == "distance" || 
               typeName == "duration" || typeName == "count" || typeName == "simplequantity" ||
               typeName == "moneyquantity";
    }

    /// <summary>
    /// Checks if the additional precision component contains non-zero values.
    /// For example, ".1" has non-zero milliseconds, ":01" has non-zero seconds.
    /// Used to determine if two values with different precision are definitely different.
    /// </summary>
    private static bool HasNonZeroAdditionalPrecision(string additionalPart)
    {
        if (string.IsNullOrEmpty(additionalPart))
            return false;

        // Check for non-zero digits in the additional part
        // Examples: ".1", ".001", ":01", "-01", etc.
        foreach (var c in additionalPart)
        {
            if (c >= '1' && c <= '9')
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasTimezone(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        // Check for 'Z' suffix
        if (value.EndsWith('Z'))
            return true;

        // Check for +HH:MM or -HH:MM timezone offset (after 'T' if present)
        var tIndex = value.IndexOf('T', StringComparison.Ordinal);
        if (tIndex < 0)
            return false; // No time component means no timezone

        var timePart = value.Substring(tIndex);
        var plusIndex = timePart.LastIndexOf('+');
        var minusIndex = timePart.LastIndexOf('-');

        // A + or - after T indicates a timezone offset
        return plusIndex > 0 || minusIndex > 0;
    }
    
    private static string RemoveTimezoneForComparison(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
            
        // Remove 'Z' suffix
        if (value.EndsWith('Z'))
        {
            value = value.Substring(0, value.Length - 1);
        }
            
        var tIndex = value.IndexOf('T', StringComparison.Ordinal);
        if (tIndex < 0)
            return value;
            
        var timePart = value.Substring(tIndex);
        var plusIndex = timePart.LastIndexOf('+');
        var minusIndex = timePart.LastIndexOf('-');
        var tzIndex = Math.Max(plusIndex, minusIndex);
        
        if (tzIndex > 0)
        {
            return value.Substring(0, tIndex + tzIndex);
        }
        
        return value;
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

        private DateTime? GetDateTimeLowerBound(string value, FhirTemporalPrecision precision)
        {
            try
            {
                return precision switch
                {
                    FhirTemporalPrecision.Year => new DateTime(int.Parse(value), 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    FhirTemporalPrecision.Month => DateTime.ParseExact(value + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal).ToUniversalTime(),
                    _ => TryParseFhirDateTime(value, out var dt) ? dt.UtcDateTime : null
                };
            }
            catch
            {
                return null;
            }
        }
    
        private DateTime? GetDateTimeUpperBound(string value, FhirTemporalPrecision precision)
        {
            try
            {
                if (precision == FhirTemporalPrecision.Year)
                    return new DateTime(int.Parse(value), 12, 31, 23, 59, 59, 999, DateTimeKind.Utc);
    
                if (precision == FhirTemporalPrecision.Month)
                    return DateTime.ParseExact(value + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal).ToUniversalTime().AddMonths(1).AddMilliseconds(-1);
    
                if (!TryParseFhirDateTime(value, out var dtOffset))
                    return null;
    
                var dt = dtOffset.UtcDateTime;
    
                return precision switch
                {
                    FhirTemporalPrecision.Day => dt.Date.AddDays(1).AddMilliseconds(-1),
                    FhirTemporalPrecision.Hour => dt.AddHours(1).AddMilliseconds(-1),
                    FhirTemporalPrecision.Minute => dt.AddMinutes(1).AddMilliseconds(-1),
                    FhirTemporalPrecision.Second => dt.AddSeconds(1).AddMilliseconds(-1),
                    FhirTemporalPrecision.Millisecond => dt, // Millisecond precision is exact
                    _ => dt
                };
            }
            catch
            {
                return null;
            }
        }

    private IEnumerable<IElement> EvaluateDateTimeArithmetic(string dateTimeStr, Types.Quantity quantity, bool add, string instanceType)
    {
        ThrowIfCalendarIncompatibleUnit(instanceType, quantity);

        dateTimeStr = dateTimeStr.StartsWith("@", StringComparison.Ordinal) ? dateTimeStr.Substring(1) : dateTimeStr;

        var isTimeOnly = instanceType == "time";
        var parseStr = isTimeOnly && !dateTimeStr.StartsWith("T", StringComparison.Ordinal)
            ? "T" + dateTimeStr
            : dateTimeStr;

        var precision = FhirTemporal.GetLiteralPrecision(parseStr);
        if (precision == FhirTemporalPrecision.Invalid)
            return [];
        if (!TryParseFhirDateTime(parseStr, out var dt))
            return [];

        var unitPrecision = GetDateTimeArithmeticUnitPrecision(instanceType, quantity.Unit);
        if (unitPrecision is null)
        {
            // A unit that is not time-valued at all is a spec error, not an empty result (official
            // testMinus6: @1974-12-25 - 1 'cm'). A time-valued unit that is merely finer than the operand
            // can carry (@1973-12-25 + 1 'h') is a separate question the official suite does not settle,
            // so that case keeps returning empty. Non-temporal operands reach here only because the caller
            // routes any lexical value paired with a Quantity through this method; leave them unchanged.
            if (IsTemporalInstanceType(instanceType) && !_timeValuedUnits.Contains(quantity.Unit))
            {
                throw new FhirPathEvaluationException(
                    $"'{quantity}' is not a time-valued quantity, so it cannot be used in {instanceType} arithmetic.");
            }

            return [];
        }

        var value = (double)quantity.Value * (add ? 1 : -1);
        DateTimeOffset result;

        try
        {
            result = quantity.Unit switch
            {
                "year" or "years" => dt.AddYears((int)Math.Truncate(value)),
                "month" or "months" => dt.AddMonths((int)Math.Truncate(value)),
                "wk" or "week" or "weeks" => dt.AddDays(Math.Truncate(value) * 7),
                "d" or "day" or "days" => dt.AddDays(Math.Truncate(value)),
                "h" or "hour" or "hours" => dt.AddHours(value),
                "min" or "minute" or "minutes" => dt.AddMinutes(value),
                "s" or "second" or "seconds" => dt.AddMilliseconds(value * 1000),
                "ms" or "millisecond" or "milliseconds" => dt.AddMilliseconds(value),
                _ => throw new InvalidOperationException("Unsupported date/time arithmetic unit.")
            };
        }
        catch
        {
            return [];
        }

        var resultPrecision = MaxPrecision(precision, unitPrecision.Value);
        var resultStr = FormatDateTimeWithPrecision(result, resultPrecision, dateTimeStr, isTimeOnly);
        return [new PrimitiveElement(resultStr, instanceType)];
    }

    private string FormatDateTimeWithPrecision(DateTimeOffset dt, FhirTemporalPrecision precision, string originalStr, bool isTimeOnly)
    {
        // Preserve timezone from original string
        var hasTimeZone = originalStr.Contains('+', StringComparison.Ordinal) ||
                          (originalStr.Contains('-', StringComparison.Ordinal) && originalStr.LastIndexOf('-') > 10) ||
                          originalStr.EndsWith("Z", StringComparison.Ordinal);

        var format = precision switch
        {
            FhirTemporalPrecision.Year => "yyyy",
            FhirTemporalPrecision.Month => "yyyy-MM",
            FhirTemporalPrecision.Day => "yyyy-MM-dd",
            FhirTemporalPrecision.Hour => "yyyy-MM-dd'T'HH",
            FhirTemporalPrecision.Minute => "yyyy-MM-dd'T'HH:mm",
            FhirTemporalPrecision.Second => "yyyy-MM-dd'T'HH:mm:ss",
            FhirTemporalPrecision.Millisecond => "yyyy-MM-dd'T'HH:mm:ss.fff",
            _ => "o"
        };

        var result = dt.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
        
        if (isTimeOnly)
        {
            var tIndex = result.IndexOf('T', StringComparison.Ordinal);
            // Strip T prefix - FHIR time format is HH:mm:ss, not THH:mm:ss
            result = result.Substring(tIndex + 1);
        }
        else if (hasTimeZone && precision >= FhirTemporalPrecision.Hour)
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

    public IEnumerable<IElement> VisitInstanceSelector(InstanceSelectorExpression expression, EvaluationContext context)
    {
        // Instance selector: TypeName { element: value, element: value, ... }
        // Creates a new FHIR object of the specified type

        // Per spec: If input collection has multiple items, signal an error
        if (context.Focus.Count > 1)
        {
            throw new FhirPathEvaluationException(
                $"Instance selector requires a single input item or empty collection, but got {context.Focus.Count} items");
        }

        // Per spec: If input collection is empty, result is empty
        if (context.Focus.Count == 0)
        {
            return [];
        }

        var typeName = expression.TypeName;

        // Evaluate element assignments. Per spec: an element whose value evaluates
        // to an empty collection is omitted. Handles {:} / {} as zero elements.
        var elements = new List<InstanceElement>();
        foreach (var assignment in expression.Elements)
        {
            var values = EvaluateExpression(context.Focus, assignment.ValueExpression, context).ToList();
            if (values.Count == 0)
            {
                continue;
            }

            elements.Add(new InstanceElement(assignment.ElementName, values));
        }

        // Construction is delegated to the host's model/type system; there is no engine-local
        // object model to fall back on. A transient stand-in node would look like it worked while
        // producing something that carries no schema metadata and cannot be serialized, so an
        // unconfigured engine fails loudly instead.
        if (context.InstanceCreator is not { } createInstance)
        {
            throw new InvalidOperationException(
                $"Cannot construct '{expression.FullTypeName}': no instance creator is configured on the evaluation context. " +
                $"Set one via {nameof(EvaluationContext.WithInstanceCreator)} (for example, Ignixa.Serialization's SourceNodeInstanceFactory.Create).");
        }

        var created = createInstance(new InstanceCreationRequest(typeName, expression.NamespacePrefix, elements));
        return created is null ? [] : [created];
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
            FhirTemporal temporal => temporal.Kind switch
            {
                FhirPrimitive.Date => "date",
                FhirPrimitive.DateTime => "dateTime",
                FhirPrimitive.Instant => "instant",
                FhirPrimitive.Time => "time",
                _ => "dateTime"
            },
            _ => "string"
        };
    }

    private static bool IsOrderDependentFunction(string functionName) =>
        UnorderedCollectionDetection.IsOrderDependentFunction(functionName);

    private static bool IsPositionalFunction(string functionName) =>
        UnorderedCollectionDetection.IsPositionalFunction(functionName);

    private static string? GetUnorderedNavigationSource(Expression? focus) =>
        UnorderedCollectionDetection.GetUnorderedNavigationSource(focus);

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
        public bool HasPrimitiveValue => true;

        public IReadOnlyList<IElement> Children(string? name = null) => [];

        public T? Meta<T>() where T : class => null;
    }
}
