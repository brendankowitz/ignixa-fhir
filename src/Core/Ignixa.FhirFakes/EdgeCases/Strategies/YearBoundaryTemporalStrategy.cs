// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using Bogus;

namespace Ignixa.FhirFakes.EdgeCases.Strategies;

/// <summary>Sets a date leaf to a year boundary (Dec 31 or Jan 1).</summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Random is used for test data generation only")]
public sealed class YearBoundaryTemporalStrategy : TemporalEdgeCaseStrategy
{
    private static readonly string[] BoundaryDates =
    [
        "2023-12-31", "2024-01-01", "1999-12-31", "2000-01-01",
    ];

    /// <inheritdoc />
    public override string Category => "temporal.year-boundary";

    /// <inheritdoc />
    public override MutationResult Apply(MutationTarget target, Randomizer rng)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(rng);
        var value = rng.ArrayElement(BoundaryDates);
        return new MutationResult(value, "Set date to a year boundary (Dec 31 / Jan 1)");
    }
}
