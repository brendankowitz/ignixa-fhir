// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Clock;

/// <summary>
/// The point in time a call's answers are anchored to: the instant the data was pulled, or the one the caller named, or the
/// session's clock. Defaults that mean "now" (a draft's <c>authoredOn</c>, a view's <c>%asOf</c>) read it, never the wall clock.
/// </summary>
/// <param name="Moment">The instant, with the offset it was given in (UTC for the clock).</param>
/// <param name="Source">One of <see cref="AsOfSources"/>.</param>
public sealed record AsOf([property: JsonIgnore] DateTimeOffset Moment, string Source)
{
    /// <summary>The instant as a FHIR instant (<see cref="FhirInstant.Format"/>).</summary>
    [JsonPropertyOrder(-1)]
    public string Instant => FhirInstant.Format(Moment);
}
