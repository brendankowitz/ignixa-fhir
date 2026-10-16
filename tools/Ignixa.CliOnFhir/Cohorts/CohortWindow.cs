// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Cohorts;

/// <summary>When an event must have happened: after <see cref="After"/> (exclusive) and at or before <see cref="Before"/>.</summary>
/// <param name="After">The exclusive lower bound; null for no lower bound.</param>
/// <param name="Before">The inclusive upper bound; <c>asOf</c> unless the request says otherwise.</param>
internal sealed record CohortWindow(DateTimeOffset? After, DateTimeOffset Before)
{
    public bool Contains(DateTimeOffset instant) => (After is not { } after || instant > after) && instant <= Before;

    /// <summary>The window as the step rules show it, for example <c>(2025-08-17, 2026-08-17]</c>.</summary>
    public string Describe() => $"({(After is { } after ? CohortInstant.Format(after) : "any")}, {CohortInstant.Format(Before)}]";
}
