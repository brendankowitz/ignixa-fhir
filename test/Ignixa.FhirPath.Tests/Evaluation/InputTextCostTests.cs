// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Evaluation.Functions;
using Ignixa.FhirPath.Parser;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// A long String or a large collection can reach a function for one step - through a variable, an accumulator or a
/// resource element - while the function reads all of it, so reading it is charged where the function receives it.
/// </summary>
public class InputTextCostTests
{
    /// <summary>A 65,536-character String of digits, built once for about 4,000 steps.</summary>
    private const string LongDigits =
        "defineVariable('v', (1|2|3|4|5|6|7|8|9|10|11|12|13|14|15|16).aggregate($total & $total, '1'))";

    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    [Theory]
    [InlineData("%v.toInteger()")]
    [InlineData("%v.toLong()")]
    [InlineData("%v.toDecimal()")]
    [InlineData("%v.toDate()")]
    [InlineData("%v.toDateTime()")]
    [InlineData("%v.toQuantity()")]
    [InlineData("%v.convertsToDecimal()")]
    [InlineData("%v.convertsToInteger()")]
    [InlineData("'a'.replaceMatches('b', %v)")]
    public void GivenALongStringReadByAFunctionManyTimes_WhenEvaluatedUnderALowStepLimit_ThenTheTextIsCharged(string read)
    {
        // Arrange: 50 reads of 65,536 characters each, far more text than 50,000 steps allow.
        var expression = LongDigits + ".select('" + new string('a', 50) + "'.toChars().select(" + read + "))";
        var context = new EvaluationContext { Limits = FhirPathEvaluationLimits.Default with { MaxSteps = 50_000 } };

        // Act & Assert
        Should.Throw<FhirPathResourceLimitExceededException>(() => Evaluate(expression, context));
    }

    [Fact]
    public void GivenAnAggregateWhoseAccumulatorIsLarge_WhenEachIterationRebindsIt_ThenEachRebindingIsCharged()
    {
        // Arrange: a 2,000-item accumulator rebound for 50 items, more than 50,000 steps allow.
        var expression = "'" + new string('a', 50) + "'.toChars().aggregate($total, '" + new string('a', 2_000) + "'.toChars()).count()";
        var context = new EvaluationContext { Limits = FhirPathEvaluationLimits.Default with { MaxSteps = 50_000 } };

        // Act & Assert
        Should.Throw<FhirPathResourceLimitExceededException>(() => Evaluate(expression, context));
    }

    [Fact]
    public void GivenASmallAggregate_WhenEvaluatedWithTheDefaultLimits_ThenItAnswers()
    {
        // Arrange
        var expression = "(1 | 2 | 3 | 4).aggregate($total + $this, 0)";

        // Act
        var result = Evaluate(expression, new EvaluationContext());

        // Assert
        result.Single().Value.ShouldBe(10);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(ValueOrdering.MaxNumericTextLength, true)]
    [InlineData(ValueOrdering.MaxNumericTextLength + 1, false)]
    public void GivenNumberTextOfALength_WhenReadAsANumber_ThenTextBeyondTheLimitIsNotOne(int fractionDigits, bool expected)
    {
        // Arrange: a fraction decimal would round rather than reject, so only the length can refuse it.
        var text = "0." + new string('1', fractionDigits - 2 > 0 ? fractionDigits - 2 : 1);

        // Act
        var parsed = ValueOrdering.TryParseNumericText(text, out _);

        // Assert
        parsed.ShouldBe(expected);
    }

    private List<Ignixa.Abstractions.IElement> Evaluate(string expression, EvaluationContext context) =>
        _evaluator.Evaluate(FunctionHelpers.CreateInteger(0), _parser.Parse(expression), context).ToList();
}
