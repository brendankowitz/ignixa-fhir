// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Evaluation.Functions;
using Ignixa.FhirPath.Parser;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// A collection computed once and reused - a variable, or a subexpression memoised for the evaluation - costs nothing
/// to hand out again, so the functions that walk it without evaluating a node per item must charge per item.
/// </summary>
public class CollectionReuseCostTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    [Theory]
    [InlineData("%s.sort().count()")]
    [InlineData("%s.sum()")]
    [InlineData("%s.min()")]
    [InlineData("%s.max()")]
    [InlineData("%s.avg()")]
    [InlineData("%s.lowBoundary().count()")]
    [InlineData("%s.highBoundary().count()")]
    [InlineData("%s.tail().count()")]
    [InlineData("%s.skip(1).count()")]
    [InlineData("%s.take(2000).count()")]
    [InlineData("%s.ofType(Integer).count()")]
    [InlineData("%s.type().count()")]
    [InlineData("%s.hasValue()")]
    [InlineData("%s.descendants().count()")]
    [InlineData("%s.count()")]
    [InlineData("%s.last()")]
    [InlineData("%s.empty()")]
    [InlineData("%s")]
    public void GivenALargeCollectionReusedManyTimes_WhenAFunctionWalksIt_ThenEachItemIsCharged(string walk)
    {
        // Arrange: 2,000 numbers built once, then walked 50 times - 100,000 items, more than the limit allows.
        var expression = "defineVariable('s', '" + new string('a', 2_000) + "'.toChars().select($index))"
            + ".select('" + new string('a', 50) + "'.toChars().select(" + walk + "))";
        var context = new EvaluationContext { Limits = FhirPathEvaluationLimits.Default with { MaxSteps = 100_000 } };

        // Act & Assert
        Should.Throw<FhirPathResourceLimitExceededException>(
            () => _evaluator.Evaluate(FunctionHelpers.CreateInteger(0), _parser.Parse(expression), context).ToList());
    }

    [Fact]
    public void GivenASmallCollectionReusedAFewTimes_WhenSorted_ThenItAnswersWithinTheDefaultLimits()
    {
        // Arrange
        var expression = "defineVariable('s', (3 | 1 | 2)).select((1 | 2 | 3).select(%s.sort().first()))";

        // Act
        var result = _evaluator.Evaluate(FunctionHelpers.CreateInteger(0), _parser.Parse(expression), new EvaluationContext()).ToList();

        // Assert
        result.Select(element => element.Value).ShouldBe([1, 1, 1]);
    }
}
