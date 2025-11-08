/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Tests for case-sensitivity of FHIRPath keywords.
 * Per FHIRPath N1.0 specification, keywords are case-sensitive.
 */

// Test assertions don't need StringComparison parameters
#pragma warning disable CA1307 // Specify StringComparison for clarity

using Ignixa.FhirPath;

namespace Ignixa.FhirPath.Tests.Lexer;

/// <summary>
/// Tests that verify FHIRPath keywords are case-sensitive per the N1.0 specification.
/// Keywords like 'or', 'and', 'is', 'as', 'div', 'mod', 'in', 'contains' must be lowercase.
/// Capitalized versions like 'Or', 'And', 'Organization', 'Invoices' are valid identifiers.
/// </summary>
public class KeywordCaseSensitivityTests
{
    private readonly FhirPathCompiler _compiler = new();

    #region Organization (starts with "Or")

    [Fact]
    public void GivenOrganization_WhenParsed_ThenParsesAsIdentifier()
    {
        // Arrange & Act
        var success = _compiler.TryParse("Organization", out var expr, out var error);

        // Assert
        Assert.True(success, $"Failed to parse 'Organization': {error}");
        Assert.NotNull(expr);
        Assert.Contains("Organization", expr.ToString());
    }

    [Fact]
    public void GivenOrganizationDotAddress_WhenParsed_ThenParsesAsPath()
    {
        // Arrange & Act - This is the exact expression that was failing in search indexing
        var success = _compiler.TryParse("Organization.address", out var expr, out var error);

        // Assert
        Assert.True(success, $"Failed to parse 'Organization.address': {error}");
        Assert.NotNull(expr);
    }

    [Fact]
    public void GivenOrganizationDotName_WhenParsed_ThenParsesCorrectly()
    {
        // Arrange & Act
        var success = _compiler.TryParse("Organization.name", out var expr, out var error);

        // Assert
        Assert.True(success, $"Failed to parse 'Organization.name': {error}");
        Assert.NotNull(expr);
    }

    #endregion

    #region Other Resource Types Starting with Keywords

    [Fact]
    public void GivenInvoice_WhenParsed_ThenParsesAsIdentifier()
    {
        // 'Invoice' starts with 'In' which is a keyword
        var success = _compiler.TryParse("Invoice", out var expr, out var error);

        Assert.True(success, $"Failed to parse 'Invoice': {error}");
        Assert.NotNull(expr);
    }

    [Fact]
    public void GivenInvoiceDotStatus_WhenParsed_ThenParsesAsPath()
    {
        // Arrange & Act
        var success = _compiler.TryParse("Invoice.status", out var expr, out var error);

        // Assert
        Assert.True(success, $"Failed to parse 'Invoice.status': {error}");
        Assert.NotNull(expr);
    }

    #endregion

    #region Lowercase Keywords (should be recognized as keywords)

    [Fact]
    public void GivenLowercaseOr_WhenParsedInExpression_ThenRecognizedAsKeyword()
    {
        // Arrange & Act
        var success = _compiler.TryParse("true or false", out var expr, out var error);

        // Assert
        Assert.True(success, $"Failed to parse 'true or false': {error}");
        Assert.NotNull(expr);
    }

    [Fact]
    public void GivenLowercaseAnd_WhenParsedInExpression_ThenRecognizedAsKeyword()
    {
        // Arrange & Act
        var success = _compiler.TryParse("true and false", out var expr, out var error);

        // Assert
        Assert.True(success, $"Failed to parse 'true and false': {error}");
        Assert.NotNull(expr);
    }

    [Fact]
    public void GivenLowercaseIs_WhenParsedInExpression_ThenRecognizedAsKeyword()
    {
        // Arrange & Act
        var success = _compiler.TryParse("name is string", out var expr, out var error);

        // Assert
        Assert.True(success, $"Failed to parse 'name is string': {error}");
        Assert.NotNull(expr);
    }

    [Fact]
    public void GivenLowercaseAs_WhenParsedInExpression_ThenRecognizedAsKeyword()
    {
        // Arrange & Act
        var success = _compiler.TryParse("value as String", out var expr, out var error);

        // Assert
        Assert.True(success, $"Failed to parse 'value as String': {error}");
        Assert.NotNull(expr);
    }

    [Fact]
    public void GivenLowercaseDiv_WhenParsedInExpression_ThenRecognizedAsKeyword()
    {
        // Arrange & Act
        var success = _compiler.TryParse("10 div 3", out var expr, out var error);

        // Assert
        Assert.True(success, $"Failed to parse '10 div 3': {error}");
        Assert.NotNull(expr);
    }

    [Fact]
    public void GivenLowercaseMod_WhenParsedInExpression_ThenRecognizedAsKeyword()
    {
        // Arrange & Act
        var success = _compiler.TryParse("10 mod 3", out var expr, out var error);

        // Assert
        Assert.True(success, $"Failed to parse '10 mod 3': {error}");
        Assert.NotNull(expr);
    }

    [Fact]
    public void GivenLowercaseIn_WhenParsedInExpression_ThenRecognizedAsKeyword()
    {
        // Arrange & Act
        var success = _compiler.TryParse("'a' in ('a' | 'b')", out var expr, out var error);

        // Assert
        Assert.True(success, $"Failed to parse \"'a' in ('a' | 'b')\": {error}");
        Assert.NotNull(expr);
    }

    [Fact]
    public void GivenLowercaseContains_WhenParsedInExpression_ThenRecognizedAsKeyword()
    {
        // Arrange & Act
        var success = _compiler.TryParse("('a' | 'b') contains 'a'", out var expr, out var error);

        // Assert
        Assert.True(success, $"Failed to parse \"('a' | 'b') contains 'a'\": {error}");
        Assert.NotNull(expr);
    }

    #endregion

    #region Capitalized Keywords (should be identifiers, not keywords)

    [Fact]
    public void GivenCapitalizedOr_WhenUsedAsIdentifier_ThenParsesAsIdentifier()
    {
        // 'Or' should be an identifier, not the 'or' keyword
        var success = _compiler.TryParse("Or", out var expr, out var error);

        Assert.True(success, $"Failed to parse 'Or': {error}");
        Assert.NotNull(expr);
    }

    [Fact]
    public void GivenCapitalizedAnd_WhenUsedAsIdentifier_ThenParsesAsIdentifier()
    {
        // 'And' should be an identifier, not the 'and' keyword
        var success = _compiler.TryParse("And", out var expr, out var error);

        Assert.True(success, $"Failed to parse 'And': {error}");
        Assert.NotNull(expr);
    }

    [Fact]
    public void GivenCapitalizedIs_WhenUsedAsIdentifier_ThenParsesAsIdentifier()
    {
        // 'Is' should be an identifier, not the 'is' keyword
        var success = _compiler.TryParse("Is", out var expr, out var error);

        Assert.True(success, $"Failed to parse 'Is': {error}");
        Assert.NotNull(expr);
    }

    [Fact]
    public void GivenCapitalizedAs_WhenUsedAsIdentifier_ThenParsesAsIdentifier()
    {
        // 'As' should be an identifier, not the 'as' keyword
        var success = _compiler.TryParse("As", out var expr, out var error);

        Assert.True(success, $"Failed to parse 'As': {error}");
        Assert.NotNull(expr);
    }

    [Fact]
    public void GivenCapitalizedDiv_WhenUsedAsIdentifier_ThenParsesAsIdentifier()
    {
        // 'Div' should be an identifier, not the 'div' keyword
        var success = _compiler.TryParse("Div", out var expr, out var error);

        Assert.True(success, $"Failed to parse 'Div': {error}");
        Assert.NotNull(expr);
    }

    [Fact]
    public void GivenCapitalizedMod_WhenUsedAsIdentifier_ThenParsesAsIdentifier()
    {
        // 'Mod' should be an identifier, not the 'mod' keyword
        var success = _compiler.TryParse("Mod", out var expr, out var error);

        Assert.True(success, $"Failed to parse 'Mod': {error}");
        Assert.NotNull(expr);
    }

    [Fact]
    public void GivenCapitalizedIn_WhenUsedAsIdentifier_ThenParsesAsIdentifier()
    {
        // 'In' should be an identifier, not the 'in' keyword
        var success = _compiler.TryParse("In", out var expr, out var error);

        Assert.True(success, $"Failed to parse 'In': {error}");
        Assert.NotNull(expr);
    }

    [Fact]
    public void GivenCapitalizedContains_WhenUsedAsIdentifier_ThenParsesAsIdentifier()
    {
        // 'Contains' should be an identifier, not the 'contains' keyword
        var success = _compiler.TryParse("Contains", out var expr, out var error);

        Assert.True(success, $"Failed to parse 'Contains': {error}");
        Assert.NotNull(expr);
    }

    #endregion

    #region Mixed Case Keywords (should all be identifiers)

    [Fact]
    public void GivenUppercaseOR_WhenUsedAsIdentifier_ThenParsesAsIdentifier()
    {
        // 'OR' should be an identifier, not the 'or' keyword
        var success = _compiler.TryParse("OR", out var expr, out var error);

        Assert.True(success, $"Failed to parse 'OR': {error}");
        Assert.NotNull(expr);
    }

    [Fact]
    public void GivenUppercaseAND_WhenUsedAsIdentifier_ThenParsesAsIdentifier()
    {
        // 'AND' should be an identifier, not the 'and' keyword
        var success = _compiler.TryParse("AND", out var expr, out var error);

        Assert.True(success, $"Failed to parse 'AND': {error}");
        Assert.NotNull(expr);
    }

    [Fact]
    public void GivenMixedCaseXoR_WhenUsedAsIdentifier_ThenParsesAsIdentifier()
    {
        // 'XoR' should be an identifier, not the 'xor' keyword
        var success = _compiler.TryParse("XoR", out var expr, out var error);

        Assert.True(success, $"Failed to parse 'XoR': {error}");
        Assert.NotNull(expr);
    }

    #endregion
}
