// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Frozen;
using System.Diagnostics;
using System.Globalization;
using Ignixa.Abstractions;
using P = Hl7.Fhir.ElementModel.Types;

namespace Ignixa.Extensions.FirelySdk;

/// <summary>
/// Translates primitive values across the adapter boundary so that each adapter presents the
/// value contract its own SDK expects.
/// </summary>
/// <remarks>
/// <para>
/// The two SDKs disagree on how a primitive value is represented. Firely surfaces the temporal
/// primitives as <c>Hl7.Fhir.ElementModel.Types</c> instances - <c>date</c> becomes a
/// <see cref="P.Date"/>, <c>dateTime</c>/<c>instant</c> a <see cref="P.DateTime"/>, and
/// <c>time</c> a <see cref="P.Time"/>. Ignixa carries those as FHIR wire-format strings
/// (see <c>SchemaAwareElement.Value</c>). Both agree on <c>boolean</c>, <c>integer</c>,
/// <c>decimal</c>, and the string-backed types.
/// </para>
/// <para>
/// Passing a value straight through therefore hands the receiving engine a type it does not
/// recognise. That is not merely untidy: Ignixa's FHIRPath comparison helpers narrow their
/// operands through a <c>string</c>/<c>DateTime</c>/<c>DateTimeOffset</c> switch that falls
/// through to <c>null</c> for anything else, so a Firely <see cref="P.DateTime"/> makes any
/// <c>date</c>/<c>dateTime</c> comparison yield an empty collection instead of a boolean -
/// silently, and in a position where FHIRPath's empty-propagation hides it.
/// </para>
/// <para>
/// <c>integer64</c> is translated in the Ignixa-to-Firely direction only. Firely reports it as a
/// <see cref="long"/>, which Ignixa's evaluator already treats as a first-class numeric type
/// alongside <c>int</c> and <c>decimal</c>, so converting it to a string on the way in would
/// downgrade numeric comparisons to lexical ones. Note that this leaves Ignixa's own two element
/// implementations disagreeing - <c>SchemaAwareElement</c> has no <c>integer64</c> arm and yields
/// a string - which is a pre-existing gap in <c>Ignixa.Serialization</c>, not one this shim can
/// close.
/// </para>
/// <para>
/// Only the primitives the two SDKs disagree about are translated. The FHIRPath system types
/// Firely's own evaluator can surface - <c>P.Quantity</c>, <c>P.Code</c>, <c>P.Concept</c> - are
/// not, because their Ignixa counterparts live in <c>Ignixa.FhirPath</c>, which this interop shim
/// deliberately does not reference. Such a value reaching Ignixa hits the same silent-empty
/// behaviour described above; the cases are pinned in <c>FirelyPrimitiveValueContractTests</c>.
/// </para>
/// </remarks>
internal static class FirelyPrimitiveValues
{
    /// <summary>
    /// The FHIR primitive types whose representation differs between the two SDKs, mapped to the
    /// conversion each needs.
    /// </summary>
    /// <remarks>
    /// Keyed case-insensitively because <c>IElement.InstanceType</c> is populated from several
    /// sources - schema lookups, FHIRPath-synthesised elements, third-party implementations - and
    /// Ignixa's own evaluator lower-cases it before comparing. A casing mismatch here would not
    /// fail loudly; it would silently skip the translation.
    /// </remarks>
    private static readonly FrozenDictionary<string, PrimitiveKind> TranslatedTypes =
        new Dictionary<string, PrimitiveKind>(StringComparer.OrdinalIgnoreCase)
        {
            [FhirTypeConstants.Date] = PrimitiveKind.Date,
            [FhirTypeConstants.DateTime] = PrimitiveKind.DateTime,
            [FhirTypeConstants.Instant] = PrimitiveKind.DateTime,
            [FhirTypeConstants.Time] = PrimitiveKind.Time,
            [FhirTypeConstants.Integer64] = PrimitiveKind.Integer64,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private enum PrimitiveKind
    {
        Date,
        DateTime,
        Time,
        Integer64,
    }

    /// <summary>
    /// Converts a value read from a Firely <c>ITypedElement</c> into the representation Ignixa uses.
    /// </summary>
    /// <param name="value">The value as Firely reports it.</param>
    /// <returns>The equivalent value in Ignixa's representation.</returns>
    /// <remarks>
    /// The temporal types round-trip through their FHIR wire format, which is what
    /// <c>ToString()</c> returns: Firely preserves the originally parsed string when it has one,
    /// and otherwise renders at the precision the value actually carries, so a year-only
    /// <c>dateTime</c> stays year-only rather than being widened to a full timestamp.
    /// </remarks>
    public static object? ToIgnixa(object? value) => value switch
    {
        P.DateTime dateTime => dateTime.ToString(),
        P.Date date => date.ToString(),
        P.Time time => time.ToString(),
        _ => value,
    };

    /// <summary>
    /// Converts a value read from an Ignixa <c>IElement</c> into the representation Firely uses.
    /// </summary>
    /// <param name="value">The value as Ignixa reports it.</param>
    /// <param name="instanceType">The FHIR type name of the element the value came from.</param>
    /// <returns>The equivalent value in Firely's representation.</returns>
    /// <remarks>
    /// Ignixa reports the temporal primitives as strings, so the FHIR type name is what tells us
    /// which of them to build. A value that does not parse is returned unchanged, which is what
    /// the adapter did before any translation existed: navigation over a malformed resource stays
    /// possible, and the malformed text reaches Firely exactly as it would have. What Firely then
    /// does with it depends on the consumer - one that coerces it to a temporal throws, while
    /// navigation, <c>toString()</c> and serialization see the raw string. This method neither
    /// introduces nor suppresses either outcome.
    /// </remarks>
    public static object? ToFirely(object? value, string? instanceType)
    {
        if (value is null || instanceType is null || !TranslatedTypes.TryGetValue(instanceType, out var kind))
        {
            return value;
        }

        return kind switch
        {
            PrimitiveKind.Date => ToFirelyDate(value),
            PrimitiveKind.DateTime => ToFirelyDateTime(value),
            PrimitiveKind.Time => ToFirelyTime(value),
            PrimitiveKind.Integer64 => ToFirelyInteger64(value),
            _ => throw new UnreachableException($"Unhandled {nameof(PrimitiveKind)}: {kind}."),
        };
    }

    /// <remarks>
    /// A FHIR <c>date</c> has no time and no offset, so the offset of a supplied instant is
    /// deliberately dropped rather than folded into the result.
    /// </remarks>
    private static object ToFirelyDate(object value) => value switch
    {
        string text => P.Date.TryParse(text, out var parsedText) ? parsedText : value,
        DateTimeOffset dto => P.Date.FromDateTimeOffset(dto, P.DateTimePrecision.Day, includeOffset: false),
        DateTime dt => P.Date.FromDateTimeOffset(AsOffset(dt), P.DateTimePrecision.Day, includeOffset: false),
        _ => value,
    };

    /// <remarks>
    /// The offset is part of a FHIR <c>dateTime</c>, so a bare <see cref="DateTime"/> is rendered
    /// through its round-trip format rather than being given an arbitrary one: that emits no
    /// offset for <see cref="DateTimeKind.Unspecified"/>, <c>Z</c> for
    /// <see cref="DateTimeKind.Utc"/>, and the host machine's offset for
    /// <see cref="DateTimeKind.Local"/>, matching how Ignixa's own evaluator normalises the same
    /// value. The <see cref="DateTimeKind.Local"/> result is therefore host-dependent, which is
    /// inherent to the input: a local <see cref="DateTime"/> has no meaning without one.
    /// </remarks>
    private static object ToFirelyDateTime(object value) => value switch
    {
        string text => P.DateTime.TryParse(text, out var parsedText) ? parsedText : value,
        DateTimeOffset dto => P.DateTime.FromDateTimeOffset(dto, P.DateTimePrecision.Fraction, includeOffset: true),
        DateTime dt => P.DateTime.TryParse(dt.ToString("o", CultureInfo.InvariantCulture), out var parsedDateTime)
            ? parsedDateTime
            : value,
        _ => value,
    };

    /// <remarks>
    /// A FHIR <c>time</c> is a wall-clock time, so where an offset has to be discarded to build
    /// one - the instant-shaped arms below - it is dropped rather than folded into the result.
    /// The string arm does not strip one: FHIR's <c>time</c> grammar forbids an offset, so a
    /// string carrying one is already non-conformant, and preserving what was actually written is
    /// more useful to a validator than silently normalising it away.
    /// </remarks>
    private static object ToFirelyTime(object value) => value switch
    {
        string text => P.Time.TryParse(text, out var parsedText) ? parsedText : value,
        DateTimeOffset dto => P.Time.FromDateTimeOffset(dto, P.DateTimePrecision.Fraction, includeOffset: false),
        DateTime dt => P.Time.FromDateTimeOffset(AsOffset(dt), P.DateTimePrecision.Fraction, includeOffset: false),
        _ => value,
    };

    /// <remarks>
    /// <c>AllowLeadingSign</c> rather than the default <see cref="NumberStyles.Integer"/>, which
    /// also accepts surrounding whitespace that FHIR's <c>integer64</c> grammar does not permit.
    /// This is not a grammar check: FHIR's <c>[0]|[-+]?[1-9][0-9]*</c> also forbids leading zeros,
    /// which <see cref="long.TryParse(string, NumberStyles, IFormatProvider, out long)"/> accepts
    /// and canonicalises away, so <c>"007"</c> crosses as <c>7</c>. That matches how
    /// <c>SchemaAwareElement</c> already parses the narrower integer types; validating the literal
    /// belongs in the validator, not here. A value outside <see cref="long"/>'s range fails the
    /// parse and is returned unchanged, same as any other unparseable input.
    /// </remarks>
    private static object ToFirelyInteger64(object value) => value switch
    {
        string text => long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsedText)
            ? parsedText
            : value,
        _ => value,
    };

    /// <summary>
    /// Widens a <see cref="DateTime"/> to a <see cref="DateTimeOffset"/> without consulting the
    /// host's time zone, for the cases where the offset is discarded anyway.
    /// </summary>
    private static DateTimeOffset AsOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), TimeSpan.Zero);
}
