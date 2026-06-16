// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text;
using Bogus;

namespace Ignixa.FhirFakes.EdgeCases.Strategies;

/// <summary>
/// Replaces a free-text value with a very long (multi-KB) ASCII string. FHIR <c>string</c> permits
/// arbitrary length up to the 1 MB implementation cap, so this mutation preserves spec validity.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Random is used for test data generation only")]
public sealed class MaxLengthStringStrategy : StringBoundaryEdgeCaseStrategy
{
    private static readonly string[] Words =
    [
        "Lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "adipiscing", "elit",
        "sed", "do", "eiusmod", "tempor", "incididunt", "ut", "labore", "et", "dolore",
    ];

    private const int TargetLength = 4096;

    /// <inheritdoc />
    public override string Category => "string.max-length";

    /// <inheritdoc />
    public override ValidityIntent Intent => ValidityIntent.PreservesValidity;

    /// <inheritdoc />
    public override MutationResult Apply(MutationTarget target, Randomizer rng)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(rng);
        var value = BuildLongString(rng);
        return new MutationResult(value, $"Replaced free-text with a {value.Length}-character ASCII string");
    }

    private static string BuildLongString(Randomizer rng)
    {
        var sb = new StringBuilder(TargetLength + 32);
        while (sb.Length < TargetLength)
        {
            sb.Append(rng.ArrayElement(Words));
            sb.Append(' ');
        }

        return sb.ToString(0, TargetLength);
    }
}
