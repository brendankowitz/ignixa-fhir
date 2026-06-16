// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text;
using Bogus;

namespace Ignixa.FhirFakes.EdgeCases.Strategies;

/// <summary>Injects zero-width characters (U+200B, U+200C, U+200D, U+FEFF) into a free-text value.</summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Random is used for test data generation only")]
public sealed class ZeroWidthUnicodeStrategy : FreeTextEdgeCaseStrategy
{
    private static readonly char[] ZeroWidthChars =
    [
        '​', '‌', '‍', '﻿',
    ];

    /// <inheritdoc />
    public override string Category => "unicode.zero-width";

    /// <inheritdoc />
    public override MutationResult Apply(MutationTarget target, Randomizer rng)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(rng);
        var seed = string.IsNullOrEmpty(target.Value) ? "Name" : target.Value;
        var value = InjectZeroWidth(seed, rng);
        return new MutationResult(value, "Injected zero-width characters between code points");
    }

    private static string InjectZeroWidth(string seed, Randomizer rng)
    {
        var builder = new StringBuilder(seed.Length * 2);
        foreach (var ch in seed)
        {
            builder.Append(ch);
            builder.Append(rng.ArrayElement(ZeroWidthChars));
        }

        return builder.ToString();
    }
}
