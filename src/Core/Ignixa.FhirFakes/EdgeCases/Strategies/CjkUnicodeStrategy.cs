// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using Bogus;

namespace Ignixa.FhirFakes.EdgeCases.Strategies;

/// <summary>Replaces a free-text value with CJK (Chinese/Japanese/Korean) text.</summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Random is used for test data generation only")]
public sealed class CjkUnicodeStrategy : FreeTextEdgeCaseStrategy
{
    private static readonly string[] Samples =
    [
        "山田太郎", "李明", "김철수", "張偉", "陳美玲", "渡辺さくら", "박지성", "王芳",
    ];

    /// <inheritdoc />
    public override string Category => "unicode.cjk";

    /// <inheritdoc />
    public override MutationResult Apply(MutationTarget target, Randomizer rng)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(rng);
        var value = rng.ArrayElement(Samples);
        return new MutationResult(value, "Replaced free-text with CJK characters");
    }
}
