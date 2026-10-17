// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.RegularExpressions;

namespace Ignixa.CliOnFhir.Clock;

/// <summary>A full FHIR date (R4 datatypes §2.24.0.1: <c>YYYY-MM-DD</c>), a day of the calendar; years and months alone are not days.</summary>
internal static partial class FhirDate
{
    /// <summary>Whether <paramref name="text"/> is written as a full date, a day of the calendar or not.</summary>
    public static bool HasShape(string? text) => text is not null && Shape().IsMatch(text);

    /// <summary>Whether <paramref name="text"/> is a full FHIR date of the calendar, and which day.</summary>
    public static bool TryParse(string? text, out DateOnly day)
    {
        day = default;
        return HasShape(text) && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}
