// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text;
using Bogus;

namespace Ignixa.FhirFakes.EdgeCases.Strategies;

/// <summary>
/// Injects C0 control characters (U+0000-U+001F, excluding \r \n \t) into a free-text value.
/// FHIR <c>string</c> grammar is <c>[\r\n\t -\uFFFF]+</c>, so these characters are not
/// permitted - this mutation is expected to violate the spec and produce invalid resources.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Random is used for test data generation only")]
public sealed class ControlCharsStringStrategy : StringBoundaryEdgeCaseStrategy
{
    private static readonly char[] ControlChars =
    [
        '\u0000', '\u0001', '\u0002', '\u001b', '\u0007', '\u0008', '\u000b', '\u000c',
    ];

    /// <inheritdoc />
    public override string Category => "string.control-chars";

    /// <inheritdoc />
    public override ValidityIntent Intent => ValidityIntent.MayViolate;

    /// <inheritdoc />
    public override MutationResult Apply(MutationTarget target, Randomizer rng)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(rng);
        var seed = string.IsNullOrEmpty(target.Value) ? "Name" : target.Value;
        var value = InjectControlChar(seed, rng);
        return new MutationResult(value, "Injected C0 control character into free-text value");
    }

    private static string InjectControlChar(string seed, Randomizer rng)
    {
        var mid = seed.Length / 2;
        var ctrl = rng.ArrayElement(ControlChars);
        return new StringBuilder(seed.Length + 1)
            .Append(seed, 0, mid)
            .Append(ctrl)
            .Append(seed, mid, seed.Length - mid)
            .ToString();
    }
}
