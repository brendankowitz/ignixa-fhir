// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.FhirPath.Types;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// The cost of relating quantity units: UCUM work is charged to the evaluation that asks for it, and a unit longer
/// than <see cref="QuantityUnitConverter.MaxUnitLength"/> never reaches UCUM.
/// </summary>
public class QuantityUnitCostTests
{
    private static readonly IElement Patient = ResourceJsonNode
        .Parse("""{"resourceType":"Patient","id":"p"}""")
        .ToElement(FhirVersion.R4.GetSchemaProvider());

    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    [Fact]
    public void GivenComparisonsThatEachConvertADifferentValue_WhenEvaluatedUnderALowStepLimit_ThenTheLimitIsExceeded()
    {
        // Arrange: 200 comparisons, each converting a distinct value between two derived units. Every conversion
        // canonicalises its value through UCUM - hundreds of microseconds or more - and each must cost the budget
        // accordingly rather than the single step a comparison node costs.
        var expression = "'" + new string('a', 200) + "'.toChars()"
            + ".select(1 'kPa' < ($index.toString() & ' \\'[psi]\\'').toQuantity()).count()";
        var context = new EvaluationContext { Limits = FhirPathEvaluationLimits.Default with { MaxSteps = 100_000 } };

        // Act & Assert
        Should.Throw<FhirPathResourceLimitExceededException>(
            () => _evaluator.Evaluate(Patient, _parser.Parse(expression), context).ToList());
    }

    [Fact]
    public void GivenTheSameComparisonRepeated_WhenEvaluatedUnderALowStepLimit_ThenItIsAnsweredFromTheMemo()
    {
        // Arrange: one conversion repeated 2,000 times converts once; the repeats cost a step each. Without the memo each
        // repeat would be charged hundreds of steps of UCUM time, far past the million allowed here.
        var expression = "'" + new string('a', 2_000) + "'.toChars().select(1 'cm' < 1 'm').count()";
        var context = new EvaluationContext { Limits = FhirPathEvaluationLimits.Default with { MaxSteps = 1_000_000 } };

        // Act
        var result = _evaluator.Evaluate(Patient, _parser.Parse(expression), context).ToList();

        // Assert
        result.Single().Value.ShouldBe(2_000);
    }

    [Fact]
    public void GivenAUnitLongerThanTheLimit_WhenComparedWithAnEquivalentUnit_ThenTheResultIsEmpty()
    {
        // Arrange: m.m.….m with one symbol more than fits is the same dimension as m65, but is never read.
        var longUnit = string.Join('.', Enumerable.Repeat("m", (QuantityUnitConverter.MaxUnitLength / 2) + 1));
        var expression = $"1 '{longUnit}' = 1 'm{(QuantityUnitConverter.MaxUnitLength / 2) + 1}'";

        // Act
        var result = _evaluator.Evaluate(Patient, _parser.Parse(expression), new EvaluationContext()).ToList();

        // Assert
        longUnit.Length.ShouldBeGreaterThan(QuantityUnitConverter.MaxUnitLength);
        result.ShouldBeEmpty();
    }

    [Fact]
    public void GivenAUnitAtTheLimit_WhenComparedWithAnEquivalentUnit_ThenItIsEqual()
    {
        // Arrange
        var longUnit = string.Join('.', Enumerable.Repeat("m", QuantityUnitConverter.MaxUnitLength / 2));
        var expression = $"1 '{longUnit}' = 1 'm{QuantityUnitConverter.MaxUnitLength / 2}'";

        // Act
        var result = _evaluator.Evaluate(Patient, _parser.Parse(expression), new EvaluationContext()).ToList();

        // Assert
        longUnit.Length.ShouldBeLessThanOrEqualTo(QuantityUnitConverter.MaxUnitLength);
        result.Single().Value.ShouldBe(true);
    }
}
