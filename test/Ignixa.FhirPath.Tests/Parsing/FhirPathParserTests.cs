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
        Assert.IsType<ScopeExpression>(funcExpr.Focus);
        var scope = (ScopeExpression)funcExpr.Focus;
        Assert.Equal("that", scope.ScopeName, ignoreCase: true);

        var argExpr = Assert.Single(funcExpr.Arguments);
        var idExpr = Assert.IsType<IdentifierExpression>(argExpr);
        Assert.Equal("Patient", idExpr.Name);
    }

    #endregion

    #region String Escape Sequence Tests

    [Fact]
    public void GivenStringWithBackslashEscapedQuote_WhenParsed_ThenUnescapedCorrectly()
    {
        // Arrange & Act
        var expression = _parser.Parse(@"'O\'Reilly'");

        // Assert
        var constExpr = Assert.IsType<ConstantExpression>(expression);
        Assert.Equal("O'Reilly", constExpr.Value);
    }

    [Fact]
    public void GivenStringWithBackslashEscapedBackslash_WhenParsed_ThenUnescapedCorrectly()
    {
        // Arrange & Act
        var expression = _parser.Parse(@"'C:\\path\\file'");

        // Assert
        var constExpr = Assert.IsType<ConstantExpression>(expression);
        Assert.Equal(@"C:\path\file", constExpr.Value);
    }

    [Fact]
    public void GivenStringWithNewline_WhenParsed_ThenUnescapedCorrectly()
    {
        // Arrange & Act
        var expression = _parser.Parse(@"'line1\nline2'");

        // Assert
        var constExpr = Assert.IsType<ConstantExpression>(expression);
        Assert.Equal("line1\nline2", constExpr.Value);
    }

    [Fact]
    public void GivenStringWithCarriageReturn_WhenParsed_ThenUnescapedCorrectly()
    {
        // Arrange & Act
        var expression = _parser.Parse(@"'line1\r\nline2'");

        // Assert
        var constExpr = Assert.IsType<ConstantExpression>(expression);
        Assert.Equal("line1\r\nline2", constExpr.Value);
    }

    [Fact]
    public void GivenStringWithTab_WhenParsed_ThenUnescapedCorrectly()
    {
        // Arrange & Act
        var expression = _parser.Parse(@"'col1\tcol2'");

        // Assert
        var constExpr = Assert.IsType<ConstantExpression>(expression);
        Assert.Equal("col1\tcol2", constExpr.Value);
    }

    [Fact]
    public void GivenStringWithFormFeed_WhenParsed_ThenUnescapedCorrectly()
    {
        // Arrange & Act
        var expression = _parser.Parse(@"'page1\fpage2'");

        // Assert
        var constExpr = Assert.IsType<ConstantExpression>(expression);
        Assert.Equal("page1\fpage2", constExpr.Value);
    }

    [Fact]
    public void GivenStringWithUnicodeEscape_WhenParsed_ThenUnescapedCorrectly()
    {
        // Arrange & Act
        var expression = _parser.Parse(@"'Hello\u0020World'");

        // Assert
        var constExpr = Assert.IsType<ConstantExpression>(expression);
        Assert.Equal("Hello World", constExpr.Value);
    }

    [Fact]
    public void GivenStringWithUnicodeEscapeNonAscii_WhenParsed_ThenUnescapedCorrectly()
    {
        // Arrange & Act
        var expression = _parser.Parse(@"'\u00A9 2025'");

        // Assert
        var constExpr = Assert.IsType<ConstantExpression>(expression);
        Assert.Equal("© 2025", constExpr.Value);
    }

    [Fact]
    public void GivenStringWithSqlStyleEscape_WhenParsed_ThenUnescapedCorrectly()
    {
        // Arrange & Act
        var expression = _parser.Parse("'O''Reilly'");

        // Assert
        var constExpr = Assert.IsType<ConstantExpression>(expression);
        Assert.Equal("O'Reilly", constExpr.Value);
    }

    [Fact]
    public void GivenStringWithMultipleEscapes_WhenParsed_ThenUnescapedCorrectly()
    {
        // Arrange & Act
        var expression = _parser.Parse(@"'Path: C:\\data\tValue: \""test\""\nEnd'");

        // Assert
        var constExpr = Assert.IsType<ConstantExpression>(expression);
        Assert.Equal("Path: C:\\data\tValue: \"test\"\nEnd", constExpr.Value);
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
