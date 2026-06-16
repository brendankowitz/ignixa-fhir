// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text;
using Bogus;

namespace Ignixa.FhirFakes.EdgeCases.Strategies;

/// <summary>Replaces a free-text value with a long string mixing Latin, CJK, RTL, Cyrillic and emoji.</summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Random is used for test data generation only")]
public sealed class MultiScriptLongUnicodeStrategy : FreeTextEdgeCaseStrategy
{
    private static readonly string[] Fragments =
    [
        "Latin", "山田", "محمد", "Привет", "\U0001F600", "Ελληνικά", "한국어", "עברית",
    ];

    /// <inheritdoc />
    public override string Category => "unicode.multi-script-long";

    /// <inheritdoc />
    public override MutationResult Apply(MutationTarget target, Randomizer rng)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(rng);
        var value = BuildLongString(rng);
        return new MutationResult(value, "Replaced free-text with long multi-script string");
    }

    private static string BuildLongString(Randomizer rng)
    {
        var count = rng.Int(20, 40);
        var builder = new StringBuilder(count * 8);
        for (var i = 0; i < count; i++)
        {
            builder.Append(rng.ArrayElement(Fragments));
            builder.Append(' ');
        }

        return builder.ToString().TrimEnd();
    }
}
