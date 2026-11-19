// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Types;
using NSubstitute;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class MeteredUnitConverterTests
{
    [Fact]
    public void GivenAConversionThatTakesTime_WhenConverted_ThenItsElapsedTimeIsCharged()
    {
        // Arrange: the inner converter takes at least 20 ms, so at least 20 ms worth of steps must be charged.
        var inner = Substitute.For<IQuantityUnitConverter>();
        inner.Convert(1m, "a", "b").Returns(_ => SpinThenReturn(TimeSpan.FromMilliseconds(20), 2m));
        var budget = new EvaluationBudget(FhirPathEvaluationLimits.Default, CancellationToken.None);
        var converter = new MeteredUnitConverter(budget, inner);

        // Act
        var converted = converter.Convert(1m, "a", "b");

        // Assert
        converted.ShouldBe(2m);
        budget.StepsTaken.ShouldBeGreaterThanOrEqualTo(20_000_000 / EvaluationBudget.NanosecondsPerStep);
    }

    [Fact]
    public void GivenAConversionAlreadyMadeInThisEvaluation_WhenRepeated_ThenTheInnerConverterIsNotAskedAgain()
    {
        // Arrange
        var inner = Substitute.For<IQuantityUnitConverter>();
        inner.Convert(1.5m, "cm", "m").Returns(0.015m);
        var budget = new EvaluationBudget(FhirPathEvaluationLimits.Default, CancellationToken.None);
        var converter = new MeteredUnitConverter(budget, inner);
        converter.Convert(1.5m, "cm", "m");
        var stepsAfterFirst = budget.StepsTaken;

        // Act
        var repeated = converter.Convert(1.5m, "cm", "m");

        // Assert
        repeated.ShouldBe(0.015m);
        inner.Received(1).Convert(1.5m, "cm", "m");
        (budget.StepsTaken - stepsAfterFirst).ShouldBe(1);
    }

    [Fact]
    public void GivenEqualValuesOfDifferentScale_WhenConverted_ThenEachIsConvertedSeparately()
    {
        // Arrange: 1.0 and 1.00 are equal but convert to results of different scale, which ~ reads as precision.
        var inner = Substitute.For<IQuantityUnitConverter>();
        inner.Convert(Arg.Any<decimal>(), "cm", "m").Returns(call => call.Arg<decimal>().Scale == 1 ? 0.010m : 0.0100m);
        var converter = new MeteredUnitConverter(
            new EvaluationBudget(FhirPathEvaluationLimits.Default, CancellationToken.None),
            inner);

        // Act
        var first = converter.Convert(1.0m, "cm", "m");
        var second = converter.Convert(1.00m, "cm", "m");

        // Assert
        first!.Value.Scale.ShouldBe((byte)3);
        second!.Value.Scale.ShouldBe((byte)4);
    }

    private static decimal SpinThenReturn(TimeSpan duration, decimal value)
    {
        var start = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(start) < duration)
        {
            Thread.SpinWait(100);
        }

        return value;
    }
}
