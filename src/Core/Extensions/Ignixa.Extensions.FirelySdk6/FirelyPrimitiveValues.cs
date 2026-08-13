// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using P = Hl7.Fhir.ElementModel.Types;

namespace Ignixa.Extensions.FirelySdk;

/// <summary>
/// Translates primitive values across the adapter boundary so that each adapter presents the
/// value contract its own SDK expects.
/// </summary>
/// <remarks>
/// <para>
/// The two SDKs disagree on how a primitive value is represented. Firely surfaces the temporal
/// primitives as <c>Hl7.Fhir.ElementModel.Types</c> instances — <c>date</c> becomes a
/// <see cref="P.Date"/>, <c>dateTime</c>/<c>instant</c> a <see cref="P.DateTime"/>, and
/// <c>time</c> a <see cref="P.Time"/>. Ignixa carries the same values as FHIR wire-format
/// strings (see <c>SchemaAwareElement.Value</c>). Both agree on <c>boolean</c>, the integer
/// family, <c>decimal</c>, and the string-backed types.
/// </para>
/// <para>
/// Passing a value straight through therefore hands the receiving engine a type it does not
/// recognise. That is not merely untidy: Ignixa's FHIRPath comparison helpers narrow their
/// operands through a <c>string</c>/<c>DateTime</c>/<c>DateTimeOffset</c> switch that falls
/// through to <c>null</c> for anything else, so a Firely <see cref="P.DateTime"/> makes every
/// date comparison yield an empty collection instead of a boolean — silently, and in a position
/// where FHIRPath's empty-propagation hides it.
/// </para>
/// </remarks>
internal static class FirelyPrimitiveValues
{
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
    /// which of them to build. An unparseable value is surfaced as its raw text rather than
    /// throwing, matching how Firely's own <c>PocoElementNode</c> degrades on malformed input —
    /// navigation over a bad resource should stay possible.
    /// </remarks>
    public static object? ToFirely(object? value, string? instanceType)
    {
        if (value is null || instanceType is null)
        {
            return value;
        }

        try
        {
            return (instanceType, value) switch
            {
                ("date", string text) => P.Date.Parse(text),
                ("dateTime" or "instant", string text) => P.DateTime.Parse(text),
                ("time", string text) => P.Time.Parse(text),
                ("integer64", string text) => long.Parse(text, CultureInfo.InvariantCulture),

                // IElement.Value also permits DateTimeOffset for the temporal types.
                ("date", DateTimeOffset dto) => P.Date.FromDateTimeOffset(dto),
                ("dateTime" or "instant", DateTimeOffset dto) => P.DateTime.FromDateTimeOffset(dto),

                _ => value,
            };
        }
        catch (FormatException)
        {
            return value;
        }
        catch (OverflowException)
        {
            return value;
        }
    }
}
