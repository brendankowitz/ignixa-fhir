/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Coverage for the backtick spelling surviving the parser into VariableRefExpression.IsDelimited.
 */

using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parser;

namespace Ignixa.FhirPath.Tests.Parsing;

/// <summary>
/// Pins that <c>%`name`</c> and <c>%name</c> reach the AST as different things.
/// </summary>
/// <remarks>
/// <see cref="VariableRefExpression.IsDelimited"/> is what stops Ignixa's lexical allowance for <c>-</c>
/// in a bare <c>%name</c> from becoming a resolution rule the specification does not have.
/// </remarks>
public class VariableRefDelimitingTests
{
    private readonly FhirPathParser _parser = new();

    [Theory]
    [InlineData("%`vs-administrative-gender`", "vs-administrative-gender", true)]
    [InlineData("%vs-administrative-gender", "vs-administrative-gender", false)]
    [InlineData("%`ext-patient-birthTime`", "ext-patient-birthTime", true)]
    [InlineData("%ext-patient-birthTime", "ext-patient-birthTime", false)]
    [InlineData("%`context`", "context", true)]
    [InlineData("%context", "context", false)]
    public void GivenAnExternalConstant_WhenParsed_ThenItRecordsHowItWasSpelled(
        string expression,
        string expectedName,
        bool expectedIsDelimited)
    {
        // Act
        var parsed = _parser.Parse(expression);

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
}
