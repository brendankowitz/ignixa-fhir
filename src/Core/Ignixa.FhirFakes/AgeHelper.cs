// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes;

/// <summary>
/// Calendar-correct age and birth year calculations using a mid-year birthday assumption (July 1).
/// </summary>
internal static class AgeHelper
{
    private const int MidYearMonth = 7;
    private const int MidYearDay = 1;

    /// <summary>
    /// Calculates the birth year for a given target age, assuming mid-year birthday (July 1).
    /// Uses calendar-correct <see cref="DateTime.AddYears"/> arithmetic to avoid off-by-one errors.
    /// </summary>
    internal static int BirthYearFromAge(int age)
    {
        var today = DateTime.UtcNow;
        var tentativeBirthYear = today.Year - age;
        var tentativeBirthDate = new DateTime(tentativeBirthYear, MidYearMonth, MidYearDay);
        var actualAge = today.Year - tentativeBirthDate.Year;
        if (today < tentativeBirthDate.AddYears(actualAge))
        {
            actualAge--;
        }

        return tentativeBirthYear - (age - actualAge);
    }

    /// <summary>
    /// Calculates approximate age from a birth year, assuming mid-year birthday (July 1).
    /// Uses calendar-correct <see cref="DateTime.AddYears"/> arithmetic to avoid off-by-one errors.
    /// </summary>
    internal static int AgeFromBirthYear(int year)
    {
        var today = DateTime.UtcNow;
        var assumedBirthDate = new DateTime(year, MidYearMonth, MidYearDay);
        var age = today.Year - assumedBirthDate.Year;
        if (today < assumedBirthDate.AddYears(age))
        {
            age--;
        }

        return age;
    }
}
