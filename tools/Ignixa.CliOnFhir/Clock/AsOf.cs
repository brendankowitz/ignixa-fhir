// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Clock;

/// <summary>
/// The point in time a call's answers are anchored to: the instant the data was pulled, or the one the caller named, or the
/// session's clock. Defaults that mean "now" (a draft's <c>authoredOn</c>, a view's <c>%asOf</c>) read it, never the wall clock.
/// </summary>
/// <param name="Moment">
/// The instant, with the offset it was given in (UTC for the clock). For a date-only value, that day's first instant in UTC, as
/// <c>patient_summary</c> and <c>patient_cohort</c> have always read a date (2024-05-01 is 2024-05-01T00:00:00Z).
/// </param>
/// <param name="Source">One of <see cref="AsOfSources"/>.</param>
/// <param name="IsDateOnly">Whether the value was given as a FHIR date, so it names a day, not a moment of it.</param>
public sealed record AsOf([property: JsonIgnore] DateTimeOffset Moment, string Source, [property: JsonIgnore] bool IsDateOnly = false)
{
    /// <summary>The precision <see cref="Precision"/> names for a date-only value.</summary>
    public const string DatePrecision = "date";

    /// <summary>The instant as a FHIR instant (<see cref="FhirInstant.Format"/>).</summary>
    [JsonPropertyOrder(-1)]
    public string Instant => FhirInstant.Format(Moment);

    /// <summary><see cref="DatePrecision"/> when the value was given as a date; absent for an instant.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Precision => IsDateOnly ? DatePrecision : null;

    /// <summary>The calendar day, as a FHIR date: the date given, or the instant's day in its own offset.</summary>
    [JsonIgnore]
    public string Day => Moment.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The value as a FHIR dateTime: the day for a date-only value (a dateTime may be a date), the instant otherwise.</summary>
    [JsonIgnore]
    public string DateTimeText => IsDateOnly ? Day : Instant;

    /// <summary>The first instant of <paramref name="day"/> in UTC, marked date-only.</summary>
    public static AsOf OfDate(DateOnly day, string source) => new(new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), source, IsDateOnly: true);
}
