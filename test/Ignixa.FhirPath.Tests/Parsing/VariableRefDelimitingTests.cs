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
/// <see cref="VariableRefExpression.IsDelimited"/> is what stops Ignixa's lexical allowance for <c>-</c>
/// in a bare <c>%name</c> from becoming a resolution rule the specification does not have. The analyzer,
/// evaluator and SQL-on-FHIR validator each pin that behaviour, but all of them reach the flag through
/// <c>FhirPathParser</c> - so none covered the second place it is computed.
/// <see cref="FhirPathGrammar"/> builds an AST directly, duplicating
/// <c>FhirPathParseTreeGrammar</c>'s external-constant rule with no shared code, and nothing in the repo
/// calls <see cref="FhirPathGrammar.Expression"/>: inverting its backtick test broke no test at all. It
/// is public surface on a shipped assembly, so it is covered rather than deleted - the same reasoning as
/// <c>TemporalLiteralClassificationTests.GivenTheDirectGrammar_WhenParsingATemporalLiteral_ThenItAgreesWithTheParseTreeGrammar</c>.
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
