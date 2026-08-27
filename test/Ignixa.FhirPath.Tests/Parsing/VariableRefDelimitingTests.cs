/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Coverage for the backtick spelling surviving both parsers into VariableRefExpression.IsDelimited.
 */

using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parser;
using Superpower;

namespace Ignixa.FhirPath.Tests.Parsing;

/// <summary>
/// Pins that <c>%`name`</c> and <c>%name</c> reach the AST as different things, in both grammars.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="VariableRefExpression.IsDelimited"/> is what stops Ignixa's lexical allowance for <c>-</c>
/// in a bare <c>%name</c> from becoming a resolution rule the specification does not have: only the
/// delimited spelling expands the <c>vs-</c> / <c>ext-</c> families. The analyzer, the evaluator and the
/// SQL-on-FHIR validator each have their own pin on that behaviour, but every one of them reaches the
/// flag through <c>FhirPathParser</c>, so all four covered one of the two places the flag is computed.
/// </para>
/// <para>
/// <see cref="FhirPathGrammar"/> builds an AST directly and duplicates the external-constant rule of
/// <c>FhirPathParseTreeGrammar</c> with no shared code between them, and nothing inside the repo calls
/// <see cref="FhirPathGrammar.Expression"/> - so inverting its backtick test broke no test at all. It is
/// public surface on a shipped assembly, where the absence of an internal caller says nothing about
/// external ones, so it is covered rather than deleted. Same reasoning as
/// <c>TemporalLiteralClassificationTests.GivenTheDirectGrammar_WhenParsingATemporalLiteral_ThenItAgreesWithTheParseTreeGrammar</c>.
/// </para>
/// </remarks>
public class VariableRefDelimitingTests
{
    private readonly FhirPathParser _parser = new();
    private readonly Tokenizer<FhirPathTokenKind> _tokenizer = FhirPathTokenizer.Create();

    [Theory]
    [InlineData("%`vs-administrative-gender`", "vs-administrative-gender", true)]
    [InlineData("%vs-administrative-gender", "vs-administrative-gender", false)]
    [InlineData("%`ext-patient-birthTime`", "ext-patient-birthTime", true)]
    [InlineData("%ext-patient-birthTime", "ext-patient-birthTime", false)]
    [InlineData("%`context`", "context", true)]
    [InlineData("%context", "context", false)]
    public void GivenTheDirectGrammar_WhenParsingAnExternalConstant_ThenItRecordsHowItWasSpelled(
        string expression,
        string expectedName,
        bool expectedIsDelimited)
    {
        // Act
        var parsed = ParseWithDirectGrammar(expression);

        // Assert
        var variable = parsed.ShouldBeOfType<VariableRefExpression>();
        variable.Name.ShouldBe(
            expectedName,
            $"'{expression}' names {expectedName}; the '%' and any backticks are not part of the name.");
        variable.IsDelimited.ShouldBe(
            expectedIsDelimited,
            $"'{expression}' is {(expectedIsDelimited ? "" : "not ")}written in the backtick-delimited "
            + "form, and only the delimited form expands the vs-/ext- families.");
    }

    [Theory]
    [InlineData("%`vs-administrative-gender`", "vs-administrative-gender", true)]
    [InlineData("%vs-administrative-gender", "vs-administrative-gender", false)]
    [InlineData("%`ext-patient-birthTime`", "ext-patient-birthTime", true)]
    [InlineData("%ext-patient-birthTime", "ext-patient-birthTime", false)]
    [InlineData("%`context`", "context", true)]
    [InlineData("%context", "context", false)]
    public void GivenTheParseTreeGrammar_WhenParsingAnExternalConstant_ThenItAgreesWithTheDirectGrammar(
        string expression,
        string expectedName,
        bool expectedIsDelimited)
    {
        // Act
        var viaParseTree = _parser.Parse(expression);
        var direct = ParseWithDirectGrammar(expression);

        // Assert
        var variable = viaParseTree.ShouldBeOfType<VariableRefExpression>();
        variable.Name.ShouldBe(expectedName);
        variable.IsDelimited.ShouldBe(
            expectedIsDelimited,
            "The parser production takes this path, so a regression here changes resolution for every "
            + "consumer.");
        variable.IsDelimited.ShouldBe(
            direct.ShouldBeOfType<VariableRefExpression>().IsDelimited,
            "The two grammars must not disagree about the spelling; they share no code, so this is the "
            + "only thing keeping them together.");
    }

    private Expression ParseWithDirectGrammar(string expression)
    {
        var tokenized = _tokenizer.TryTokenize(expression);
        tokenized.HasValue.ShouldBeTrue($"Tokenization failed: {tokenized}");

        var parsed = FhirPathGrammar.Expression.AtEnd().TryParse(tokenized.Value);
        parsed.HasValue.ShouldBeTrue($"Parsing failed: {parsed}");

        return parsed.Value;
    }
}
