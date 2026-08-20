// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Search.Indexing.Converters;

/// <summary>
/// A converter used to convert from <c>Timing</c> to a list of <see cref="DateTimeSearchValue"/>.
/// </summary>
/// <remarks>
/// <para>
/// The FHIR search specification reduces a Timing to a single interval:
/// "[Timing] the specified scheduling details are ignored and only the outer limits matter"
/// (https://hl7.org/fhir/R4/search.html#date). So <c>frequency</c>, <c>period</c>, <c>dayOfWeek</c> and the
/// rest of <c>repeat</c> are deliberately not read — a Timing indexes as one row spanning its extent, not
/// as one row per computed occurrence.
/// </para>
/// <para>
/// Those outer limits come from <c>repeat.bounds</c> when it is a Period, and otherwise from the extent of
/// the <c>event</c> list. A <c>boundsDuration</c> or <c>boundsRange</c> is skipped rather than approximated:
/// a duration has no anchor, so placing it on the calendar would require inventing an origin the resource
/// never stated. When such a bound is the only thing present the Timing indexes nothing, which makes it
/// invisible to date search — the honest outcome, since the resource genuinely does not say when it happens.
/// </para>
/// </remarks>
public class TimingToDateTimeSearchValueConverter : FhirElementToSearchValueConverter<DateTimeSearchValue>
{
    private static readonly PeriodToDateTimeSearchValueConverter BoundsConverter = new();

    public TimingToDateTimeSearchValueConverter()
        : base("Timing")
    {
    }

    protected override IEnumerable<ISearchValue> Convert(IElement value)
    {
        IElement bounds = BoundingPeriod(value);

        return bounds != null ? BoundsConverter.ConvertTo(bounds) : EventExtent(value);
    }

    private static IElement BoundingPeriod(IElement timing)
    {
        // ofType(Period) carries the design decision rather than merely navigating: bounds[x] is also
        // allowed to be a Duration or a Range, and neither can be resolved to absolute instants.
        IElement period = timing.Select("repeat.bounds.ofType(Period)").FirstOrDefault();

        // A Period with neither bound would index as [MinValue, MaxValue] and match every date query ever
        // issued, so it is treated as absent and the event list is consulted instead.
        return HasEitherBound(period) ? period : null;
    }

    private static bool HasEitherBound(IElement period)
    {
        return period != null && (period.Scalar("start") != null || period.Scalar("end") != null);
    }

    private static IEnumerable<ISearchValue> EventExtent(IElement timing)
    {
        // event is dateTime, so each entry denotes the span of the precision it was written at, and the
        // outer limits of the list are the earliest lower bound and the latest upper bound across it. A
        // lone "2015-03-09" therefore spans that whole day, exactly as the same literal would in an
        // effectiveDateTime -- the search value's meaning does not change with the element carrying it.
        List<DateTimeSearchValue> occurrences = timing.Select("event")
            .AsStringValues()
            .Select(literal => new DateTimeSearchValue(PartialDateTime.Parse(literal)))
            .ToList();

        if (occurrences.Count == 0) yield break;

        yield return new DateTimeSearchValue(
            new PartialDateTime(occurrences.Min(occurrence => occurrence.Start)),
            new PartialDateTime(occurrences.Max(occurrence => occurrence.End)));
    }
}
