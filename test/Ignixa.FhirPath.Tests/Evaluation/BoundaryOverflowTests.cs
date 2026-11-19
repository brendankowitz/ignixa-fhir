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
/// A boundary decimal cannot hold is empty, as FHIRPath answers any arithmetic overflow, not a raw
/// <see cref="OverflowException"/>.
/// </summary>
public class BoundaryOverflowTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    [Fact]
    public void GivenAValueNearDecimalsLimit_WhenItsLowBoundaryAtAHighPrecisionIsRequested_ThenTheResultIsEmpty()
    {
        // Arrange: scaling the value by 10^28 overflows decimal.
        const string expression = "79228162514264337593543950.335.lowBoundary(28)";

        // Act
        var result = _evaluator.Evaluate(FunctionHelpers.CreateInteger(0), _parser.Parse(expression), new EvaluationContext()).ToList();

        // Assert
        result.ShouldBeEmpty();
    }
}
