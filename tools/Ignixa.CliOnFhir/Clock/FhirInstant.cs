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
    /// <summary>Whether <paramref name="text"/> is a FHIR instant, and the point in time it names, with its own offset.</summary>
    public static bool TryParse(string? text, out DateTimeOffset value)
    {
        value = default;
        if (text is null || !Shape().IsMatch(text))
        {
            return false;
        }

        // .NET reads at most seven fractional digits; FHIR allows more, which name no finer point than .NET can hold.
        string trimmed = Fraction().Replace(text, match => match.Value.Length > 8 ? match.Value[..8] : match.Value);
        return DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

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
    [GeneratedRegex(@"^\d{4}-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])T([01]\d|2[0-3]):[0-5]\d:[0-5]\d(\.\d{1,9})?(Z|[+-]((0\d|1[0-3]):[0-5]\d|14:00))$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();

    [GeneratedRegex(@"\.\d+", RegexOptions.CultureInvariant)]
    private static partial Regex Fraction();
}
