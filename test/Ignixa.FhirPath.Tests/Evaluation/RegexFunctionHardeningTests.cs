// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Evaluation.Functions;
using Ignixa.FhirPath.Parser;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// matches(), matchesFull() and replaceMatches() run expression-supplied patterns, so they must not hang on
/// catastrophic backtracking, must report a timeout or an invalid pattern as an error rather than "no match",
/// and must keep working for constructs only the backtracking engine supports (#532, #538).
/// </summary>
public class RegexFunctionHardeningTests
{
    /// <summary>
    /// Far beyond what a backtracking engine survives on these patterns (2^n paths), and trivial for a
    /// linear-time one.
    /// </summary>
    private static readonly string Hostile = new string('a', 5_000) + "!";

    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    [Theory]
    [InlineData("%s.matches('^(a+)+$')", false)]
    [InlineData("%s.matchesFull('(a+)+')", false)]
    [InlineData("%s.replaceMatches('^(a+)+$', 'x') = %s", true)]
    public void GivenACatastrophicBacktrackingPattern_WhenEvaluated_ThenItAnswersPromptly(string expression, bool expected)
    {
        // Arrange
        var context = new EvaluationContext().WithEnvironmentVariable("s", FunctionHelpers.CreateString(Hostile));
        object? result = null;

        // Act
        Should.CompleteIn(
            () => result = Evaluate(expression, context).Single().Value,
            TimeSpan.FromSeconds(10),
            "The pattern ran on a backtracking engine without a bound.");

        // Assert
        result.ShouldBe(expected);
    }

    [Theory]
    [InlineData(@"'abcabc'.matches('(abc)\\1')", true)]
    [InlineData(@"'abcabd'.matches('(abc)\\1')", false)]
    [InlineData(@"'100USD'.matches('\\d+(?=USD)')", true)]
    [InlineData(@"'100EUR'.matches('\\d+(?=USD)')", false)]
    [InlineData(@"'xb'.matches('(?<!a)b')", true)]
    [InlineData(@"'abab'.matchesFull('(ab)\\1')", true)]
    [InlineData(@"'abac'.matchesFull('(ab)\\1')", false)]
    [InlineData(@"'aaa'.matchesFull('(?>a+)')", true)]
    [InlineData(@"'a1b2'.replaceMatches('[a-z](?=2)', 'X')", "a1X2")]
    [InlineData(@"'aa-bb'.replaceMatches('(\\w)\\1', '<$1>')", "<a>-<b>")]
    public void GivenAPatternOnlyTheBacktrackingEngineSupports_WhenEvaluated_ThenItStillAnswers(string expression, object expected)
    {
        // Act
        var result = Evaluate(expression).Single().Value;

        // Assert
        result.ShouldBe(expected);
    }

    [Fact]
    public void GivenSubstitutionGroups_WhenReplaceMatchesRunsOnTheLinearEngine_ThenGroupsAreSubstituted()
    {
        // Act
        var result = Evaluate(@"'a12b34'.replaceMatches('(\\d)(\\d)', '$2$1')").Single().Value;

        // Assert
        result.ShouldBe("a21b43");
    }

    [Theory]
    [InlineData("('a' | 'b').where($this.matches('(')).empty()", "matches")]
    [InlineData("('a' | 'b').where($this.matchesFull('(')).empty()", "matchesFull")]
    [InlineData("('a' | 'b').where($this.replaceMatches('(', 'x') = 'x').empty()", "replaceMatches")]
    public void GivenAnInvalidPatternInANegatedInvariantShape_WhenEvaluated_ThenAnErrorIsSignalledRatherThanTrue(
        string expression,
        string functionName)
    {
        // Act
        var exception = Should.Throw<FhirPathEvaluationException>(() => Evaluate(expression).ToList());

        // Assert
        exception.Message.ShouldContain($"{functionName}()");
        exception.InnerException.ShouldBeAssignableTo<ArgumentException>();
    }

    [Fact]
    public void GivenABacktrackingPatternThatExceedsItsTimeout_WhenMatching_ThenAnEvaluationErrorCarriesTheTimeout()
    {
        // Arrange: the lookahead forces the backtracking engine; (a+)+ inside it is exponential.
        var regex = FhirPathRegex.Create("(?=(a+)+b)", RegexOptions.None, "matches", TimeSpan.FromMilliseconds(50));

        // Act
        var exception = Should.Throw<FhirPathEvaluationException>(
            () => FhirPathRegex.IsMatch(regex, new string('a', 40), "matches"));

        // Assert
        exception.Message.ShouldContain("matches()");
        exception.InnerException.ShouldBeOfType<RegexMatchTimeoutException>();
    }

    [Fact]
    public void GivenABacktrackingPatternThatExceedsItsTimeout_WhenReplacing_ThenAnEvaluationErrorCarriesTheTimeout()
    {
        // Arrange
        var regex = FhirPathRegex.Create("(?=(a+)+b)a", RegexOptions.None, "replaceMatches", TimeSpan.FromMilliseconds(50));

        // Act
        var exception = Should.Throw<FhirPathEvaluationException>(
            () => FhirPathRegex.Replace(regex, new string('a', 40), "x", "replaceMatches"));

        // Assert
        exception.Message.ShouldContain("replaceMatches()");
        exception.InnerException.ShouldBeOfType<RegexMatchTimeoutException>();
    }

    [Fact]
    public void GivenAPatternTheLinearEngineSupports_WhenBuilt_ThenItRunsOnTheLinearEngine()
    {
        // Act
        var regex = FhirPathRegex.Create("^(a+)+$", RegexOptions.Singleline, "matches", TimeSpan.FromMilliseconds(50));

        // Assert: NonBacktracking is what makes the pattern safe; a backtracking build here would only be bounded
        // by the timeout, and a regression to it would show up as slow evaluation rather than a failure.
        regex.Options.ShouldBe(RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    }

    private IEnumerable<IElement> Evaluate(string expression, EvaluationContext? context = null) =>
        _evaluator.Evaluate(FunctionHelpers.CreateInteger(0), _parser.Parse(expression), context ?? new EvaluationContext());
}
