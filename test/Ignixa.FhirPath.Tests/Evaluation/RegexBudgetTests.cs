// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Evaluation.Functions;
using Ignixa.FhirPath.Parser;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Regex work belongs to the evaluation's budget: a pattern's length is bounded before it is built, and the time
/// matching takes is bounded by the steps the evaluation has left, not only per call.
/// </summary>
public class RegexBudgetTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    [Theory]
    [InlineData("matches")]
    [InlineData("matchesFull")]
    [InlineData("replaceMatches")]
    public void GivenAPatternLongerThanTheLimit_WhenUsed_ThenTheResourceLimitIsExceeded(string function)
    {
        // Arrange
        var pattern = new string('a', FhirPathRegex.MaxPatternLength + 1);
        var context = new EvaluationContext().WithEnvironmentVariable("p", FunctionHelpers.CreateString(pattern));
        var call = function == "replaceMatches" ? "replaceMatches(%p, 'x')" : function + "(%p)";

        // Act
        var exception = Should.Throw<FhirPathResourceLimitExceededException>(() => Evaluate("'b'." + call, context).ToList());

        // Assert
        exception.Message.ShouldContain($"{function}()");
    }

    [Fact]
    public void GivenAPatternAtTheLimit_WhenMatched_ThenItAnswers()
    {
        // Arrange
        var pattern = new string('a', FhirPathRegex.MaxPatternLength);
        var context = new EvaluationContext().WithEnvironmentVariable("p", FunctionHelpers.CreateString(pattern));

        // Act
        var result = Evaluate("'b'.matches(%p)", context).ToList();

        // Assert
        result.Single().Value.ShouldBe(false);
    }

    [Fact]
    public void GivenFewStepsLeft_WhenAMatchWouldRunToThePerCallTimeout_ThenItStopsWhenTheBudgetsTimeIsSpent()
    {
        // Arrange: 400,000 steps stand for 100 ms, so the match gets a fraction of its one-second per-call timeout.
        // The lookahead forces the backtracking engine, and (a+)+ inside it never finishes on this input.
        var context = new EvaluationContext
        {
            Limits = FhirPathEvaluationLimits.Default with { MaxSteps = 400_000 },
        }.WithEnvironmentVariable("s", FunctionHelpers.CreateString(new string('a', 40)));
        var stopwatch = Stopwatch.StartNew();

        // Act
        Should.Throw<FhirPathResourceLimitExceededException>(() => Evaluate("%s.matches('^(?=(a+)+b)')", context).ToList());

        // Assert
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromMilliseconds(600));
    }

    [Fact]
    public void GivenAMatchThatTakesTime_WhenMatched_ThenItsElapsedTimeIsCharged()
    {
        // Arrange
        var budget = new EvaluationBudget(FhirPathEvaluationLimits.Default, CancellationToken.None);
        var regex = FhirPathRegex.Get("ab", System.Text.RegularExpressions.RegexOptions.None, "matches", budget);
        var input = new string('b', 1_000_000);
        var before = budget.StepsTaken;
        var stopwatch = Stopwatch.StartNew();

        // Act
        FhirPathRegex.IsMatch(regex, "ab", input, "matches", budget);
        stopwatch.Stop();

        // Assert
        (budget.StepsTaken - before).ShouldBeGreaterThanOrEqualTo(
            (long)(stopwatch.Elapsed.TotalNanoseconds / EvaluationBudget.NanosecondsPerStep * 0.9));
    }

    private IEnumerable<IElement> Evaluate(string expression, EvaluationContext context) =>
        _evaluator.Evaluate(FunctionHelpers.CreateInteger(0), _parser.Parse(expression), context);
}
