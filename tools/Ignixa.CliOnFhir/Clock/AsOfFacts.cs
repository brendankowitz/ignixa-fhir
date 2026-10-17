// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Clock;

/// <summary>
/// The instant a call's answer was anchored to, as its facts state it: the session's <see cref="AsOf"/>, or, where the call put
/// another value in its place (a view's own constant, a variable), that value and where it came from.
/// </summary>
/// <param name="Instant">The value, as written.</param>
/// <param name="Source">One of <see cref="AsOfSources"/>.</param>
public sealed record AsOfFacts(string Instant, string Source)
{
    public static AsOfFacts Of(AsOf asOf)
    {
        ArgumentNullException.ThrowIfNull(asOf);

        return new AsOfFacts(asOf.Instant, asOf.Source);
    }
}
