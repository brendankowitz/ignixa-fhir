/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Canonicalises the two engines' type names so the sweep reports behaviour, not spelling.
 */

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// Reduces an <c>InstanceType</c> to a form both engines spell the same way.
/// </summary>
/// <remarks>
/// <para>
/// The engines disagree systematically about what to call the result of an operator: Firely uses the
/// FHIRPath system namespace (<c>System.Boolean</c>, <c>System.Date</c>, <c>System.Quantity</c>)
/// while Ignixa uses the FHIR primitive name (<c>boolean</c>, <c>date</c>, <c>Quantity</c>). That is
/// one finding, and it is pinned by a dedicated test in
/// <see cref="FirelyVersusIgnixaDifferentialTests"/> rather than being allowed to restate itself as
/// 158 separate rows that push the behavioural divergences off the end of the report.
/// </para>
/// <para>
/// The rule is deliberately narrow - drop a leading <c>System.</c>, then compare case-insensitively.
/// It cannot mask a genuine type divergence: <c>BackboneElement</c> against
/// <c>Observation.Component</c>, or <c>string</c> against <c>code</c>, still differ under it.
/// </para>
/// </remarks>
internal static class ParityTypeName
{
    private const string SystemPrefix = "System.";

    public static string Canonical(string? instanceType)
    {
        if (string.IsNullOrEmpty(instanceType))
        {
            return "<untyped>";
        }

        var name = instanceType.StartsWith(SystemPrefix, StringComparison.Ordinal)
            ? instanceType[SystemPrefix.Length..]
            : instanceType;

        return name.ToUpperInvariant();
    }
}
