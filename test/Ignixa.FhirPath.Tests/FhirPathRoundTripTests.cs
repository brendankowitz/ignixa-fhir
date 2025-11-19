/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for FhirPath round-tripping (parse → ToFhirPath).
 */

using Ignixa.FhirPath;
using Ignixa.FhirPath.Parser;

namespace Ignixa.FhirPath.Tests;

public class FhirPathRoundTripTests
{
    [Fact]
    public void GivenSimpleExpression_WhenRoundTripping_ThenPreservesExactText()
    {
        var compiler = new FhirPathParser(preserveTrivia: true);
        var original = "Patient.name";

        var expr = compiler.Parse(original);
        var roundTripped = expr.ToFhirPath();

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void GivenExpressionWithWhitespace_WhenRoundTripping_ThenPreservesWhitespace()
    {
        var compiler = new FhirPathParser(preserveTrivia: true);
        var original = "Patient  .  name";

        var expr = compiler.Parse(original);
        var roundTripped = expr.ToFhirPath();

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void GivenExpressionWithLineComment_WhenRoundTripping_ThenPreservesComment()
    {
        var compiler = new FhirPathParser(preserveTrivia: true);
        var original = "Patient // patient resource\n.name";

        var expr = compiler.Parse(original);
        var roundTripped = expr.ToFhirPath();

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void GivenExpressionWithBlockComment_WhenRoundTripping_ThenPreservesComment()
    {
        var compiler = new FhirPathParser(preserveTrivia: true);
        var original = "Patient /* comment */ .name";

        var expr = compiler.Parse(original);
        var roundTripped = expr.ToFhirPath();

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void GivenComplexExpressionWithTrivia_WhenRoundTripping_ThenPreservesAllTrivia()
    {
        var compiler = new FhirPathParser(preserveTrivia: true);
        var original = "Patient  // patient\n.name  /* name */\n.given";

        var expr = compiler.Parse(original);
        var roundTripped = expr.ToFhirPath();

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void GivenFunctionCallWithTrivia_WhenRoundTripping_ThenPreservesTrivia()
    {
        var compiler = new FhirPathParser(preserveTrivia: true);
        var original = "name . where ( $this != '' )";

        var expr = compiler.Parse(original);
        var roundTripped = expr.ToFhirPath();

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void GivenExpressionWithoutTrivia_WhenRoundTripping_ThenReconstructsSemantically()
    {
        var compiler = new FhirPathParser(preserveTrivia: false);
        var original = "Patient.name";

        var expr = compiler.Parse(original);
        var roundTripped = expr.ToFhirPath();

        // Without trivia preservation, ToString() is used, which may not be identical
        // but should be semantically equivalent
        Assert.NotEmpty(roundTripped);
        Assert.Contains("Patient", roundTripped, StringComparison.Ordinal);
        Assert.Contains("name", roundTripped, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenBinaryExpression_WhenRoundTripping_ThenReconstructsCorrectly()
    {
        var compiler = new FhirPathParser(preserveTrivia: false);
        var original = "age > 18";

        var expr = compiler.Parse(original);
        var roundTripped = expr.ToFhirPath();

        // Should contain the key elements
        Assert.Contains("age", roundTripped, StringComparison.Ordinal);
        Assert.Contains(">", roundTripped, StringComparison.Ordinal);
        Assert.Contains("18", roundTripped, StringComparison.Ordinal);
    }
}
