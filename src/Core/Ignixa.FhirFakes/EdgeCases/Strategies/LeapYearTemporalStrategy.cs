// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using Bogus;

namespace Ignixa.FhirFakes.EdgeCases.Strategies;

/// <summary>Sets a date leaf to Feb 29 of a leap year.</summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Random is used for test data generation only")]
public sealed class LeapYearTemporalStrategy : TemporalEdgeCaseStrategy
{
    private static readonly string[] LeapDates =
    [
        "2024-02-29", "2020-02-29", "2000-02-29", "1996-02-29",
    ];

    /// <inheritdoc />
    public override string Category => "temporal.leap-year";

    /// <inheritdoc />
    public override MutationResult Apply(MutationTarget target, Randomizer rng)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(rng);
        var value = rng.ArrayElement(LeapDates);
        return new MutationResult(value, "Set date to Feb 29 of a leap year");
    }
}
