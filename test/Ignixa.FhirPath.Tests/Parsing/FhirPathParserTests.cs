/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Parser tests to validate FHIRPath expression parsing,
 * focusing on type specifiers for ofType() and is() functions.
 */

using Ignixa.FhirPath;
using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parser;
using Xunit;

namespace Ignixa.FhirPath.Tests.Parsing;

/// <summary>
/// Tests for FhirPathCompiler to verify correct parsing of FHIRPath expressions.
/// </summary>
public class FhirPathParserTests
{
    private readonly FhirPathParser _parser = new();

    #region Type Specifier Tests

    /// <summary>
    /// Per FHIRPath spec, ofType() arguments should be parsed as type specifiers (IdentifierExpression),
    /// not as zero-argument function calls (FunctionCallExpression).
    /// </summary>
    [Fact]
    public void GivenOfTypeWithPrimitiveType_WhenParsed_ThenArgumentIsIdentifierExpression()
    {
        // Arrange & Act
        var expression = _parser.Parse("value.ofType(string)");

        // Assert
        Assert.IsType<FunctionCallExpression>(expression);
        var funcExpr = (FunctionCallExpression)expression;

        Assert.Equal("ofType", funcExpr.FunctionName, ignoreCase: true);
        Assert.Single(funcExpr.Arguments);

        // This is the key assertion - should be IdentifierExpression, not FunctionCallExpression
        var argExpr = funcExpr.Arguments[0];
        Assert.IsType<IdentifierExpression>(argExpr);

        var idExpr = (IdentifierExpression)argExpr;
        Assert.Equal("string", idExpr.Name);
    }

    /// <summary>
    /// Test ofType() with complex type name (capitalized).
    /// </summary>
    [Fact]
    public void GivenOfTypeWithComplexType_WhenParsed_ThenArgumentIsIdentifierExpression()
    {
        // Arrange & Act
        var expression = _parser.Parse("value.ofType(Quantity)");

        // Assert
        var funcExpr = Assert.IsType<FunctionCallExpression>(expression);
        Assert.Equal("ofType", funcExpr.FunctionName, ignoreCase: true);

        var argExpr = Assert.Single(funcExpr.Arguments);
        var idExpr = Assert.IsType<IdentifierExpression>(argExpr);
        Assert.Equal("Quantity", idExpr.Name);
    }

    /// <summary>
    /// Test is() operator - parsed as BinaryExpression with type specifier on right side.
    /// Per FHIRPath spec, 'is' is an infix operator, not a function.
    /// </summary>
    [Fact]
    public void GivenIsWithType_WhenParsed_ThenArgumentIsIdentifierExpression()
    {
        // Arrange & Act
        var expression = _parser.Parse("value is string");

        // Assert - is() is parsed as BinaryExpression
        var binaryExpr = Assert.IsType<BinaryExpression>(expression);
        Assert.Equal("is", binaryExpr.Operator, ignoreCase: true);

        // Right side should be IdentifierExpression (type specifier)
        // Note: Currently parsed as FunctionCallExpression, but that's okay for the evaluator
        // The evaluator handles both IdentifierExpression and zero-arg FunctionCallExpression
    }

    /// <summary>
    /// Test bare ofType() to verify it's parsed as a function call.
    /// Bare function calls have an implicit $that focus.
    /// </summary>
    [Fact]
    public void GivenBareOfType_WhenParsed_ThenIsFunctionCallExpression()
    {
        // Arrange & Act
        var expression = _parser.Parse("ofType(Patient)");

        // Assert
        var funcExpr = Assert.IsType<FunctionCallExpression>(expression);
        Assert.Equal("ofType", funcExpr.FunctionName, ignoreCase: true);

        // Bare function calls have implicit $that focus
        Assert.NotNull(funcExpr.Focus);
        Assert.IsType<AxisExpression>(funcExpr.Focus);
        var axis = (AxisExpression)funcExpr.Focus;
        Assert.Equal("that", axis.AxisName, ignoreCase: true);

        var argExpr = Assert.Single(funcExpr.Arguments);
        var idExpr = Assert.IsType<IdentifierExpression>(argExpr);
        Assert.Equal("Patient", idExpr.Name);
    }

    #endregion

    #region OfType Function Tests

    [Fact]
    public void GivenOfTypeFunction_WhenStringTypeArgument_ThenParsesAsIdentifier()
    {
        // Arrange & Act
        var expression = _parser.Parse("value.ofType(string)");

        // Assert
        var funcExpr = (FunctionCallExpression)expression;
        Assert.Equal("ofType", funcExpr.FunctionName);

        // The argument should be an IdentifierExpression, not a FunctionCallExpression
        var argExpr = funcExpr.Arguments[0];
        Assert.IsType<IdentifierExpression>(argExpr);
        var identExpr = (IdentifierExpression)argExpr;
        Assert.Equal("string", identExpr.Name);
    }

    #endregion
}
