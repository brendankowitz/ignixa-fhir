/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Single normalization point between the typed values carried by IElement.Value and the
 * lexical form the FhirPath engine's string-oriented code paths operate on.
 */

using System.Globalization;
using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// Normalizes an <see cref="IElement.Value"/> to the wire lexical form the FhirPath engine's
/// string-oriented paths expect.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IElement.Value"/> returns a <see cref="FhirTemporal"/> for FHIR <c>date</c>,
/// <c>dateTime</c>, <c>instant</c> and <c>time</c> primitives, but FHIRPath <c>@</c>-literals still
/// arrive as raw strings and the engine's arithmetic, conversion and string functions are all written
/// against the lexical form. Every one of those sites previously tested <c>is string</c>, so a typed
/// temporal silently missed the branch and produced an empty collection or an exception.
/// </para>
/// <para>
/// Routing those sites through one function keeps the two representations from diverging again: a new
/// typed value only has to be taught here, not at every <c>is string</c> in the engine. A
/// <see langword="null"/> result means "this value has no lexical form" and callers must treat it
/// exactly as they previously treated a failed <c>is string</c> test.
/// </para>
/// </remarks>
internal static class WireValue
{
    /// <summary>
    /// Returns the wire lexical form of a primitive value, or <see langword="null"/> when the value has none.
    /// </summary>
    /// <param name="value">An <see cref="IElement.Value"/>, a FHIRPath literal, or any other boxed value.</param>
    /// <returns>
    /// The value itself for a <see cref="string"/>, <see cref="FhirTemporal.Literal"/> for a temporal, a
    /// round-trippable ISO 8601 rendering for a <see cref="DateTimeOffset"/> or <see cref="DateTime"/>, and
    /// <see langword="null"/> for everything else.
    /// </returns>
    public static string? AsWireString(object? value)
    {
        return value switch
        {
            string text => text,
            FhirTemporal temporal => temporal.Literal,
            DateTimeOffset offset => offset.ToString("o", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("o", CultureInfo.InvariantCulture),
            _ => null
        };
    }
}
