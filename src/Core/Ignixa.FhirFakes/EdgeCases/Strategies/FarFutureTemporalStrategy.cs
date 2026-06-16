// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using Bogus;

namespace Ignixa.FhirFakes.EdgeCases.Strategies;

/// <summary>Sets a date leaf to a far-future but spec-valid date.</summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Random is used for test data generation only")]
public sealed class FarFutureTemporalStrategy : TemporalEdgeCaseStrategy
{
    private static readonly string[] FarFutureDates =
    [
        "9999-12-31", "9999-01-01", "2999-06-15",
    ];

    /// <inheritdoc />
    public override string Category => "temporal.far-future";

    /// <inheritdoc />
    public override MutationResult Apply(MutationTarget target, Randomizer rng)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(rng);
        var value = rng.ArrayElement(FarFutureDates);
        return new MutationResult(value, "Set date to a far-future valid date");
    }
}
