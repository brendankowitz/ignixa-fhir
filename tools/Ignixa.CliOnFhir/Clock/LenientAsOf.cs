// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;

namespace Ignixa.CliOnFhir.Clock;

/// <summary>
/// The asOf forms beyond a full date and a FHIR instant that <c>patient_summary</c> and <c>patient_cohort</c> accepted before
/// the asOf sources were unified (V14, <c>Cohorts/CohortInstant.cs</c>), read as they read them, so that an agent tuned on them
/// keeps its calls: a year or a month is the first instant of that period in UTC, and a date and time (anything with a <c>T</c>
/// that .NET reads as one) without a zone is read as UTC (<see cref="AsOf.ZoneAssumed"/>), one with a zone keeps it.
/// </summary>
internal static class LenientAsOf
{
    private const DateTimeStyles Utc = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;

    /// <summary>The value <paramref name="text"/> (already trimmed) names in one of these forms; null when it is none of them.</summary>
    public static AsOf? Parse(string text, string source)
    {
        if (DateTime.TryParseExact(text, "yyyy", CultureInfo.InvariantCulture, Utc, out DateTime year))
        {
            return AsOf.OfDate(DateOnly.FromDateTime(year), source, AsOf.YearPrecision);
        }

        if (DateTime.TryParseExact(text, "yyyy-MM", CultureInfo.InvariantCulture, Utc, out DateTime month))
        {
            return AsOf.OfDate(DateOnly.FromDateTime(month), source, AsOf.MonthPrecision);
        }

        if (!text.Contains('T', StringComparison.Ordinal)
            || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset moment))
        {
            return null;
        }

        // RoundtripKind leaves a value written without a zone Unspecified; one with Z or an offset is Utc or Local.
        bool zoneAssumed = DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime written)
            && written.Kind == DateTimeKind.Unspecified;
        return new AsOf(moment, source, AsOf.InstantPrecision, zoneAssumed);
    }
}
