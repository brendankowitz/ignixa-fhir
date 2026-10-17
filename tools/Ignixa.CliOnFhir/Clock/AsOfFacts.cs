// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Clock;

/// <summary>
/// The instant a call's answer was anchored to, as its facts state it: the session's <see cref="AsOf"/>, or, where the call put
/// another value in its place (a view's own constant, a variable), that value and where it came from.
/// </summary>
/// <param name="Instant">The value as written when it is a FHIR instant; null when it is not (a view's own constant can hold anything).</param>
/// <param name="Source">One of <see cref="AsOfSources"/>.</param>
/// <param name="Digest">When the value is not an instant, its digest (it can be the folder's text); absent otherwise.</param>
/// <param name="Date">For a view, what it read as <c>%asOfDate</c>.</param>
/// <param name="Precision">The session's value's <see cref="AsOf.Precision"/> when it is not an instant (<c>year</c>, <c>month</c>, <c>date</c>); absent otherwise.</param>
/// <param name="ZoneAssumed"><c>true</c> when the session's value was a date and time without a zone, read as UTC; absent otherwise.</param>
public sealed record AsOfFacts(
    string? Instant,
    string Source,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Digest = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AsOfDateFacts? Date = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Precision = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? ZoneAssumed = null)
{
    public static AsOfFacts Of(AsOf asOf)
    {
        ArgumentNullException.ThrowIfNull(asOf);

        return new AsOfFacts(asOf.Instant, asOf.Source, Precision: asOf.ShownPrecision, ZoneAssumed: asOf.ShownZoneAssumed);
    }
}
