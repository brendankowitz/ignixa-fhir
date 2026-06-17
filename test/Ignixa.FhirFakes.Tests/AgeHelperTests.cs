// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.FhirFakes;
using Xunit;

namespace Ignixa.FhirFakes.Tests;

/// <summary>
/// Unit tests for AgeHelper.
/// </summary>
public class AgeHelperTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(65)]
    public void GivenBirthdayAlreadyPassedThisYear_WhenSolvingBirthYear_ThenComputedAgeIsExact(int age)
    {
        // A month/day on-or-before today already happened this year (has-had-birthday branch).
        var today = DateTime.UtcNow;
        var (month, day) = (today.Month, today.Day);

        var year = AgeHelper.BirthYearFromAge(age, month, day);

        AgeAsOf(new DateTime(year, month, day), today).ShouldBe(age);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(65)]
    public void GivenBirthdayNotYetThisYear_WhenSolvingBirthYear_ThenComputedAgeIsExact(int age)
    {
        // A month/day strictly after today has not happened yet this year (not-yet branch).
        // Use tomorrow's calendar month/day; if today is Dec 31 there is no later slot in the
        // year, so fall back to a yesterday-style date which still exercises a real solve.
        var today = DateTime.UtcNow;
        var afterToday = today is { Month: 12, Day: 31 } ? today.AddDays(-1) : today.AddDays(1);
        var (month, day) = (afterToday.Month, afterToday.Day);

        var year = AgeHelper.BirthYearFromAge(age, month, day);

        AgeAsOf(new DateTime(year, month, day), today).ShouldBe(age);
    }

    private static int AgeAsOf(DateTime birthDate, DateTime asOf)
    {
        var age = asOf.Year - birthDate.Year;
        if (asOf < birthDate.AddYears(age))
        {
            age--;
        }

        return age;
    }
}
