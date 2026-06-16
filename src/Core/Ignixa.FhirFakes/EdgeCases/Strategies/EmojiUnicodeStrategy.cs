// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using Bogus;

namespace Ignixa.FhirFakes.EdgeCases.Strategies;

/// <summary>Injects emoji (including surrogate pairs and ZWJ sequences) into a free-text value.</summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Random is used for test data generation only")]
public sealed class EmojiUnicodeStrategy : FreeTextEdgeCaseStrategy
{
    private static readonly string[] Emoji =
    [
        "\U0001F600", "\U0001F926‍♀️", "\U0001F468‍\U0001F469‍\U0001F467",
        "\U0001F3F3️‍\U0001F308", "\U0001F44D\U0001F3FD", "\U0001F9EC",
    ];

    /// <inheritdoc />
    public override string Category => "unicode.emoji";

    /// <inheritdoc />
    public override MutationResult Apply(MutationTarget target, Randomizer rng)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(rng);
        var emoji = rng.ArrayElement(Emoji);
        var baseValue = string.IsNullOrEmpty(target.Value) ? "Name" : target.Value;
        var value = $"{baseValue} {emoji}";
        return new MutationResult(value, "Injected emoji into free-text value");
    }
}
