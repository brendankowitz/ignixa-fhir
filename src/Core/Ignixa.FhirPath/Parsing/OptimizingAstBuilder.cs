/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Optimizing AST builder that performs compile-time optimizations.
 * Demonstrates the extensibility of the visitor pattern for compilation.
 */

using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parsing.ParseTree;

namespace Ignixa.FhirPath.Parsing;

/// <summary>
/// AST builder that performs optimizations during compilation.
/// Applies constant folding, short-circuiting and function optimizations at parse-time.
/// </summary>
/// <remarks>
/// <para>
/// <b>Optimization Strategies:</b>
/// </para>
/// <list type="bullet">
///   <item><description>Short-circuiting: Folds the rows the left operand alone decides (false and X -> false, true or X -> true)</description></item>
///   <item><description>Constant folding: Evaluates compile-time constants (2 + 3 -> 5)</description></item>
///   <item><description>Function optimization: Removes no-op function calls (where(true) -> focus)</description></item>
///   <item><description>Parenthesis elimination: Removes unnecessary parentheses around simple expressions</description></item>
/// </list>
/// <para>
/// <b>Discarding an operand is a semantic change, not an optimization.</b>
/// </para>
/// <para>
/// Every FHIRPath operand can signal an error, so an operand may only be dropped when it is itself a
/// literal or when the interpreter would not have evaluated it either. A rewrite that returns one
/// operand in place of the whole expression carries the same burden for type and cardinality, because
/// the operand's runtime shape is unknown at parse time. Anything that cannot clear both bars is left
/// as written; <c>OptimizedVersusUnoptimizedDifferentialTests</c> is what holds this to account.
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

        if (TryShortCircuit(left, normalized, out var shortCircuited))
        {
            return shortCircuited;
        }

        if (TryFoldConstants(left, op, right, out var folded))
        {
            return new ConstantExpression(folded!, location);
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

        // A temporal literal reaches the AST as its source text, sigil and all, so folding it here
        // would compare "@2012" to "@2012-01" as ordinal strings. FHIRPath compares them by instant
        // and answers empty when their precisions merely overlap, which no string compare can express.
        if (IsTemporalLiteral(leftConst) || IsTemporalLiteral(rightConst))
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

    /// <summary>
    /// Folds <c>=</c> only for operand shapes whose answer is the one the evaluator would reach.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to be <see cref="object.Equals(object, object)"/> on the boxed literals, which decides
    /// by CLR type where FHIRPath decides by value. Integer and Decimal are one number in FHIRPath -
    /// <c>CompareEquality</c> widens both to <see cref="decimal"/> - so <c>1 = 1.0</c> answered
    /// <see langword="true"/> unoptimized and <see langword="false"/> under
    /// <c>CompilationOptions.Optimize</c>, as did <c>2 = 2.00</c>, <c>-1 = -1.0</c> and every
    /// <c>1L = 1</c>. Same expression, same data, a different answer per compilation option.
    /// </para>
    /// <para>
    /// The rule is the folder's, not the evaluator's, so it is expressed as a refusal: anything that is
    /// not a pair this method can answer identically is left as written and evaluated at runtime.
    /// Declining to fold is always sound; folding on a rule the evaluator does not share never is.
    /// </para>
    /// </remarks>
    private static bool TryFoldEquality(object left, object right, out object? result)
    {
        if (!TryDecideEquality(left, right, out var equal))
        {
            result = null;
            return false;
        }

        result = equal;
        return true;
    }

    private static bool TryFoldInequality(object left, object right, out object? result)
    {
        if (!TryDecideEquality(left, right, out var equal))
        {
            result = null;
            return false;
        }

        result = !equal;
        return true;
    }

    private static bool TryDecideEquality(object left, object right, out bool equal)
    {
        equal = false;

        if (IsNumericLiteral(left) && IsNumericLiteral(right))
        {
            equal = Convert.ToDecimal(left, CultureInfo.InvariantCulture)
                == Convert.ToDecimal(right, CultureInfo.InvariantCulture);
            return true;
        }

        if (left is string leftText && right is string rightText)
        {
            equal = string.Equals(leftText, rightText, StringComparison.Ordinal);
            return true;
        }

        if (left is bool leftFlag && right is bool rightFlag)
        {
            equal = leftFlag == rightFlag;
            return true;
        }

        return false;
    }

    private static bool IsNumericLiteral(object value) => value is int or long or decimal;

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

    /// <summary>
    /// Whether an operand can be dropped from the AST without losing an error it would have signalled.
    /// </summary>
    /// <remarks>
    /// Deliberately the narrowest rule that is obviously true: only a literal, an absent focus, or an
    /// expression already known to be empty. A path step or a function call is left alone even when it
    /// looks harmless, because deciding that requires knowing the runtime shape of the data and the
    /// error behaviour of every function - a purity analysis this optimizer has no business carrying.
    /// </remarks>
    private static bool IsDiscardable(Expression? expression) =>
        expression is null or ConstantExpression or EmptyExpression;

    private static bool IsTemporalLiteral(Expression expression) =>
        expression is TemporalConstantExpression;

    /// <summary>
    /// Folds the boolean operator rows whose answer the left operand alone decides.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are exactly the three rows <c>FhirPathEvaluator.DecideFromLeftOperand</c> short-circuits:
    /// <c>false and *</c>, <c>true or *</c> and <c>false implies *</c>. Folding them discards the right
    /// operand, which is sound only because the interpreter does not evaluate it either.
    /// </para>
    /// <para>
    /// The mirrored rows - <c>* and false</c>, <c>* or true</c>, <c>* implies true</c> - reach the same
    /// value but discard the LEFT operand, which the interpreter always evaluates. Every FHIRPath
    /// operand can signal an error, so folding those turns an expression that throws into one that
    /// quietly answers: <c>(1 | 2).single() and false</c> answered false under Optimize while the
    /// interpreter threw. They are deliberately absent.
    /// </para>
    /// <para>
    /// The identity rewrites - <c>true and X</c> to <c>X</c> and its siblings - are absent for a second
    /// reason. <c>and</c> yields a boolean or empty, whereas X is a collection of unknown type and
    /// cardinality, so the rewrite changes the observable result whenever X is not a boolean singleton:
    /// <c>name.given and true</c> yielded two strings instead of a boolean.
    /// </para>
    /// </remarks>
    private static bool TryShortCircuit(Expression left, string op, out Expression result)
    {
        result = null!;

        if (left is not ConstantExpression { Value: bool leftValue })
        {
            return false;
        }

        bool? decided = (op, leftValue) switch
        {
            ("AND", false) => false,
            ("OR", true) => true,
            ("IMPLIES", false) => true,
            _ => null
        };

        if (decided is null)
        {
            return false;
        }

        result = new ConstantExpression(decided.Value);
        return true;
    }

    /// <summary>
    /// Rewrites that keep one operand and drop the other are unsound in FHIRPath and are not attempted.
    /// </summary>
    /// <remarks>
    /// The removed algebraic identities (<c>X + 0</c> to <c>X</c>, <c>X * 1</c> to <c>X</c>, <c>X &amp; ''</c>
    /// to <c>X</c>) could only ever fire when one operand was NOT a constant, because two constants are
    /// already handled by <see cref="TryFoldConstants"/> above. In exactly that case the surviving
    /// operand's runtime type and cardinality are unknown, so the rewrite is not value-preserving -
    /// <c>gender &amp; ''</c> yielded a code where the operator yields a string, <c>0 / multipleBirthInteger</c>
    /// yielded an integer where division yields a decimal - and the discarded operand may signal an
    /// error the rewrite would swallow, as <c>(1 | 2).single() * 0</c> did.
    /// </remarks>
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
                    if (args[0] is ConstantExpression { Value: false } && IsDiscardable(focus))
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
                // "X.not().not()" is NOT rewritten to "X": not() yields a boolean or empty, so the
                // rewrite is only an identity when X is already a boolean singleton. "name.not().not()"
                // yielded two HumanName elements where the unoptimized parse yielded a boolean.
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
                // A temporal literal also carries a string, but returning it unchanged would leave a
                // date element where toString() has to produce a string one.
                if (focus is ConstantExpression { Value: string } and not TemporalConstantExpression)
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
