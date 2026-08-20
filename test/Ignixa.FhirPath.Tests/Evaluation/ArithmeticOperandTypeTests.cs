/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * The arithmetic operators' and math functions' operand-type rule, and its locale independence.
 */

using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Covers the rule that FHIRPath arithmetic is defined for numbers, and that a String or Boolean operand
/// is one of §Math's "incompatible items" rather than something to be converted.
/// </summary>
/// <remarks>
/// <para>
/// <c>FunctionHelpers.TryConvertToDecimal</c> used to end in a <c>Convert.ToDecimal(IConvertible)</c>
/// fallback. <see cref="string"/> and <see cref="bool"/> both implement <see cref="IConvertible"/>, so
/// every one of the six math operators and every math function accepted them: <c>'5' + 1</c> answered
/// <c>6</c>, <c>'5' - '1'</c> answered <c>4</c>, <c>'4'.sqrt()</c> answered <c>2</c> and <c>1 + true</c>
/// answered <c>2</c>. String-to-Decimal and Boolean-to-Decimal are <i>explicit</i> conversions in the
/// FHIRPath conversion table, reserved for <c>toDecimal()</c>; this is the same argument the engine
/// already makes for <c>&amp;</c>.
/// </para>
/// <para>
/// Official <c>testMinus4</c> (<c>'a' - 'b'</c>) gave no coverage of any of this, because <c>'a'</c>
/// fails to parse as a number whatever the rule is. <c>'5' - '1'</c> is the case that distinguishes them.
/// </para>
/// <para>
/// The string branch was also the only locale-dependent path in the evaluator, which
/// <see cref="GivenANumericStringOperand_WhenTheHostCultureVaries_ThenTheAnswerDoesNot"/> pins.
/// </para>
/// </remarks>
public class ArithmeticOperandTypeTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    [Theory]
    [InlineData("'5' + 1")]
    [InlineData("1 + '5'")]
    [InlineData("'5' - '1'")]
    [InlineData("'5' - 1")]
    [InlineData("'10' * 2")]
    [InlineData("2 * '10'")]
    [InlineData("'10' / 2")]
    [InlineData("'5' div 2")]
    [InlineData("'5' mod 2")]
    [InlineData("1 + true")]
    [InlineData("true + 1")]
    [InlineData("true - false")]
    [InlineData("true * 2")]
    public void GivenAStringOrBooleanOperand_WhenApplyingAMathOperator_ThenAnErrorIsSignalled(string expression)
    {
        // Act & Assert
        Should.Throw<FhirPathEvaluationException>(() => Evaluate(expression));
    }

    [Theory]
    [InlineData("'4'.sqrt()")]
    [InlineData("'4'.abs()")]
    [InlineData("'4'.ceiling()")]
    [InlineData("'4'.floor()")]
    [InlineData("'4'.round()")]
    [InlineData("'4'.exp()")]
    [InlineData("'4'.ln()")]
    [InlineData("true.sqrt()")]
    [InlineData("true.exp()")]
    [InlineData("true.abs()")]
    public void GivenAStringOrBooleanFocus_WhenCallingAMathFunction_ThenAnErrorIsSignalled(string expression)
    {
        // Act & Assert
        Should.Throw<FhirPathEvaluationException>(() => Evaluate(expression));
    }

    /// <summary>
    /// The explicit conversion still works, which is what makes the rejection above a type rule rather
    /// than a loss of capability.
    /// </summary>
    [Theory]
    [InlineData("'5'.toInteger() + 1", 6)]
    [InlineData("'5'.toDecimal() + 1", 6)]
    [InlineData("'4'.toDecimal().sqrt()", 2)]
    public void GivenAnExplicitConversion_WhenApplyingArithmetic_ThenItEvaluates(string expression, int expected)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.Count.ShouldBe(1);
        Convert.ToDecimal(result[0].Value, CultureInfo.InvariantCulture).ShouldBe(expected);
    }

    /// <summary>
    /// Numeric arithmetic is untouched by the type rule.
    /// </summary>
    [Theory]
    [InlineData("5 + 1", "6")]
    [InlineData("5 - 1", "4")]
    [InlineData("10 * 2", "20")]
    [InlineData("10 / 2", "5")]
    [InlineData("5 div 2", "2")]
    [InlineData("5 mod 2", "1")]
    [InlineData("1.5 + 1", "2.5")]
    [InlineData("'abc' + 'def'", "abcdef")]
    public void GivenOperandsTheOperatorIsDefinedFor_WhenApplyingIt_ThenItEvaluates(string expression, string expected)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.Count.ShouldBe(1);
        DifferentialFixture.Render(result[0].Value).ShouldBe(expected);
    }

    /// <summary>
    /// The empty-propagation and divide-by-zero rules must survive the type rule: they are empty, not
    /// errors, and an over-eager type check is the obvious way to break them.
    /// </summary>
    [Theory]
    [InlineData("1 + {}")]
    [InlineData("{} + 1")]
    [InlineData("1 / 0")]
    [InlineData("1 div 0")]
    [InlineData("1 mod 0")]
    [InlineData("2147483647 + 1")]
    public void GivenAnEmptyOrOverflowingOperation_WhenEvaluated_ThenItYieldsEmpty(string expression)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.ShouldBeEmpty();
    }

    /// <summary>
    /// The same expression over the same data must give the same answer on every server.
    /// </summary>
    /// <remarks>
    /// <c>Convert.ToDecimal</c> takes no <see cref="IFormatProvider"/>, so the old string branch parsed
    /// under <c>CurrentCulture</c>. <c>'1,5' + 1</c> answered <c>2.5</c> on de-DE and fr-FR but <c>16</c>
    /// on en-US, where the comma reads as a group separator; <c>'1.5' + 1</c> answered <c>2.5</c> on
    /// en-US, <c>16</c> on de-DE and threw on fr-FR; <c>'1 234' + 1</c> answered <c>1235</c> on fr-FR and
    /// threw elsewhere. Two of those three pairs differ by <i>value</i> rather than by value-versus-error,
    /// so no host looked suspicious on its own. Asserting a single outcome across cultures is what makes a
    /// re-introduced culture-sensitive parse fail here rather than in a customer's data.
    /// </remarks>
    [Theory]
    [InlineData("'1,5' + 1")]
    [InlineData("'1.5' + 1")]
    [InlineData("'1 234' + 1")]
    [InlineData("'1,5'.sqrt()")]
    [InlineData("'1.5' * 2")]
    public void GivenANumericStringOperand_WhenTheHostCultureVaries_ThenTheAnswerDoesNot(string expression)
    {
        foreach (var culture in new[] { "en-US", "de-DE", "fr-FR" })
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);

                Should.Throw<FhirPathEvaluationException>(
                    () => Evaluate(expression),
                    $"'{expression}' must be rejected under {culture} as it is under every other culture.");
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }
    }

    /// <summary>
    /// The equality operator shared the same coercion, so a String compared equal to the number it spells.
    /// Firely answers false here too.
    /// </summary>
    [Theory]
    [InlineData("1 = '1'", false)]
    [InlineData("1 != '1'", true)]
    [InlineData("1.5 = '1.5'", false)]
    [InlineData("1 = 1.0", true)]
    [InlineData("1 = 1", true)]
    public void GivenANumberAndAString_WhenComparedForEquality_ThenTheyAreNotEqual(string expression, bool expected)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.Count.ShouldBe(1);
        result[0].Value.ShouldBe(expected);
    }

    private List<IElement> Evaluate(string expression)
    {
        var parsed = _parser.Parse(expression);
        return _evaluator.Evaluate(new ScalarRoot(), parsed).ToList();
    }

    private sealed class ScalarRoot : IElement
    {
        public string Name => string.Empty;
        public string InstanceType => "integer";
        public object Value => 0;
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => true;
        public IReadOnlyList<IElement> Children(string? name = null) => [];
        public T? Meta<T>() where T : class => null;
    }
}
