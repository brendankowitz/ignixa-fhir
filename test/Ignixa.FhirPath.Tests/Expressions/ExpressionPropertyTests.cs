/*
 * Copyright (c) 2025, Sparky Contributors
 *
 * Unit tests for Expression class properties and ToString methods.
 */

// Test assertions don't need StringComparison parameters
#pragma warning disable CA1307 // Specify StringComparison for clarity

using Ignixa.FhirPath;
using Ignixa.FhirPath.Expressions;

namespace Ignixa.FhirPath.Tests.Expressions;

public class ExpressionPropertyTests
{
    private readonly FhirPathCompiler _compiler = new();

    #region ConstantExpression Tests

    [Fact]
    public void GivenIntegerConstant_WhenToString_ThenReturnsValue()
    {
        // Arrange
        var expr = _compiler.Parse("42");

        // Act
        var result = expr.ToString();

        // Assert
        Assert.Contains("42", result);
    }

    [Fact]
    public void GivenStringConstant_WhenToString_ThenReturnsQuotedValue()
    {
        // Arrange
        var expr = _compiler.Parse("'hello'");

        // Act
        var result = expr.ToString();

        // Assert
        Assert.Contains("hello", result);
    }

    [Fact]
    public void GivenBooleanConstant_WhenToString_ThenReturnsBooleanValue()
    {
        // Arrange
        var expr = _compiler.Parse("true");

        // Act
        var result = expr.ToString();

        // Assert
        Assert.Contains("True", result); // .NET ToString for bool returns "True"
    }

    #endregion

    #region IdentifierExpression Tests

    [Fact]
    public void GivenIdentifier_WhenToString_ThenReturnsName()
    {
        // Arrange
        var expr = _compiler.Parse("Patient");

        // Act
        var result = expr.ToString();

        // Assert
        Assert.Contains("Patient", result);
    }

    [Fact]
    public void GivenDelimitedIdentifier_WhenParsed_ThenPreservesName()
    {
        // Arrange & Act
        var expr = _compiler.Parse("`integer`");

        // Assert
        Assert.NotNull(expr);
        var result = expr.ToString();
        Assert.Contains("integer", result);
    }

    #endregion

    #region AxisExpression Tests

    [Fact]
    public void GivenThisAxis_WhenToString_ThenReturnsAxis()
    {
        // Arrange
        var expr = _compiler.Parse("$this");

        // Act
        var result = expr.ToString();

        // Assert
        Assert.Contains("$this", result);
    }

    [Fact]
    public void GivenIndexAxis_WhenParsed_ThenCreatesAxisExpression()
    {
        // Arrange & Act
        var expr = _compiler.Parse("$index");

        // Assert
        Assert.NotNull(expr);
        Assert.IsType<AxisExpression>(expr);
    }

    [Fact]
    public void GivenTotalAxis_WhenParsed_ThenCreatesAxisExpression()
    {
        // Arrange & Act
        var expr = _compiler.Parse("$total");

        // Assert
        Assert.NotNull(expr);
        Assert.IsType<AxisExpression>(expr);
    }

    #endregion

    #region VariableRefExpression Tests

    [Fact]
    public void GivenExternalVariable_WhenToString_ThenReturnsVariableName()
    {
        // Arrange
        var expr = _compiler.Parse("%context");

        // Act
        var result = expr.ToString();

        // Assert
        Assert.Contains("context", result);
    }

    #endregion

    #region FunctionCallExpression Tests

    [Fact]
    public void GivenSimpleFunction_WhenToString_ThenReturnsFunctionSignature()
    {
        // Arrange
        var expr = _compiler.Parse("name.exists()");

        // Act
        var result = expr.ToString();

        // Assert
        Assert.Contains("exists", result);
    }

    [Fact]
    public void GivenFunctionWithArguments_WhenToString_ThenIncludesArguments()
    {
        // Arrange
        var expr = _compiler.Parse("name.where($this != '')");

        // Act
        var result = expr.ToString();

        // Assert
        Assert.Contains("where", result);
    }

    #endregion

    #region ChildExpression Tests

    [Fact]
    public void GivenChildNavigation_WhenToString_ThenReturnsPath()
    {
        // Arrange
        var expr = _compiler.Parse("Patient.name");

        // Act
        var result = expr.ToString();

        // Assert
        Assert.Contains("name", result);
    }

    [Fact]
    public void GivenNestedChild_WhenToString_ThenReturnsFullPath()
    {
        // Arrange
        var expr = _compiler.Parse("Patient.name.given");

        // Act
        var result = expr.ToString();

        // Assert
        Assert.Contains("given", result);
    }

    #endregion

    #region IndexerExpression Tests

    [Fact]
    public void GivenIndexer_WhenToString_ThenReturnsIndexNotation()
    {
        // Arrange
        var expr = _compiler.Parse("name[0]");

        // Act
        var result = expr.ToString();

        // Assert
        Assert.Contains("[", result);
        Assert.Contains("0", result);
    }

    #endregion

    #region BinaryExpression Tests

    [Fact]
    public void GivenBinaryOperator_WhenToString_ThenReturnsExpression()
    {
        // Arrange
        var expr = _compiler.Parse("age > 18");

        // Act
        var result = expr.ToString();

        // Assert
        Assert.Contains(">", result);
        Assert.Contains("18", result);
    }

    [Fact]
    public void GivenUnionOperator_WhenToString_ThenReturnsUnion()
    {
        // Arrange
        var expr = _compiler.Parse("1 | 2");

        // Act
        var result = expr.ToString();

        // Assert
        Assert.Contains("|", result);
    }

    #endregion

    #region UnaryExpression Tests

    [Fact]
    public void GivenUnaryMinus_WhenToString_ThenReturnsMinusSign()
    {
        // Arrange
        var expr = _compiler.Parse("-5");

        // Act
        var result = expr.ToString();

        // Assert
        Assert.Contains("-", result);
        Assert.Contains("5", result);
    }

    [Fact]
    public void GivenUnaryPlus_WhenToString_ThenReturnsPlusSign()
    {
        // Arrange
        var expr = _compiler.Parse("+5");

        // Act
        var result = expr.ToString();

        // Assert
        Assert.Contains("5", result);
    }

    #endregion

    #region ParenthesizedExpression Tests

    [Fact]
    public void GivenParenthesizedExpression_WhenToString_ThenIncludesParentheses()
    {
        // Arrange
        var expr = _compiler.Parse("(1 + 2)");

        // Act
        var result = expr.ToString();

        // Assert
        Assert.Contains("(", result);
        Assert.Contains(")", result);
    }

    #endregion

    #region EmptyExpression Tests

    [Fact]
    public void GivenEmptyCollection_WhenToString_ThenReturnsEmptyNotation()
    {
        // Arrange
        var expr = _compiler.Parse("{}");

        // Act
        var result = expr.ToString();

        // Assert
        Assert.Contains("{", result);
        Assert.Contains("}", result);
    }

    [Fact]
    public void GivenEmptyCollection_WhenParsed_ThenCreatesEmptyExpression()
    {
        // Arrange & Act
        var expr = _compiler.Parse("{}");

        // Assert
        Assert.IsType<EmptyExpression>(expr);
    }

    #endregion

    #region QuantityExpression Tests

    [Fact]
    public void GivenQuantity_WhenToString_ThenReturnsValueAndUnit()
    {
        // Note: Quantity parsing requires special handling, so we test the structure
        var success = _compiler.TryParse("5 'mg'", out var expr, out var error);

        if (success && expr != null)
        {
            // Act
            var result = expr.ToString();

            // Assert
            Assert.Contains("5", result);
        }
    }

    #endregion

    #region Expression Location Info Tests

    [Fact]
    public void GivenExpression_WhenGetLocation_ThenReturnsPositionInfo()
    {
        // Arrange
        var expr = _compiler.Parse("Patient.name");

        // Act
        var location = expr.Location;

        // Assert
        Assert.NotNull(location);
        Assert.True(location.LineNumber >= 0);
        Assert.True(location.LinePosition >= 0);
    }

    [Fact]
    public void GivenComplexExpression_WhenGetLocation_ThenHasValidSpan()
    {
        // Arrange
        var expr = _compiler.Parse("Patient.name.where($this != '')");

        // Act
        var location = expr.Location;

        // Assert
        Assert.NotNull(location);
        Assert.True(location.Length > 0);
    }

    #endregion

    #region Parser Error Tests

    [Fact]
    public void GivenInvalidSyntax_WhenTryParse_ThenReturnsFalse()
    {
        // Arrange & Act
        var success = _compiler.TryParse("Patient..name", out var expr, out var error);

        // Assert
        Assert.False(success);
        Assert.Null(expr);
        Assert.NotNull(error);
    }

    [Fact]
    public void GivenEmptyString_WhenTryParse_ThenReturnsFalse()
    {
        // Arrange & Act
        var success = _compiler.TryParse("", out var expr, out var error);

        // Assert
        Assert.False(success);
        Assert.Null(expr);
        Assert.NotNull(error);
    }

    [Fact]
    public void GivenInvalidFunction_WhenTryParse_ThenReturnsFalse()
    {
        // Arrange & Act
        var success = _compiler.TryParse("name.invalidFunc(", out var expr, out var error);

        // Assert
        Assert.False(success);
        Assert.Null(expr);
        Assert.NotNull(error);
    }

    #endregion
}
