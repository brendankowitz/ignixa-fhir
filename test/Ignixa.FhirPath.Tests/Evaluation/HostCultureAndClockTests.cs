// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Evaluation.Functions;
using Ignixa.FhirPath.Parser;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// today(), comparisons against it, and case-insensitive regex answer the same on every host culture, and
/// timeOfDay() is on the same clock as now() (#539).
/// </summary>
/// <remarks>
/// th-TH and ar-SA default to non-Gregorian calendars (Buddhist, Um Al-Qura), so a culture-sensitive
/// <c>yyyy</c> prints 2569 or 1448; tr-TR folds <c>I</c> to dotless <c>ı</c>, not <c>i</c>. The current culture
/// is per-thread, so setting it inside a test does not leak into tests running in parallel.
/// </remarks>
public class HostCultureAndClockTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    [Theory]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    [InlineData("tr-TR")]
    public void GivenAHostCulture_WhenEvaluatingToday_ThenItIsTheGregorianUtcDate(string culture)
    {
        // Arrange
        var before = UtcDate();

        // Act
        var today = UnderCulture(culture, () => Evaluate("today().toString()").Single().Value);

        // Assert: either side of a UTC midnight that falls during the call.
        today.ShouldBeOneOf(before, UtcDate());
    }

    [Theory]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    [InlineData("tr-TR")]
    public void GivenAHostCulture_WhenComparingAgainstToday_ThenTheAnswerMatchesTheInvariantCulture(string culture)
    {
        // Arrange: a birth date in the past and a date beyond any plausible run of this suite.
        const string Expression = "(@1990-05-01 <= today()) and (today() < @2100-01-01)";

        // Act
        var result = UnderCulture(culture, () => Evaluate(Expression).Single().Value);

        // Assert
        result.ShouldBe(true);
    }

    [Theory]
    [InlineData("th-TH", "'I'.matches('(?i)^i')")]
    [InlineData("ar-SA", "'I'.matches('(?i)i$')")]
    [InlineData("tr-TR", "'I'.matches('(?i)i')")]
    [InlineData("tr-TR", "'i'.matches('(?i)I')")]
    [InlineData("tr-TR", "'I'.matchesFull('i', 'i')")]
    public void GivenAHostCulture_WhenMatchingCaseInsensitively_ThenTheAnswerMatchesTheInvariantCulture(string culture, string expression)
    {
        // Act
        var result = UnderCulture(culture, () => Evaluate(expression).Single().Value);

        // Assert
        result.ShouldBe(true);
    }

    [Fact]
    public void GivenTheHostClock_WhenEvaluatingTimeOfDay_ThenItAgreesWithTheTimeComponentOfNow()
    {
        // Act
        var now = (string)Evaluate("now().toString()").Single().Value!;
        var timeOfDay = (string)Evaluate("timeOfDay().toString()").Single().Value!;

        // Assert: both are UTC; allow a few seconds, and wrap around midnight.
        var nowTime = DateTimeOffset.Parse(now, CultureInfo.InvariantCulture).UtcDateTime.TimeOfDay;
        var time = TimeSpan.ParseExact(timeOfDay, @"hh\:mm\:ss", CultureInfo.InvariantCulture);
        var gap = (time - nowTime).Duration();
        TimeSpan.FromTicks(Math.Min(gap.Ticks, TimeSpan.FromDays(1).Ticks - gap.Ticks))
            .ShouldBeLessThan(TimeSpan.FromSeconds(5), $"now() = {now}, timeOfDay() = {timeOfDay}");
    }

    private static string UtcDate() => DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static T UnderCulture<T>(string culture, Func<T> action)
    {
        var ambient = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = ambient;
        }
    }

    private IEnumerable<IElement> Evaluate(string expression) =>
        _evaluator.Evaluate(FunctionHelpers.CreateInteger(0), _parser.Parse(expression)).ToList();
}
