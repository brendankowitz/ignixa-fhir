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
/// The instant, with the offset it was given in (UTC for the clock, and for a value given without a zone). For a year, a month
/// or a date, the first instant of that period in UTC, as <c>patient_summary</c> and <c>patient_cohort</c> have always read
/// one (2024 is 2024-01-01T00:00:00Z, 2024-05-01 is 2024-05-01T00:00:00Z).
/// </param>
/// <param name="Source">One of <see cref="AsOfSources"/>.</param>
/// <param name="Precision">What the value was given as: <see cref="YearPrecision"/>, <see cref="MonthPrecision"/>, <see cref="DatePrecision"/> or <see cref="InstantPrecision"/>.</param>
/// <param name="ZoneAssumed">Whether a date and time was given without a zone, so it was read as UTC.</param>
public sealed record AsOf(
    [property: JsonIgnore] DateTimeOffset Moment,
    string Source,
    [property: JsonIgnore] string Precision = AsOf.InstantPrecision,
    [property: JsonIgnore] bool ZoneAssumed = false)
{
    /// <summary>A year alone (<c>2024</c>).</summary>
    public const string YearPrecision = "year";

    /// <summary>A year and month (<c>2024-05</c>).</summary>
    public const string MonthPrecision = "month";

    /// <summary>A full date (<c>2024-05-01</c>).</summary>
    public const string DatePrecision = "date";

    /// <summary>A date and a time, with a zone or (<see cref="ZoneAssumed"/>) read as UTC without one.</summary>
    public const string InstantPrecision = "instant";

    /// <summary>The instant as a FHIR instant (<see cref="FhirInstant.Format"/>).</summary>
    [JsonPropertyOrder(-1)]
    public string Instant => FhirInstant.Format(Moment);

    /// <summary>The precision as facts show it: absent for an instant, the usual case.</summary>
    [JsonPropertyName("precision")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShownPrecision => Precision == InstantPrecision ? null : Precision;

    /// <summary><c>true</c> when the zone was assumed; absent otherwise.</summary>
    [JsonPropertyName("zoneAssumed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ShownZoneAssumed => ZoneAssumed ? true : null;

    /// <summary>The calendar day, as a FHIR date: the instant's day in its own offset, so the first day of a year or month given.</summary>
    [JsonIgnore]
    public string Day => Moment.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// The value as a FHIR dateTime, at the precision it was given (a dateTime may be a year, a month or a date, and a time of day
    /// nobody named is not invented); the instant otherwise.
    /// </summary>
    [JsonIgnore]
    public string DateTimeText => Precision switch
    {
        YearPrecision => Moment.ToString("yyyy", CultureInfo.InvariantCulture),
        MonthPrecision => Moment.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        DatePrecision => Day,
        _ => Instant,
    };

    /// <summary>The first instant of <paramref name="day"/> in UTC, at <paramref name="precision"/> (a date, or the year or month it starts).</summary>
    public static AsOf OfDate(DateOnly day, string source, string precision = DatePrecision) =>
        new(new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), source, precision);
}
