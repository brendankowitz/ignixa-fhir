// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.RegularExpressions;

namespace Ignixa.CliOnFhir.Clock;

/// <summary>
/// FHIR instants (R4 datatypes §2.24.0.1: a date and time to the second, with a time zone) read and written as text. Only a
/// complete instant names a point in time; a date alone or a time without a zone does not, so neither is accepted.
/// </summary>
internal static partial class FhirInstant
{
    /// <summary>
    /// Whether <paramref name="text"/> is a FHIR instant, and the point in time it names, with its own offset. FHIR allows any number of
    /// fractional digits; past the seventh (100 ns, what .NET holds) they are dropped, not rounded.
    /// </summary>
    public static bool TryParse(string? text, out DateTimeOffset value) => Problem(text, out value) is null;

    /// <summary>
    /// Why <paramref name="text"/> is not an instant the tool can hold, in words that follow "is": not a FHIR instant, not a date of the
    /// calendar, or outside 0001-01-01 to 9999-12-31 once its offset is applied; null when it is one, which is then <paramref name="value"/>.
    /// </summary>
    public static string? Problem(string? text, out DateTimeOffset value)
    {
        value = default;
        if (text is null || !Shape().IsMatch(text))
        {
            return "not a FHIR instant: give a date and a time to the second with a time zone, for example 2024-05-01T00:00:00Z or 2024-05-01T08:30:00-05:00 (a date alone does not say which moment of the day is meant)";
        }

        string trimmed = Fraction().Replace(text, match => match.Value.Length > 8 ? match.Value[..8] : match.Value);
        if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
        {
            return null;
        }

        // The local date and time alone, without the zone: valid there means only the offset took it out of range.
        return DateTime.TryParseExact(trimmed[..19], "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? "outside the instants the tool can hold (0001-01-01T00:00:00Z to 9999-12-31T23:59:59Z once its offset is applied)"
            : "not a date of the calendar";
    }

    /// <summary>Whether <paramref name="text"/> is written as an instant (date, time to the second, zone), valid date or not.</summary>
    public static bool HasShape(string? text) => text is not null && Shape().IsMatch(text);

    /// <summary>
    /// <paramref name="value"/> as a FHIR instant: to the second, with the fraction it has (trailing zeros dropped), and <c>Z</c>
    /// for UTC or its offset otherwise.
    /// </summary>
    public static string Format(DateTimeOffset value)
    {
        string fraction = value.Ticks % TimeSpan.TicksPerSecond == 0 ? string.Empty : value.ToString(".FFFFFFF", CultureInfo.InvariantCulture);
        string zone = value.Offset == TimeSpan.Zero ? "Z" : value.ToString("zzz", CultureInfo.InvariantCulture);
        return value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + fraction + zone;
    }

    // R4 instant's regular expression, but for the leap second (60), which no clock here can name.
    [GeneratedRegex(@"^\d{4}-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])T([01]\d|2[0-3]):[0-5]\d:[0-5]\d(\.\d+)?(Z|[+-]((0\d|1[0-3]):[0-5]\d|14:00))$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();

    [GeneratedRegex(@"\.\d+", RegexOptions.CultureInvariant)]
    private static partial Regex Fraction();
}
