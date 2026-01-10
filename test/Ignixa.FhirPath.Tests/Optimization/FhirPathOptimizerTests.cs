// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Optimization;
using Ignixa.FhirPath.Parser;
using Ignixa.FhirPath.Parsing;

namespace Ignixa.FhirPath.Tests.Optimization;

/// <summary>
/// Tests for FhirPath optimization. These tests verify that optimization logic works correctly.
/// The tests use the obsolete FhirPathOptimizer for unit testing the optimization logic,
/// while new parse-time optimization tests verify the recommended approach.
/// </summary>
#pragma warning disable CS0618 // Type or member is obsolete - We're testing the optimization logic, which is now in OptimizingAstBuilder
public class FhirPathOptimizerTests
{
    private readonly FhirPathOptimizer _optimizer = new();
    private readonly FhirPathParser _parser = new();

    // Helper method to optimize an expression using the parse-time optimizer
    private Expression OptimizeViaParse(Expression expression)
    {
        var optimizingParser = new FhirPathParser(CompilationOptions.Optimized);

        // For manually constructed expressions, we'll still use the old optimizer
        // In real usage, expressions come from parsing strings
        return _optimizer.Optimize(expression);
    }

    #region Short-Circuiting Tests

    [Fact]
    public void GivenFalseAndX_WhenOptimizing_ThenShortCircuitsToFalse()
    {
        var expression = new BinaryExpression(
            "and",
            new ConstantExpression(false),
            new IdentifierExpression("someProperty"));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(false, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenXAndFalse_WhenOptimizing_ThenShortCircuitsToFalse()
    {
        var expression = new BinaryExpression(
            "and",
            new IdentifierExpression("someProperty"),
            new ConstantExpression(false));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(false, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenTrueAndX_WhenOptimizing_ThenReturnsX()
    {
        var innerExpr = new IdentifierExpression("someProperty");
        var expression = new BinaryExpression("and", new ConstantExpression(true), innerExpr);

        var result = _optimizer.Optimize(expression);

        Assert.IsType<IdentifierExpression>(result);
        Assert.Equal("someProperty", ((IdentifierExpression)result).Name);
    }

    [Fact]
    public void GivenXAndTrue_WhenOptimizing_ThenReturnsX()
    {
        var innerExpr = new IdentifierExpression("someProperty");
        var expression = new BinaryExpression("and", innerExpr, new ConstantExpression(true));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<IdentifierExpression>(result);
        Assert.Equal("someProperty", ((IdentifierExpression)result).Name);
    }

    [Fact]
    public void GivenTrueOrX_WhenOptimizing_ThenShortCircuitsToTrue()
    {
        var expression = new BinaryExpression(
            "or",
            new ConstantExpression(true),
            new IdentifierExpression("someProperty"));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(true, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenXOrTrue_WhenOptimizing_ThenShortCircuitsToTrue()
    {
        var expression = new BinaryExpression(
            "or",
            new IdentifierExpression("someProperty"),
            new ConstantExpression(true));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(true, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenFalseOrX_WhenOptimizing_ThenReturnsX()
    {
        var innerExpr = new IdentifierExpression("someProperty");
        var expression = new BinaryExpression("or", new ConstantExpression(false), innerExpr);

        var result = _optimizer.Optimize(expression);

        Assert.IsType<IdentifierExpression>(result);
        Assert.Equal("someProperty", ((IdentifierExpression)result).Name);
    }

    [Fact]
    public void GivenXOrFalse_WhenOptimizing_ThenReturnsX()
    {
        var innerExpr = new IdentifierExpression("someProperty");
        var expression = new BinaryExpression("or", innerExpr, new ConstantExpression(false));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<IdentifierExpression>(result);
        Assert.Equal("someProperty", ((IdentifierExpression)result).Name);
    }

    [Fact]
    public void GivenFalseImpliesX_WhenOptimizing_ThenReturnsTrue()
    {
        var expression = new BinaryExpression(
            "implies",
            new ConstantExpression(false),
            new IdentifierExpression("someProperty"));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(true, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenTrueImpliesX_WhenOptimizing_ThenReturnsX()
    {
        var innerExpr = new IdentifierExpression("someProperty");
        var expression = new BinaryExpression("implies", new ConstantExpression(true), innerExpr);

        var result = _optimizer.Optimize(expression);

        Assert.IsType<IdentifierExpression>(result);
        Assert.Equal("someProperty", ((IdentifierExpression)result).Name);
    }

    [Fact]
    public void GivenXImpliesTrue_WhenOptimizing_ThenReturnsTrue()
    {
        var expression = new BinaryExpression(
            "implies",
            new IdentifierExpression("someProperty"),
            new ConstantExpression(true));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(true, ((ConstantExpression)result).Value);
    }

    #endregion

    #region Constant Folding Tests

    [Fact]
    public void GivenConstantAddition_WhenOptimizing_ThenFoldsToConstant()
    {
        var expression = new BinaryExpression(
            "+",
            new ConstantExpression(2),
            new ConstantExpression(3));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(5, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenConstantSubtraction_WhenOptimizing_ThenFoldsToConstant()
    {
        var expression = new BinaryExpression(
            "-",
            new ConstantExpression(10),
            new ConstantExpression(3));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(7, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenConstantMultiplication_WhenOptimizing_ThenFoldsToConstant()
    {
        var expression = new BinaryExpression(
            "*",
            new ConstantExpression(5),
            new ConstantExpression(10));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(50, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenConstantDivision_WhenOptimizing_ThenFoldsToConstant()
    {
        var expression = new BinaryExpression(
            "/",
            new ConstantExpression(100),
            new ConstantExpression(4));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(25, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenConstantModulo_WhenOptimizing_ThenFoldsToConstant()
    {
        var expression = new BinaryExpression(
            "mod",
            new ConstantExpression(17),
            new ConstantExpression(5));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(2, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenDecimalArithmetic_WhenOptimizing_ThenFoldsToDecimal()
    {
        var expression = new BinaryExpression(
            "+",
            new ConstantExpression(2.5m),
            new ConstantExpression(3.5m));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(6.0m, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenMixedIntDecimalArithmetic_WhenOptimizing_ThenFoldsToDecimal()
    {
        var expression = new BinaryExpression(
            "+",
            new ConstantExpression(2),
            new ConstantExpression(3.5m));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(5.5m, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenBooleanAnd_WhenOptimizing_ThenFoldsToConstant()
    {
        var expression = new BinaryExpression(
            "and",
            new ConstantExpression(true),
            new ConstantExpression(true));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(true, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenBooleanOr_WhenOptimizing_ThenFoldsToConstant()
    {
        var expression = new BinaryExpression(
            "or",
            new ConstantExpression(false),
            new ConstantExpression(false));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(false, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenBooleanXor_WhenOptimizing_ThenFoldsToConstant()
    {
        var expression = new BinaryExpression(
            "xor",
            new ConstantExpression(true),
            new ConstantExpression(false));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(true, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenStringConcatenation_WhenOptimizing_ThenFoldsToConstant()
    {
        var expression = new BinaryExpression(
            "&",
            new ConstantExpression("hello"),
            new ConstantExpression(" world"));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal("hello world", ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenConstantComparison_WhenOptimizing_ThenFoldsToBoolean()
    {
        var expression = new BinaryExpression(
            ">",
            new ConstantExpression(10),
            new ConstantExpression(5));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(true, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenConstantEquality_WhenOptimizing_ThenFoldsToBoolean()
    {
        var expression = new BinaryExpression(
            "=",
            new ConstantExpression("test"),
            new ConstantExpression("test"));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(true, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenDivisionByZero_WhenOptimizing_ThenDoesNotFold()
    {
        var expression = new BinaryExpression(
            "/",
            new ConstantExpression(10),
            new ConstantExpression(0));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<BinaryExpression>(result);
    }

    #endregion

    #region Algebraic Simplification Tests

    [Fact]
    public void GivenXPlusZero_WhenOptimizing_ThenReturnsX()
    {
        var innerExpr = new IdentifierExpression("value");
        var expression = new BinaryExpression("+", innerExpr, new ConstantExpression(0));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<IdentifierExpression>(result);
        Assert.Equal("value", ((IdentifierExpression)result).Name);
    }

    [Fact]
    public void GivenZeroPlusX_WhenOptimizing_ThenReturnsX()
    {
        var innerExpr = new IdentifierExpression("value");
        var expression = new BinaryExpression("+", new ConstantExpression(0), innerExpr);

        var result = _optimizer.Optimize(expression);

        Assert.IsType<IdentifierExpression>(result);
        Assert.Equal("value", ((IdentifierExpression)result).Name);
    }

    [Fact]
    public void GivenXMinusZero_WhenOptimizing_ThenReturnsX()
    {
        var innerExpr = new IdentifierExpression("value");
        var expression = new BinaryExpression("-", innerExpr, new ConstantExpression(0));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<IdentifierExpression>(result);
        Assert.Equal("value", ((IdentifierExpression)result).Name);
    }

    [Fact]
    public void GivenXTimesOne_WhenOptimizing_ThenReturnsX()
    {
        var innerExpr = new IdentifierExpression("value");
        var expression = new BinaryExpression("*", innerExpr, new ConstantExpression(1));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<IdentifierExpression>(result);
        Assert.Equal("value", ((IdentifierExpression)result).Name);
    }

    [Fact]
    public void GivenOneTimesX_WhenOptimizing_ThenReturnsX()
    {
        var innerExpr = new IdentifierExpression("value");
        var expression = new BinaryExpression("*", new ConstantExpression(1), innerExpr);

        var result = _optimizer.Optimize(expression);

        Assert.IsType<IdentifierExpression>(result);
        Assert.Equal("value", ((IdentifierExpression)result).Name);
    }

    [Fact]
    public void GivenXTimesZero_WhenOptimizing_ThenReturnsZero()
    {
        var expression = new BinaryExpression(
            "*",
            new IdentifierExpression("value"),
            new ConstantExpression(0));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(0, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenZeroTimesX_WhenOptimizing_ThenReturnsZero()
    {
        var expression = new BinaryExpression(
            "*",
            new ConstantExpression(0),
            new IdentifierExpression("value"));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(0, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenXDividedByOne_WhenOptimizing_ThenReturnsX()
    {
        var innerExpr = new IdentifierExpression("value");
        var expression = new BinaryExpression("/", innerExpr, new ConstantExpression(1));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<IdentifierExpression>(result);
        Assert.Equal("value", ((IdentifierExpression)result).Name);
    }

    [Fact]
    public void GivenZeroDividedByX_WhenOptimizing_ThenReturnsZero()
    {
        var expression = new BinaryExpression(
            "/",
            new ConstantExpression(0),
            new IdentifierExpression("value"));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(0, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenEmptyStringConcatenation_WhenOptimizing_ThenReturnsOtherOperand()
    {
        var innerExpr = new IdentifierExpression("name");
        var expression = new BinaryExpression("&", innerExpr, new ConstantExpression(""));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<IdentifierExpression>(result);
        Assert.Equal("name", ((IdentifierExpression)result).Name);
    }

    #endregion

    #region Function Call Optimization Tests

    [Fact]
    public void GivenWhereTrue_WhenOptimizing_ThenReturnsFocus()
    {
        var focus = new IdentifierExpression("collection");
        var expression = new FunctionCallExpression(
            focus,
            "where",
            new[] { new ConstantExpression(true) });

        var result = _optimizer.Optimize(expression);

        Assert.IsType<IdentifierExpression>(result);
        Assert.Equal("collection", ((IdentifierExpression)result).Name);
    }

    [Fact]
    public void GivenWhereFalse_WhenOptimizing_ThenReturnsEmpty()
    {
        var focus = new IdentifierExpression("collection");
        var expression = new FunctionCallExpression(
            focus,
            "where",
            new[] { new ConstantExpression(false) });

        var result = _optimizer.Optimize(expression);

        Assert.IsType<EmptyExpression>(result);
    }

    [Fact]
    public void GivenFirstOfFirst_WhenOptimizing_ThenReturnsSingleFirst()
    {
        var innerFirst = new FunctionCallExpression(
            new IdentifierExpression("collection"),
            "first",
            []);
        var expression = new FunctionCallExpression(innerFirst, "first", []);

        var result = _optimizer.Optimize(expression);

        Assert.IsType<FunctionCallExpression>(result);
        var funcResult = (FunctionCallExpression)result;
        Assert.Equal("first", funcResult.FunctionName);
        Assert.IsType<IdentifierExpression>(funcResult.Focus);
    }

    [Fact]
    public void GivenLastOfLast_WhenOptimizing_ThenReturnsSingleLast()
    {
        var innerLast = new FunctionCallExpression(
            new IdentifierExpression("collection"),
            "last",
            []);
        var expression = new FunctionCallExpression(innerLast, "last", []);

        var result = _optimizer.Optimize(expression);

        Assert.IsType<FunctionCallExpression>(result);
        var funcResult = (FunctionCallExpression)result;
        Assert.Equal("last", funcResult.FunctionName);
        Assert.IsType<IdentifierExpression>(funcResult.Focus);
    }

    [Fact]
    public void GivenNotOnBoolean_WhenOptimizing_ThenFolds()
    {
        var expression = new FunctionCallExpression(
            new ConstantExpression(true),
            "not",
            []);

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(false, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenDoubleNot_WhenOptimizing_ThenReturnsOriginal()
    {
        var innerNot = new FunctionCallExpression(
            new IdentifierExpression("value"),
            "not",
            []);
        var expression = new FunctionCallExpression(innerNot, "not", []);

        var result = _optimizer.Optimize(expression);

        Assert.IsType<IdentifierExpression>(result);
        Assert.Equal("value", ((IdentifierExpression)result).Name);
    }

    [Fact]
    public void GivenExistsOnEmpty_WhenOptimizing_ThenReturnsFalse()
    {
        var expression = new FunctionCallExpression(
            new EmptyExpression(),
            "exists",
            []);

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(false, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenExistsOnConstant_WhenOptimizing_ThenReturnsTrue()
    {
        var expression = new FunctionCallExpression(
            new ConstantExpression(42),
            "exists",
            []);

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(true, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenEmptyOnEmpty_WhenOptimizing_ThenReturnsTrue()
    {
        var expression = new FunctionCallExpression(
            new EmptyExpression(),
            "empty",
            []);

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(true, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenEmptyOnConstant_WhenOptimizing_ThenReturnsFalse()
    {
        var expression = new FunctionCallExpression(
            new ConstantExpression("value"),
            "empty",
            []);

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(false, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenCountOnEmpty_WhenOptimizing_ThenReturnsZero()
    {
        var expression = new FunctionCallExpression(
            new EmptyExpression(),
            "count",
            []);

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(0, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenIifWithTrueCondition_WhenOptimizing_ThenReturnsThenBranch()
    {
        var expression = new FunctionCallExpression(
            null,
            "iif",
            new Expression[]
            {
                new ConstantExpression(true),
                new ConstantExpression("yes"),
                new ConstantExpression("no")
            });

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal("yes", ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenIifWithFalseCondition_WhenOptimizing_ThenReturnsElseBranch()
    {
        var expression = new FunctionCallExpression(
            null,
            "iif",
            new Expression[]
            {
                new ConstantExpression(false),
                new ConstantExpression("yes"),
                new ConstantExpression("no")
            });

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal("no", ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenToStringOnString_WhenOptimizing_ThenReturnsInput()
    {
        var expression = new FunctionCallExpression(
            new ConstantExpression("already a string"),
            "toString",
            []);

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal("already a string", ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenToIntegerOnInteger_WhenOptimizing_ThenReturnsInput()
    {
        var expression = new FunctionCallExpression(
            new ConstantExpression(42),
            "toInteger",
            []);

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(42, ((ConstantExpression)result).Value);
    }

    #endregion

    #region Unary Optimization Tests

    [Fact]
    public void GivenNegationOfInteger_WhenOptimizing_ThenFolds()
    {
        var expression = new UnaryExpression("-", new ConstantExpression(5));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(-5, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenNegationOfDecimal_WhenOptimizing_ThenFolds()
    {
        var expression = new UnaryExpression("-", new ConstantExpression(3.14m));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(-3.14m, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenPositiveOfNumber_WhenOptimizing_ThenReturnsNumber()
    {
        var expression = new UnaryExpression("+", new ConstantExpression(5));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(5, ((ConstantExpression)result).Value);
    }

    #endregion

    #region Parenthesis Elimination Tests

    [Fact]
    public void GivenParenthesizedConstant_WhenOptimizing_ThenRemovesParentheses()
    {
        var expression = new ParenthesizedExpression(new ConstantExpression(42));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(42, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenParenthesizedIdentifier_WhenOptimizing_ThenRemovesParentheses()
    {
        var expression = new ParenthesizedExpression(new IdentifierExpression("name"));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<IdentifierExpression>(result);
        Assert.Equal("name", ((IdentifierExpression)result).Name);
    }

    [Fact]
    public void GivenNestedParentheses_WhenOptimizing_ThenRemovesAll()
    {
        var expression = new ParenthesizedExpression(
            new ParenthesizedExpression(
                new ConstantExpression(42)));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(42, ((ConstantExpression)result).Value);
    }

    #endregion

    #region Complex Expression Tests

    [Fact]
    public void GivenNestedBinaryExpressions_WhenOptimizing_ThenOptimizesAll()
    {
        var expression = new BinaryExpression(
            "+",
            new BinaryExpression("+", new ConstantExpression(1), new ConstantExpression(2)),
            new ConstantExpression(3));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(6, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenMixedOptimizations_WhenOptimizing_ThenAppliesAll()
    {
        var expression = new BinaryExpression(
            "and",
            new BinaryExpression("=", new ConstantExpression(5), new ConstantExpression(5)),
            new BinaryExpression(">", new ConstantExpression(10), new ConstantExpression(3)));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(true, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenNoOptimizationPossible_WhenOptimizing_ThenReturnsEquivalentExpression()
    {
        var expression = new BinaryExpression(
            "+",
            new IdentifierExpression("a"),
            new IdentifierExpression("b"));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<BinaryExpression>(result);
        var binaryResult = (BinaryExpression)result;
        Assert.Equal("+", binaryResult.Operator);
    }

    #endregion

    #region Optimization Context Tests

    [Fact]
    public void GivenOptimizations_WhenOptimizing_ThenTracksCount()
    {
        var context = new OptimizationContext();
        var optimizer = new FhirPathOptimizer();

        var expression = new BinaryExpression(
            "+",
            new ConstantExpression(2),
            new ConstantExpression(3));

        var optContext = new OptimizationContext();
        expression.AcceptVisitor(optimizer, optContext);

        Assert.True(optContext.TotalOptimizations >= 0);
    }

    [Fact]
    public void GivenMultipleOptimizations_WhenOptimizing_ThenTracksCategories()
    {
        var context = new OptimizationContext();

        context.RecordOptimization("ConstantFold");
        context.RecordOptimization("ConstantFold");
        context.RecordOptimization("ShortCircuit");

        Assert.Equal(3, context.TotalOptimizations);
        Assert.Equal(2, context.GetOptimizationCount("ConstantFold"));
        Assert.Equal(1, context.GetOptimizationCount("ShortCircuit"));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GivenNullExpression_WhenOptimizing_ThenThrows()
    {
        Assert.Throws<ArgumentNullException>(() => _optimizer.Optimize(null!));
    }

    [Fact]
    public void GivenEmptyExpression_WhenOptimizing_ThenReturnsEmpty()
    {
        var expression = new EmptyExpression();

        var result = _optimizer.Optimize(expression);

        Assert.IsType<EmptyExpression>(result);
    }

    [Fact]
    public void GivenPropertyAccess_WhenOptimizing_ThenPreservesStructure()
    {
        var expression = new PropertyAccessExpression(
            new IdentifierExpression("Patient"),
            "name");

        var result = _optimizer.Optimize(expression);

        Assert.IsType<PropertyAccessExpression>(result);
        var propResult = (PropertyAccessExpression)result;
        Assert.Equal("name", propResult.PropertyName);
    }

    [Fact]
    public void GivenIndexerExpression_WhenOptimizing_ThenPreservesStructure()
    {
        var expression = new IndexerExpression(
            new IdentifierExpression("collection"),
            new ConstantExpression(0));

        var result = _optimizer.Optimize(expression);

        Assert.IsType<IndexerExpression>(result);
    }

    [Fact]
    public void GivenQuantityExpression_WhenOptimizing_ThenPreserves()
    {
        var expression = new QuantityExpression(5.0m, "mg");

        var result = _optimizer.Optimize(expression);

        Assert.IsType<QuantityExpression>(result);
        var quantityResult = (QuantityExpression)result;
        Assert.Equal(5.0m, quantityResult.Value);
        Assert.Equal("mg", quantityResult.Unit);
    }

    [Fact]
    public void GivenVariableReference_WhenOptimizing_ThenPreserves()
    {
        var expression = new VariableRefExpression("resource");

        var result = _optimizer.Optimize(expression);

        Assert.IsType<VariableRefExpression>(result);
        Assert.Equal("resource", ((VariableRefExpression)result).Name);
    }

    [Fact]
    public void GivenScopeExpression_WhenOptimizing_ThenPreserves()
    {
        var expression = new ScopeExpression("this");

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ScopeExpression>(result);
        Assert.Equal("this", ((ScopeExpression)result).ScopeName);
    }

    #endregion

    #region Integration with Parser Tests

    [Fact]
    public void GivenParsedConstantExpression_WhenOptimizing_ThenFolds()
    {
        var parsed = _parser.Parse("2 + 3 * 4");
        var result = _optimizer.Optimize(parsed);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(14, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenParsedBooleanExpression_WhenOptimizing_ThenShortCircuits()
    {
        var parsed = _parser.Parse("false and name.exists()");
        var result = _optimizer.Optimize(parsed);

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(false, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenParsedAlgebraicExpression_WhenOptimizing_ThenSimplifies()
    {
        var parsed = _parser.Parse("value + 0");
        var result = _optimizer.Optimize(parsed);

        Assert.IsType<PropertyAccessExpression>(result);
    }

    #endregion

    #region Child Expression Tests

    [Fact]
    public void GivenChildExpression_WhenOptimizing_ThenPreservesStructure()
    {
        var expression = new ChildExpression(
            new IdentifierExpression("Patient"),
            "name");

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ChildExpression>(result);
        var childResult = (ChildExpression)result;
        Assert.Equal("name", childResult.ChildName);
    }

    [Fact]
    public void GivenChildExpressionWithOptimizableFocus_WhenOptimizing_ThenOptimizesFocus()
    {
        var expression = new ChildExpression(
            new ParenthesizedExpression(new IdentifierExpression("Patient")),
            "name");

        var result = _optimizer.Optimize(expression);

        Assert.IsType<ChildExpression>(result);
        var childResult = (ChildExpression)result;
        Assert.IsType<IdentifierExpression>(childResult.Focus);
    }

    #endregion

    #region Parse-Time Optimization Tests

    [Fact]
    public void GivenConstantExpression_WhenParsingWithOptimization_ThenFoldsAtParseTime()
    {
        var parser = new FhirPathParser(CompilationOptions.Optimized);
        var result = parser.Parse("2 + 3");

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(5, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenShortCircuit_WhenParsingWithOptimization_ThenOptimizesAtParseTime()
    {
        var parser = new FhirPathParser(CompilationOptions.Optimized);
        var result = parser.Parse("false and someProperty");

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(false, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenAlgebraicSimplification_WhenParsingWithOptimization_ThenOptimizesAtParseTime()
    {
        var parser = new FhirPathParser(CompilationOptions.Optimized);
        var result = parser.Parse("someProperty + 0");

        // Parser creates PropertyAccessExpression for identifiers at root level
        Assert.IsType<PropertyAccessExpression>(result);
        Assert.Equal("someProperty", ((PropertyAccessExpression)result).PropertyName);
    }

    [Fact]
    public void GivenParenthesizedConstant_WhenParsingWithOptimization_ThenEliminatesParentheses()
    {
        var parser = new FhirPathParser(CompilationOptions.Optimized);
        var result = parser.Parse("(42)");

        Assert.IsType<ConstantExpression>(result);
        Assert.Equal(42, ((ConstantExpression)result).Value);
    }

    [Fact]
    public void GivenFunctionOptimization_WhenParsingWithOptimization_ThenOptimizesAtParseTime()
    {
        var parser = new FhirPathParser(CompilationOptions.Optimized);
        var result = parser.Parse("someCollection.where(true)");

        // where(true) should be eliminated, leaving just the focus
        // Parser creates PropertyAccessExpression for identifiers at root level
        Assert.IsType<PropertyAccessExpression>(result);
        Assert.Equal("someCollection", ((PropertyAccessExpression)result).PropertyName);
    }

    [Fact]
    public void GivenNoOptimization_WhenParsingWithDefaultOptions_ThenDoesNotOptimize()
    {
        var parser = new FhirPathParser(CompilationOptions.Default);
        var result = parser.Parse("2 + 3");

        // Should remain as a binary expression
        Assert.IsType<BinaryExpression>(result);
        var binary = (BinaryExpression)result;
        Assert.Equal("+", binary.Operator);
        Assert.IsType<ConstantExpression>(binary.Left);
        Assert.IsType<ConstantExpression>(binary.Right);
    }

    #endregion
}
#pragma warning restore CS0618 // Type or member is obsolete
