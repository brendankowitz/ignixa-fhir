// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using Bogus;

namespace Ignixa.FhirFakes.EdgeCases.Strategies;

/// <summary>
/// Sets a free-text value to whitespace only (spaces and tabs). Valid per the FHIR string grammar
/// but violates best-practice constraints that require meaningful content — this mutation is
/// expected to produce resources that may fail profile validation.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Random is used for test data generation only")]
public sealed class WhitespaceOnlyStringStrategy : StringBoundaryEdgeCaseStrategy
{
    private static readonly string[] Samples =
    [
        "   ",
        "\t\t",
        "  \t  ",
        "       ",
    ];

    /// <inheritdoc />
    public override string Category => "string.whitespace-only";

    /// <inheritdoc />
    public override ValidityIntent Intent => ValidityIntent.MayViolate;

    /// <inheritdoc />
    public override MutationResult Apply(MutationTarget target, Randomizer rng)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(rng);
        var value = rng.ArrayElement(Samples);
        return new MutationResult(value, "Set free-text value to whitespace only");
    }
}
