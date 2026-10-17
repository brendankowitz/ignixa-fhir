// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Ignixa.CliOnFhir.Clock;

namespace Ignixa.CliOnFhir.Drafting;

/// <summary>Reads a draft's date and time members, each defaulting to the session's instant (<see cref="AsOf"/>), never the wall clock.</summary>
internal static partial class DraftTimes
{
    /// <summary>
    /// A FHIR dateTime member (a year, a month, a date, or a date and time with a zone), or <paramref name="asOf"/>: its instant, or
    /// its day when it was given as a date, since a dateTime may be a date and a time of day the caller never named is not invented.
    /// </summary>
    public static string DateTimeOr(DraftArguments input, string name, AsOf asOf)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(asOf);

        return input.OptionalString(name) is { } text
            ? (DateTimeShape().IsMatch(text) ? text : throw DraftArguments.Invalid($"'{input.Of(name)}' must be a FHIR dateTime: 2024, 2024-05, 2024-05-01 or 2024-05-01T08:30:00Z (a time needs its zone)."))
            : asOf.DateTimeText;
    }

    /// <summary>A FHIR dateTime member, or null when it is absent.</summary>
    public static string? OptionalDateTime(DraftArguments input, string name)
    {
        ArgumentNullException.ThrowIfNull(input);

        return input.OptionalString(name) is { } text
            ? (DateTimeShape().IsMatch(text) ? text : throw DraftArguments.Invalid($"'{input.Of(name)}' must be a FHIR dateTime: 2024, 2024-05, 2024-05-01 or 2024-05-01T08:30:00Z (a time needs its zone)."))
            : null;
    }

    /// <summary>A FHIR instant member, or <paramref name="asOf"/>'s instant (for a date-only asOf, that day's first instant in UTC: an instant needs a time).</summary>
    public static string InstantOr(DraftArguments input, string name, AsOf asOf)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(asOf);

        return input.OptionalString(name) is { } text
            ? (FhirInstant.Problem(text, out _) is { } problem ? throw DraftArguments.Invalid($"'{input.Of(name)}' is {problem}.") : text)
            : asOf.Instant;
    }

    // R4 dateTime's regular expression (datatypes §2.24.0.1).
    [GeneratedRegex(@"^([0-9]([0-9]([0-9][1-9]|[1-9]0)|[1-9]00)|[1-9]000)(-(0[1-9]|1[0-2])(-(0[1-9]|[1-2][0-9]|3[0-1])(T([01][0-9]|2[0-3]):[0-5][0-9]:([0-5][0-9]|60)(\.[0-9]+)?(Z|(\+|-)((0[0-9]|1[0-3]):[0-5][0-9]|14:00)))?)?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex DateTimeShape();
}
