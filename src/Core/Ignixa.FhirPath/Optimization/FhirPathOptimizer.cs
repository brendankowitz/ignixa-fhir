// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Visitors;

namespace Ignixa.FhirPath.Optimization;

/// <summary>
/// Expression optimizer that transforms FhirPath expressions into more efficient forms.
/// </summary>
/// <remarks>
/// <para>
/// <b>OBSOLETE:</b> Use parse-time optimization instead for better performance.
/// Instead of optimizing at evaluation-time, use <see cref="Parsing.FhirPathParser"/> with
/// <see cref="Parsing.CompilationOptions.Optimize"/> set to true.
/// </para>
/// <para>
/// <b>Migration:</b>
/// </para>
/// <code>
/// // OLD (evaluation-time optimization):
/// var optimizer = new FhirPathOptimizer();
/// var optimized = optimizer.Optimize(expression);
///
/// // NEW (parse-time optimization):
/// var parser = new FhirPathParser(CompilationOptions.Optimized);
/// var optimized = parser.Parse(expressionString);
/// </code>
/// <para>
/// <b>Why Parse-Time is Better:</b>
/// </para>
/// <list type="bullet">
///   <item><description>Optimize once at parse-time, not repeatedly at evaluation-time</description></item>
///   <item><description>Better caching - optimized AST can be reused across evaluations</description></item>
///   <item><description>Consistent with compiler architecture (optimization is a compilation phase)</description></item>
/// </list>
/// <para>
/// <b>Optimization Strategies:</b>
/// </para>
/// <list type="bullet">
///   <item><description>Short-circuiting: Eliminates redundant boolean operations (false and X -> false, true or X -> true)</description></item>
///   <item><description>Constant folding: Evaluates compile-time constants (2 + 3 -> 5)</description></item>
///   <item><description>Algebraic simplification: Simplifies identity operations (X + 0 -> X, X * 1 -> X)</description></item>
///   <item><description>Function optimization: Removes no-op function calls (where(true) -> focus)</description></item>
/// </list>
/// </remarks>
[Obsolete("Use parse-time optimization instead: new FhirPathParser(CompilationOptions.Optimized).Parse(expression). " +
          "Optimizing at parse-time is more efficient than optimizing at evaluation-time. " +
          "This class will be removed in a future version.")]
public class FhirPathOptimizer : DefaultFhirPathExpressionVisitor<OptimizationContext, Expression>
{
    /// <summary>
    /// Optimizes a FhirPath expression, returning an equivalent but potentially more efficient expression.
    /// </summary>
    /// <param name="expression">The expression to optimize</param>
    /// <returns>An optimized expression (may be the same instance if no optimizations applied)</returns>
    public Expression Optimize(Expression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return expression.AcceptVisitor(this, new OptimizationContext());
    }

    public override Expression VisitBinary(BinaryExpression expression, OptimizationContext context)
    {
        var left = expression.Left.AcceptVisitor(this, context);
        var right = expression.Right.AcceptVisitor(this, context);

        var normalized = expression.Operator.ToUpperInvariant();

        if (TryShortCircuit(left, right, normalized, out var shortCircuited))
        {
            context.RecordOptimization("ShortCircuit");
            return shortCircuited;
        }

        if (TryConstantFold(left, right, expression.Operator, out var folded))
        {
            context.RecordOptimization("ConstantFold");
            return folded;
        }

        if (TryAlgebraicSimplification(left, right, expression.Operator, out var simplified))
        {
            context.RecordOptimization("AlgebraicSimplification");
            return simplified;
        }

        if (ReferenceEquals(left, expression.Left) && ReferenceEquals(right, expression.Right))
        {
            return expression;
        }

        return new BinaryExpression(expression.Operator, left, right, expression.Location);
    }

    public override Expression VisitUnary(UnaryExpression expression, OptimizationContext context)
    {
        var operand = expression.Operand.AcceptVisitor(this, context);

        if (TryFoldUnary(operand, expression.Operator, out var folded))
        {
            context.RecordOptimization("UnaryFold");
            return folded;
        }

        if (ReferenceEquals(operand, expression.Operand))
        {
            return expression;
        }

        return new UnaryExpression(expression.Operator, operand, expression.Location);
    }

    public override Expression VisitFunctionCall(FunctionCallExpression expression, OptimizationContext context)
    {
        var focus = expression.Focus?.AcceptVisitor(this, context);
        var args = expression.Arguments.Select(a => a.AcceptVisitor(this, context)).ToList();

        var funcName = expression.FunctionName.ToUpperInvariant();

        if (TryOptimizeFunctionCall(focus, funcName, args, out var optimized))
        {
            context.RecordOptimization("FunctionOptimization");
            return optimized;
        }

        if (ReferenceEquals(focus, expression.Focus) && args.SequenceEqual(expression.Arguments))
        {
            return expression;
        }

        return new FunctionCallExpression(focus, expression.FunctionName, args, expression.Location);
    }

    public override Expression VisitChild(ChildExpression expression, OptimizationContext context)
    {
        var focus = expression.Focus?.AcceptVisitor(this, context);

        if (ReferenceEquals(focus, expression.Focus))
        {
            return expression;
        }

        return new ChildExpression(focus, expression.ChildName, expression.Location);
    }

    public override Expression VisitConstant(ConstantExpression expression, OptimizationContext context)
    {
        return expression;
    }

    public override Expression VisitEmpty(EmptyExpression expression, OptimizationContext context)
    {
        return expression;
    }

    public override Expression VisitIdentifier(IdentifierExpression expression, OptimizationContext context)
    {
        return expression;
    }

    public override Expression VisitVariable(VariableRefExpression expression, OptimizationContext context)
    {
        return expression;
    }

    public override Expression VisitScope(ScopeExpression expression, OptimizationContext context)
    {
        return expression;
    }

    public override Expression VisitQuantity(QuantityExpression expression, OptimizationContext context)
    {
        return expression;
    }

    public override Expression VisitIndexer(IndexerExpression expression, OptimizationContext context)
    {
        var collection = expression.Collection.AcceptVisitor(this, context);
        var index = expression.Index.AcceptVisitor(this, context);

        if (ReferenceEquals(collection, expression.Collection) && ReferenceEquals(index, expression.Index))
        {
            return expression;
        }

        return new IndexerExpression(collection, index, expression.Location);
    }

    public override Expression VisitParenthesized(ParenthesizedExpression expression, OptimizationContext context)
    {
        var inner = expression.InnerExpression.AcceptVisitor(this, context);

        if (inner is ConstantExpression or EmptyExpression or IdentifierExpression or VariableRefExpression or ScopeExpression)
        {
            context.RecordOptimization("ParenthesisElimination");
            return inner;
        }

        if (ReferenceEquals(inner, expression.InnerExpression))
        {
            return expression;
        }

        return new ParenthesizedExpression(inner, expression.Location);
    }

    public override Expression VisitPropertyAccess(PropertyAccessExpression expression, OptimizationContext context)
    {
        var focus = expression.Focus?.AcceptVisitor(this, context);

        if (ReferenceEquals(focus, expression.Focus))
        {
            return expression;
        }

        return new PropertyAccessExpression(focus, expression.PropertyName, expression.Location);
    }

    private static bool TryShortCircuit(Expression left, Expression right, string op, out Expression result)
    {
        result = null!;

        if (op == "AND")
        {
            if (left is ConstantExpression { Value: false })
            {
                result = new ConstantExpression(false);
                return true;
            }
            if (right is ConstantExpression { Value: false })
            {
                result = new ConstantExpression(false);
                return true;
            }
            if (left is ConstantExpression { Value: true })
            {
                result = right;
                return true;
            }
            if (right is ConstantExpression { Value: true })
            {
                result = left;
                return true;
            }
        }

        if (op == "OR")
        {
            if (left is ConstantExpression { Value: true })
            {
                result = new ConstantExpression(true);
                return true;
            }
            if (right is ConstantExpression { Value: true })
            {
                result = new ConstantExpression(true);
                return true;
            }
            if (left is ConstantExpression { Value: false })
            {
                result = right;
                return true;
            }
            if (right is ConstantExpression { Value: false })
            {
                result = left;
                return true;
            }
        }

        if (op == "IMPLIES")
        {
            if (left is ConstantExpression { Value: false })
            {
                result = new ConstantExpression(true);
                return true;
            }
            if (left is ConstantExpression { Value: true })
            {
                result = right;
                return true;
            }
            if (right is ConstantExpression { Value: true })
            {
                result = new ConstantExpression(true);
                return true;
            }
        }

        return false;
    }

    private static bool TryConstantFold(Expression left, Expression right, string op, out Expression result)
    {
        result = null!;

        if (left is not ConstantExpression leftConst || right is not ConstantExpression rightConst)
        {
            return false;
        }

        var foldedValue = EvaluateConstantBinary(leftConst.Value, op, rightConst.Value);
        if (foldedValue is null)
        {
            return false;
        }

        result = new ConstantExpression(foldedValue);
        return true;
    }

    private static object? EvaluateConstantBinary(object? left, string op, object? right)
    {
        if (left is int li && right is int ri)
        {
            return op switch
            {
                "+" => li + ri,
                "-" => li - ri,
                "*" => li * ri,
                "/" when ri != 0 => li / ri,
                "mod" when ri != 0 => li % ri,
                "div" when ri != 0 => li / ri,
                "=" => li == ri,
                "!=" => li != ri,
                ">" => li > ri,
                ">=" => li >= ri,
                "<" => li < ri,
                "<=" => li <= ri,
                _ => null
            };
        }

        if ((left is int || left is decimal) && (right is int || right is decimal))
        {
            var ld = Convert.ToDecimal(left);
            var rd = Convert.ToDecimal(right);
            return op switch
            {
                "+" => ld + rd,
                "-" => ld - rd,
                "*" => ld * rd,
                "/" when rd != 0 => ld / rd,
                "mod" when rd != 0 => ld % rd,
                "=" => ld == rd,
                "!=" => ld != rd,
                ">" => ld > rd,
                ">=" => ld >= rd,
                "<" => ld < rd,
                "<=" => ld <= rd,
                _ => null
            };
        }

        if (left is bool lb && right is bool rb)
        {
            return op.ToUpperInvariant() switch
            {
                "AND" => lb && rb,
                "OR" => lb || rb,
                "XOR" => lb ^ rb,
                "IMPLIES" => !lb || rb,
                "=" => lb == rb,
                "!=" => lb != rb,
                _ => null
            };
        }

        if (op == "&" && left is string ls && right is string rs)
        {
            return ls + rs;
        }

        if (left is string lstr && right is string rstr)
        {
            return op switch
            {
                "=" => string.Equals(lstr, rstr, StringComparison.Ordinal),
                "!=" => !string.Equals(lstr, rstr, StringComparison.Ordinal),
                ">" => string.Compare(lstr, rstr, StringComparison.Ordinal) > 0,
                ">=" => string.Compare(lstr, rstr, StringComparison.Ordinal) >= 0,
                "<" => string.Compare(lstr, rstr, StringComparison.Ordinal) < 0,
                "<=" => string.Compare(lstr, rstr, StringComparison.Ordinal) <= 0,
                _ => null
            };
        }

        return null;
    }

    private static bool TryAlgebraicSimplification(Expression left, Expression right, string op, out Expression result)
    {
        result = null!;

        switch (op)
        {
            case "+":
                if (IsZero(right))
                {
                    result = left;
                    return true;
                }
                if (IsZero(left))
                {
                    result = right;
                    return true;
                }
                break;

            case "-":
                if (IsZero(right))
                {
                    result = left;
                    return true;
                }
                break;

            case "*":
                if (IsOne(right))
                {
                    result = left;
                    return true;
                }
                if (IsOne(left))
                {
                    result = right;
                    return true;
                }
                if (IsZero(right) || IsZero(left))
                {
                    result = new ConstantExpression(0);
                    return true;
                }
                break;

            case "/":
                if (IsOne(right))
                {
                    result = left;
                    return true;
                }
                if (IsZero(left) && !IsZero(right))
                {
                    result = new ConstantExpression(0);
                    return true;
                }
                break;

            case "&":
                if (IsEmptyString(right))
                {
                    result = left;
                    return true;
                }
                if (IsEmptyString(left))
                {
                    result = right;
                    return true;
                }
                break;
        }

        return false;
    }

    private static bool TryFoldUnary(Expression operand, string op, out Expression result)
    {
        result = null!;

        if (operand is not ConstantExpression constExpr)
        {
            return false;
        }

        switch (op)
        {
            case "-" when constExpr.Value is int i:
                result = new ConstantExpression(-i);
                return true;
            case "-" when constExpr.Value is decimal d:
                result = new ConstantExpression(-d);
                return true;
            case "+" when constExpr.Value is int or decimal:
                result = operand;
                return true;
        }

        return false;
    }

    private static bool TryOptimizeFunctionCall(Expression? focus, string funcName, List<Expression> args, out Expression result)
    {
        result = null!;

        switch (funcName)
        {
            case "WHERE":
                if (args.Count == 1)
                {
                    if (args[0] is ConstantExpression { Value: true })
                    {
                        result = focus ?? new EmptyExpression();
                        return true;
                    }
                    if (args[0] is ConstantExpression { Value: false })
                    {
                        result = new EmptyExpression();
                        return true;
                    }
                }
                break;

            case "FIRST":
                if (focus is FunctionCallExpression focusFunc &&
                    focusFunc.FunctionName.Equals("first", StringComparison.OrdinalIgnoreCase))
                {
                    result = focusFunc;
                    return true;
                }
                break;

            case "LAST":
                if (focus is FunctionCallExpression lastFocusFunc &&
                    lastFocusFunc.FunctionName.Equals("last", StringComparison.OrdinalIgnoreCase))
                {
                    result = lastFocusFunc;
                    return true;
                }
                break;

            case "NOT":
                if (args.Count == 0 && focus is ConstantExpression { Value: bool boolVal })
                {
                    result = new ConstantExpression(!boolVal);
                    return true;
                }
                if (args.Count == 0 && focus is FunctionCallExpression notFunc &&
                    notFunc.FunctionName.Equals("not", StringComparison.OrdinalIgnoreCase) &&
                    notFunc.Arguments.Count == 0)
                {
                    result = notFunc.Focus ?? new ConstantExpression(true);
                    return true;
                }
                break;

            case "EXISTS":
                if (focus is EmptyExpression)
                {
                    result = new ConstantExpression(false);
                    return true;
                }
                if (focus is ConstantExpression)
                {
                    result = new ConstantExpression(true);
                    return true;
                }
                break;

            case "EMPTY":
                if (focus is EmptyExpression)
                {
                    result = new ConstantExpression(true);
                    return true;
                }
                if (focus is ConstantExpression)
                {
                    result = new ConstantExpression(false);
                    return true;
                }
                break;

            case "COUNT":
                if (focus is EmptyExpression)
                {
                    result = new ConstantExpression(0);
                    return true;
                }
                break;

            case "IIF":
                if (args.Count >= 2 && args[0] is ConstantExpression { Value: bool condition })
                {
                    result = condition ? args[1] : (args.Count > 2 ? args[2] : new EmptyExpression());
                    return true;
                }
                break;

            case "TOSTRING":
                if (focus is ConstantExpression { Value: string })
                {
                    result = focus;
                    return true;
                }
                break;

            case "TOINTEGER":
                if (focus is ConstantExpression { Value: int })
                {
                    result = focus;
                    return true;
                }
                break;

            case "TODECIMAL":
                if (focus is ConstantExpression { Value: decimal })
                {
                    result = focus;
                    return true;
                }
                if (focus is ConstantExpression { Value: int intVal })
                {
                    result = new ConstantExpression((decimal)intVal);
                    return true;
                }
                break;

            case "TOBOOLEAN":
                if (focus is ConstantExpression { Value: bool })
                {
                    result = focus;
                    return true;
                }
                break;

            case "SINGLE":
                if (focus is ConstantExpression constFocus)
                {
                    result = constFocus;
                    return true;
                }
                break;
        }

        return false;
    }

    private static bool IsZero(Expression expr) =>
        expr is ConstantExpression { Value: int i } && i == 0 ||
        expr is ConstantExpression { Value: decimal d } && d == 0m;

    private static bool IsOne(Expression expr) =>
        expr is ConstantExpression { Value: int i } && i == 1 ||
        expr is ConstantExpression { Value: decimal d } && d == 1m;

    private static bool IsEmptyString(Expression expr) =>
        expr is ConstantExpression { Value: string s } && s.Length == 0;
}

/// <summary>
/// Context passed through the optimizer to track optimization metrics.
/// </summary>
public class OptimizationContext
{
    private readonly Dictionary<string, int> _optimizationCounts = new();

    /// <summary>
    /// Records that an optimization was applied.
    /// </summary>
    public void RecordOptimization(string optimizationType)
    {
        if (_optimizationCounts.TryGetValue(optimizationType, out var count))
        {
            _optimizationCounts[optimizationType] = count + 1;
        }
        else
        {
            _optimizationCounts[optimizationType] = 1;
        }
    }

    /// <summary>
    /// Gets the total number of optimizations applied.
    /// </summary>
    public int TotalOptimizations => _optimizationCounts.Values.Sum();

    /// <summary>
    /// Gets the count for a specific optimization type.
    /// </summary>
    public int GetOptimizationCount(string optimizationType) =>
        _optimizationCounts.TryGetValue(optimizationType, out var count) ? count : 0;

    /// <summary>
    /// Gets all optimization counts.
    /// </summary>
    public IReadOnlyDictionary<string, int> OptimizationCounts => _optimizationCounts;
}
