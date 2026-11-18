/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Pins math function overflow and precision handling (#537).
 */

using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.FhirPath.Tests.TestHelpers;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// FHIRPath §Math: an operation whose result does not fit its type yields empty, as the operators already do.
/// A function must not leak <see cref="OverflowException"/>. <c>round()</c> has a bounded precision, so an
/// unsupported one is an evaluation error and not an <see cref="ArgumentOutOfRangeException"/>.
/// </summary>
public class MathFunctionRangeTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    [Theory]
    [InlineData("1000.exp()")]
    [InlineData("1000.0.exp()")]
    [InlineData("10.0.power(40)")]
    [InlineData("2.power(1000)")]
    [InlineData("100000000.0.power(100000)")]
    [InlineData("(-8).power(0.5)")]
    [InlineData("(-2147483647 - 1).abs()")]
    public void GivenAResultOutsideTheTypeRange_WhenEvaluated_ThenTheResultIsEmpty(string expression)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("1.5.round(-1)", "-1")]
    [InlineData("1.5.round(29)", "29")]
    [InlineData("1.5.round(99999)", "99999")]
    public void GivenAPrecisionOutsideTheSupportedRange_WhenRounding_ThenItIsAnEvaluationError(
        string expression,
        string precision)
    {
        // Act
        var evaluate = () => Evaluate(expression);

        // Assert
        var failure = Should.Throw<FhirPathEvaluationException>(evaluate);
        failure.Message.ShouldContain("round()");
        failure.Message.ShouldContain(precision);
    }

    [Theory]
    [InlineData("1.55.round(0)", "2")]
    [InlineData("1.55.round(1)", "1.6")]
    [InlineData("1.5.round(28)", "1.5")]
    public void GivenAPrecisionWithinTheSupportedRange_WhenRounding_ThenItRounds(string expression, string expected)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.Select(e => e.Value).ShouldBe([decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture)]);
    }

    [Fact]
    public void GivenAnInRangeExponent_WhenEvaluated_ThenItIsStillComputed()
    {
        // Act
        var result = Evaluate("2.power(10)");

        // Assert
        result.ShouldHaveSingleItem().Value.ShouldBe(1024m);
    }

    private List<Ignixa.Abstractions.IElement> Evaluate(string expression)
    {
        var patient = FuzzPatient.Create();
        var context = new EvaluationContext { Resource = patient, RootResource = patient, Schema = FuzzPatient.Schema };
        return _evaluator.Evaluate(patient, _parser.Parse(expression), context).ToList();
    }
}
