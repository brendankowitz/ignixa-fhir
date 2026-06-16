// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using Bogus;

namespace Ignixa.FhirFakes.EdgeCases.Strategies;

/// <summary>Sets a date leaf to a far-past but spec-valid date.</summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Random is used for test data generation only")]
public sealed class FarPastTemporalStrategy : TemporalEdgeCaseStrategy
{
    private static readonly string[] FarPastDates =
    [
        "0001-01-01", "0100-06-15", "0500-03-21",
    ];

    /// <inheritdoc />
    public override string Category => "temporal.far-past";

    /// <inheritdoc />
    public override MutationResult Apply(MutationTarget target, Randomizer rng)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(rng);
        var value = rng.ArrayElement(FarPastDates);
        return new MutationResult(value, "Set date to a far-past valid date");
    }
}
