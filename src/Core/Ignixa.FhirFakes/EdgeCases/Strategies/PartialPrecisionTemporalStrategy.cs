// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using Bogus;

namespace Ignixa.FhirFakes.EdgeCases.Strategies;

/// <summary>Reduces a date leaf to partial precision (yyyy or yyyy-MM), both valid FHIR date forms.</summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Random is used for test data generation only")]
public sealed class PartialPrecisionTemporalStrategy : TemporalEdgeCaseStrategy
{
    /// <inheritdoc />
    public override string Category => "temporal.partial-precision";

    /// <inheritdoc />
    public override MutationResult Apply(MutationTarget target, Randomizer rng)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(rng);

        var year = target.Value.Length >= 4 ? target.Value[..4] : "2000";
        var reduceToYearOnly = rng.Bool();
        var value = reduceToYearOnly ? year : ReduceToYearMonth(target.Value, year);
        return new MutationResult(value, "Reduced date to partial precision (yyyy or yyyy-MM)");
    }

    private static string ReduceToYearMonth(string original, string year)
    {
        if (original.Length >= 7 && original[4] == '-')
        {
            return original[..7];
        }

        return $"{year}-01";
    }
}
