// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text;
using Bogus;

namespace Ignixa.FhirFakes.EdgeCases.Strategies;

/// <summary>Appends combining diacritical marks to base characters in a free-text value.</summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Random is used for test data generation only")]
public sealed class CombiningUnicodeStrategy : FreeTextEdgeCaseStrategy
{
    private static readonly char[] CombiningMarks =
    [
        '́', '̀', '̂', '̃', '̈', '̧', '̣',
    ];

    /// <inheritdoc />
    public override string Category => "unicode.combining";

    /// <inheritdoc />
    public override MutationResult Apply(MutationTarget target, Randomizer rng)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(rng);
        var seed = string.IsNullOrEmpty(target.Value) ? "Zalgo" : target.Value;
        var value = DecorateWithMarks(seed, rng);
        return new MutationResult(value, "Added combining diacritical marks to base characters");
    }

    private static string DecorateWithMarks(string seed, Randomizer rng)
    {
        var builder = new StringBuilder(seed.Length * 3);
        foreach (var ch in seed)
        {
            builder.Append(ch);
            AppendMarks(builder, rng);
        }

        return builder.ToString();
    }

    private static void AppendMarks(StringBuilder builder, Randomizer rng)
    {
        var count = rng.Int(1, 3);
        for (var i = 0; i < count; i++)
        {
            builder.Append(rng.ArrayElement(CombiningMarks));
        }
    }
}
