// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using Bogus;

namespace Ignixa.FhirFakes.EdgeCases.Strategies;

/// <summary>Replaces a free-text value with right-to-left (Arabic/Hebrew) text.</summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Random is used for test data generation only")]
public sealed class RtlUnicodeStrategy : FreeTextEdgeCaseStrategy
{
    private static readonly string[] Samples =
    [
        "محمد عبدالله", "فاطمة الزهراء", "أحمد حسن", "דוד כהן", "שרה לוי", "מרים אברהם",
    ];

    /// <inheritdoc />
    public override string Category => "unicode.rtl";

    /// <inheritdoc />
    public override MutationResult Apply(MutationTarget target, Randomizer rng)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(rng);
        var value = rng.ArrayElement(Samples);
        return new MutationResult(value, "Replaced free-text with right-to-left script");
    }
}
