/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Optimizing AST builder that performs compile-time optimizations.
 * Demonstrates the extensibility of the visitor pattern for compilation.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parsing.ParseTree;

namespace Ignixa.FhirPath.Parsing;

/// <summary>
/// AST builder that performs optimizations during compilation.
/// Applies constant folding, short-circuiting, algebraic simplification, and function optimizations at parse-time.
/// </summary>
/// <remarks>
/// <para>
/// <b>Optimization Strategies:</b>
/// </para>
/// <list type="bullet">
///   <item><description>Short-circuiting: Eliminates redundant boolean operations (false and X -> false, true or X -> true)</description></item>
///   <item><description>Constant folding: Evaluates compile-time constants (2 + 3 -> 5)</description></item>
///   <item><description>Algebraic simplification: Simplifies identity operations (X + 0 -> X, X * 1 -> X)</description></item>
///   <item><description>Function optimization: Removes no-op function calls (where(true) -> focus)</description></item>
///   <item><description>Parenthesis elimination: Removes unnecessary parentheses around simple expressions</description></item>
/// </list>
/// <para>
/// <b>Conservative Approach:</b>
/// </para>
/// <para>
/// Optimizations are applied only when 100% certain they preserve semantics.
/// When in doubt, the original expression is returned unchanged.
/// </para>
/// </remarks>
internal class OptimizingAstBuilder : AstBuilder
{
    public override Expression VisitBinary(BinaryParseNode node, AstBuildContext context)
    {
        var left = node.Left.Accept(this, context);
        Expression right;
        var op = node.Operator;

        // For 'is' and 'as' binary operators, the right operand is a type specifier.
        // Convert IdentifierParseNode to ConstantExpression with the type name as a string value.
        // This allows the analyzer to handle type checking correctly.
        if ((op == "is" || op == "as") && node.Right is IdentifierParseNode idNode)
        {
            right = new ConstantExpression(idNode.Name, CreateLocation(idNode.Location));
        }
        else
        {
            right = node.Right.Accept(this, context);
        }

        var location = CreateLocation(node.Location);

        var normalized = op.ToUpperInvariant();

        // 1. Try short-circuiting (boolean logic)
        if (TryShortCircuit(left, right, normalized, out var shortCircuited))
        {
            return shortCircuited;
        }

        // 2. Try constant folding
        if (TryFoldConstants(left, op, right, out var folded))
        {
            return new ConstantExpression(folded!, location);
        }

        // 3. Try algebraic simplification
        if (TryAlgebraicSimplification(left, right, op, location, out var simplified))
        {
            return simplified;
        }

        return new BinaryExpression(op, left, right, location);
    }

    public override Expression VisitUnary(UnaryParseNode node, AstBuildContext context)
    {
        var operand = node.Operand.Accept(this, context);
        var location = CreateLocation(node.Location);

        if (TryFoldUnary(node.Operator, operand, out var folded))
        {
            return new ConstantExpression(folded!, location);
        }

        return new UnaryExpression(node.Operator, operand, location);
    }

    public override Expression VisitParenthesized(ParenthesizedParseNode node, AstBuildContext context)
    {
        var inner = node.InnerExpression.Accept(this, context);

        // Eliminate parentheses around simple expressions
        if (inner is ConstantExpression or EmptyExpression or IdentifierExpression or VariableRefExpression or ScopeExpression)
        {
            return inner;
        }

        var location = CreateLocation(node.Location);
        return new ParenthesizedExpression(inner, location);
    }

    public override Expression VisitFunctionCall(FunctionCallParseNode node, AstBuildContext context)
    {
        var focus = node.Focus?.Accept(this, context);
        var args = node.Arguments.Select(a => a.Accept(this, context)).ToList();
        var location = CreateLocation(node.Location);

        var funcName = node.FunctionName.ToUpperInvariant();

        if (TryOptimizeFunctionCall(focus, funcName, args, out var optimized))
        {
            return optimized;
        }

        return new FunctionCallExpression(focus, node.FunctionName, args, location);
    }

    private static bool TryFoldConstants(Expression left, string op, Expression right, out object? result)
    {
        result = null;

        if (left is not ConstantExpression leftConst || right is not ConstantExpression rightConst)
        {
            return false;
        }

        var normalizedOp = op.ToUpperInvariant();
        return normalizedOp switch
        {
            "+" => TryFoldAddition(leftConst.Value, rightConst.Value, out result),
            "-" => TryFoldSubtraction(leftConst.Value, rightConst.Value, out result),
            "*" => TryFoldMultiplication(leftConst.Value, rightConst.Value, out result),
            "/" => TryFoldDivision(leftConst.Value, rightConst.Value, out result),
            "DIV" => TryFoldIntegerDivision(leftConst.Value, rightConst.Value, out result),
            "MOD" => TryFoldModulo(leftConst.Value, rightConst.Value, out result),
            "&" => TryFoldStringConcat(leftConst.Value, rightConst.Value, out result),
            "=" => TryFoldEquality(leftConst.Value, rightConst.Value, out result),
            "!=" => TryFoldInequality(leftConst.Value, rightConst.Value, out result),
            ">" => TryFoldGreaterThan(leftConst.Value, rightConst.Value, out result),
            ">=" => TryFoldGreaterThanOrEqual(leftConst.Value, rightConst.Value, out result),
            "<" => TryFoldLessThan(leftConst.Value, rightConst.Value, out result),
            "<=" => TryFoldLessThanOrEqual(leftConst.Value, rightConst.Value, out result),
            "AND" => TryFoldAnd(leftConst.Value, rightConst.Value, out result),
            "OR" => TryFoldOr(leftConst.Value, rightConst.Value, out result),
            "XOR" => TryFoldXor(leftConst.Value, rightConst.Value, out result),
            "IMPLIES" => TryFoldImplies(leftConst.Value, rightConst.Value, out result),
            _ => false
        };
    }

    private static bool TryFoldUnary(string op, Expression operand, out object? result)
    {
        result = null;

        if (operand is not ConstantExpression constant)
        {
            return false;
        }

        return op switch
        {
            "-" => TryFoldNegation(constant.Value, out result),
            "+" => TryFoldPositive(constant.Value, out result),
            _ => false
        };
    }

    private static bool TryFoldAddition(object left, object right, out object? result)
    {
        result = (left, right) switch
        {
            (int l, int r) => l + r,
            (decimal l, decimal r) => l + r,
            (int l, decimal r) => l + r,
            (decimal l, int r) => l + r,
            _ => null
        };
        return result is not null;
    }

    private static bool TryFoldSubtraction(object left, object right, out object? result)
    {
        result = (left, right) switch
        {
            (int l, int r) => l - r,
            (decimal l, decimal r) => l - r,
            (int l, decimal r) => l - r,
            (decimal l, int r) => l - r,
            _ => null
        };
        return result is not null;
    }

    private static bool TryFoldMultiplication(object left, object right, out object? result)
    {
        result = (left, right) switch
        {
            (int l, int r) => l * r,
            (decimal l, decimal r) => l * r,
            (int l, decimal r) => l * r,
            (decimal l, int r) => l * r,
            _ => null
        };
        return result is not null;
    }

    private static bool TryFoldDivision(object left, object right, out object? result)
    {
        result = null;

        if (IsZero(right))
        {
            return false;
        }

        result = (left, right) switch
        {
            (int l, int r) => (decimal)l / r,
            (decimal l, decimal r) => l / r,
            (int l, decimal r) => l / r,
            (decimal l, int r) => l / r,
            _ => null
        };
        return result is not null;
    }

    private static bool TryFoldIntegerDivision(object left, object right, out object? result)
    {
        result = null;

        if (IsZero(right))
        {
            return false;
        }

        result = (left, right) switch
        {
            (int l, int r) => l / r,
            (decimal l, decimal r) => (int)(l / r),
            (int l, decimal r) => (int)(l / r),
            (decimal l, int r) => (int)(l / r),
            _ => null
        };
        return result is not null;
    }

    private static bool TryFoldModulo(object left, object right, out object? result)
    {
        result = null;

        if (IsZero(right))
        {
            return false;
        }

        result = (left, right) switch
        {
            (int l, int r) => l % r,
            (decimal l, decimal r) => l % r,
            (int l, decimal r) => l % r,
            (decimal l, int r) => l % r,
            _ => null
        };
        return result is not null;
    }

    private static bool TryFoldStringConcat(object left, object right, out object? result)
    {
        if (left is string l && right is string r)
        {
            result = l + r;
            return true;
        }
        result = null;
        return false;
    }

    private static bool TryFoldEquality(object left, object right, out object? result)
    {
        result = Equals(left, right);
        return true;
    }

    private static bool TryFoldInequality(object left, object right, out object? result)
    {
        result = !Equals(left, right);
        return true;
    }

    private static bool TryFoldAnd(object left, object right, out object? result)
    {
        if (left is bool l && right is bool r)
        {
            result = l && r;
            return true;
        }
        result = null;
        return false;
    }

    private static bool TryFoldOr(object left, object right, out object? result)
    {
        if (left is bool l && right is bool r)
        {
            result = l || r;
            return true;
        }
        result = null;
        return false;
    }

    private static bool TryFoldXor(object left, object right, out object? result)
    {
        if (left is bool l && right is bool r)
        {
            result = l ^ r;
            return true;
        }
        result = null;
        return false;
    }

    private static bool TryFoldImplies(object left, object right, out object? result)
    {
        if (left is bool l && right is bool r)
        {
            result = !l || r;
            return true;
        }
        result = null;
        return false;
    }

    private static bool TryFoldGreaterThan(object left, object right, out object? result)
    {
        result = null;
        if (left is int li && right is int ri)
        {
            result = li > ri;
            return true;
        }
        if ((left is int || left is decimal) && (right is int || right is decimal))
        {
            var ld = Convert.ToDecimal(left);
            var rd = Convert.ToDecimal(right);
            result = ld > rd;
            return true;
        }
        if (left is string ls && right is string rs)
        {
            result = string.Compare(ls, rs, StringComparison.Ordinal) > 0;
            return true;
        }
        return false;
    }

    private static bool TryFoldGreaterThanOrEqual(object left, object right, out object? result)
    {
        result = null;
        if (left is int li && right is int ri)
        {
            result = li >= ri;
            return true;
        }
        if ((left is int || left is decimal) && (right is int || right is decimal))
        {
            var ld = Convert.ToDecimal(left);
            var rd = Convert.ToDecimal(right);
            result = ld >= rd;
            return true;
        }
        if (left is string ls && right is string rs)
        {
            result = string.Compare(ls, rs, StringComparison.Ordinal) >= 0;
            return true;
        }
        return false;
    }

    private static bool TryFoldLessThan(object left, object right, out object? result)
    {
        result = null;
        if (left is int li && right is int ri)
        {
            result = li < ri;
            return true;
        }
        if ((left is int || left is decimal) && (right is int || right is decimal))
        {
            var ld = Convert.ToDecimal(left);
            var rd = Convert.ToDecimal(right);
            result = ld < rd;
            return true;
        }
        if (left is string ls && right is string rs)
        {
            result = string.Compare(ls, rs, StringComparison.Ordinal) < 0;
            return true;
        }
        return false;
    }

    private static bool TryFoldLessThanOrEqual(object left, object right, out object? result)
    {
        result = null;
        if (left is int li && right is int ri)
        {
            result = li <= ri;
            return true;
        }
        if ((left is int || left is decimal) && (right is int || right is decimal))
        {
            var ld = Convert.ToDecimal(left);
            var rd = Convert.ToDecimal(right);
            result = ld <= rd;
            return true;
        }
        if (left is string ls && right is string rs)
        {
            result = string.Compare(ls, rs, StringComparison.Ordinal) <= 0;
            return true;
        }
        return false;
    }

    private static bool TryFoldNegation(object value, out object? result)
    {
        result = value switch
        {
            int i => -i,
            decimal d => -d,
            _ => null
        };
        return result is not null;
    }

    private static bool TryFoldPositive(object value, out object? result)
    {
        if (value is int or decimal)
        {
            result = value;
            return true;
        }
        result = null;
        return false;
    }

    private static bool IsZero(object value) => value switch
    {
        int i => i == 0,
        decimal d => d == 0m,
        _ => false
    };

    private static bool IsOne(object value) => value switch
    {
        int i => i == 1,
        decimal d => d == 1m,
        _ => false
    };

    private static bool IsEmptyString(object value) =>
        value is string s && s.Length == 0;

    // Short-circuiting logic for boolean operators
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

    // Algebraic simplification (X + 0 = X, X * 1 = X, etc.)
    private static bool TryAlgebraicSimplification(Expression left, Expression right, string op, ISourcePositionInfo? location, out Expression result)
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
                    result = new ConstantExpression(0, location);
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
                    result = new ConstantExpression(0, location);
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

    private static bool IsZero(Expression expr) =>
        expr is ConstantExpression { Value: int i } && i == 0 ||
        expr is ConstantExpression { Value: decimal d } && d == 0m;

    private static bool IsOne(Expression expr) =>
        expr is ConstantExpression { Value: int i } && i == 1 ||
        expr is ConstantExpression { Value: decimal d } && d == 1m;

    private static bool IsEmptyString(Expression expr) =>
        expr is ConstantExpression { Value: string s } && s.Length == 0;

    // Function call optimizations
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
}
