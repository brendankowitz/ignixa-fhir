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
/// <param name="Precision"><see cref="AsOf.DatePrecision"/> when the session's value was given as a date (<see cref="AsOf.Precision"/>); absent otherwise.</param>
public sealed record AsOfFacts(
    string? Instant,
    string Source,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Digest = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AsOfDateFacts? Date = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Precision = null)
{
    public static AsOfFacts Of(AsOf asOf)
    {
        ArgumentNullException.ThrowIfNull(asOf);

        return new AsOfFacts(asOf.Instant, asOf.Source, Precision: asOf.Precision);
    }
}
