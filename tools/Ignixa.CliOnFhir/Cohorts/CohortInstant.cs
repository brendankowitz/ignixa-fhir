// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Cohorts;

/// <summary>
/// FHIR date and dateTime literals as the instants the cohort rules compare: a partial date is its first instant, and a date
/// or a dateTime without an offset is read as UTC (2025-08-17 is 2025-08-17T00:00:00Z).
/// </summary>
internal static class CohortInstant
{
    private static readonly string[] PartialFormats = ["yyyy", "yyyy-MM", "yyyy-MM-dd"];

    /// <summary>The instant <paramref name="literal"/> starts at, or null when it is not a FHIR date or dateTime.</summary>
    public static DateTimeOffset? Parse(string? literal)
    {
        if (string.IsNullOrWhiteSpace(literal))
        {
            return null;
        }

        const DateTimeStyles utc = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
        if (DateTime.TryParseExact(literal, PartialFormats, CultureInfo.InvariantCulture, utc, out DateTime date))
        {
            return new DateTimeOffset(date, TimeSpan.Zero);
        }

        return literal.Contains('T', StringComparison.Ordinal) && DateTimeOffset.TryParse(literal, CultureInfo.InvariantCulture, utc, out DateTimeOffset instant)
            ? instant
            : null;
    }

    /// <summary>A date or dateTime the request gives, for example <c>window.after</c>.</summary>
    /// <exception cref="ToolRequestException">Not a FHIR date or dateTime.</exception>
    public static DateTimeOffset Argument(string text, string where) =>
        Parse(text.Trim()) ?? throw ToolRequestException.Error("invalid-parameter", $"{where} '{QueryEcho.Value(text)}' is not a date: write 2026-08-17 or 2026-08-17T00:00:00Z.");

    /// <summary>The instant as the facts show it.</summary>
    public static string Format(DateTimeOffset instant) =>
        instant.TimeOfDay == TimeSpan.Zero
            ? instant.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
