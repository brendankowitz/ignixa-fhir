// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Encodings.Web;
using System.Text.Json;
using Ignixa.CliOnFhir.Catalog;

namespace Ignixa.CliOnFhir.Contracts;

/// <summary>
/// A search query as a response repeats it (in facts, and in <c>next</c>): whole up to <see cref="MaxLength"/> bytes as the
/// envelope writes it (escaped: <c>é</c> and <c>&lt;</c> are six bytes each); a longer one is cut there, with an <c>omitted</c>
/// item giving its length and digest, and is never repeated in <c>next</c>, so a huge query cannot push a response past the
/// size cap whatever its characters.
/// </summary>
internal static class QueryEcho
{
    /// <summary>The longest query repeated whole, in bytes as the envelope writes it.</summary>
    public const int MaxLength = 2048;

    // The envelope's encoder (System.Text.Json's default), by which a text is measured as it is written.
    private static readonly JavaScriptEncoder Encoder = EnvelopeJson.Compact.Encoder ?? JavaScriptEncoder.Default;

    // What each printable ASCII character takes as written: 1, or more for one the encoder escapes (", \, <, >, &, ', +, `).
    private static readonly int[] AsciiCost = [.. Enumerable.Range(' ', 0x7F - ' ').Select(code => JsonEncodedText.Encode(((char)code).ToString(), Encoder).EncodedUtf8Bytes.Length)];

    /// <summary>Whether <paramref name="query"/> is too long to repeat whole.</summary>
    public static bool IsCut(string? query) => query is not null && Prefix(query, MaxLength) < query.Length;

    /// <summary>The bytes <paramref name="text"/> takes as the envelope writes it, quotes excluded.</summary>
    public static int SerializedLength(string? text) => text is null ? 0 : JsonEncodedText.Encode(text, Encoder).EncodedUtf8Bytes.Length;

    /// <summary><paramref name="query"/> as the response repeats it: whole, or as much of its start as takes <see cref="MaxLength"/> bytes, and <c>…</c>.</summary>
    public static string Of(string query)
    {
        ArgumentNullException.ThrowIfNull(query);

        int length = Prefix(query, MaxLength);
        return length == query.Length ? query : query[..length] + "…";
    }

    // How many characters of text, from its start, take at most bytes as the envelope writes them; a surrogate pair is never split.
    private static int Prefix(string text, int bytes)
    {
        if (text.Length * 6 <= bytes)
        {
            return text.Length;
        }

        int used = 0;
        int index = 0;
        while (index < text.Length)
        {
            int width = char.IsHighSurrogate(text[index]) && index + 1 < text.Length ? 2 : 1;
            char first = text[index];
            int cost = width == 1 && first is >= ' ' and < (char)0x7F
                ? AsciiCost[first - ' ']
                : JsonEncodedText.Encode(text.AsSpan(index, width), Encoder).EncodedUtf8Bytes.Length;
            if (used + cost > bytes)
            {
                return index;
            }

            used += cost;
            index += width;
        }

        return index;
    }


    /// <summary>The longest argument value an issue message repeats whole (<see cref="Value"/>).</summary>
    public const int MaxValueLength = 100;

    /// <summary>
    /// An argument value as an issue message repeats it: whole when it is at most <see cref="MaxValueLength"/> characters, else its
    /// start, its length and its digest, so a message about a value never grows with it.
    /// </summary>
    public static string Value(string? value)
    {
        if (value is null || value.Length <= MaxValueLength)
        {
            return value ?? string.Empty;
        }

        int length = char.IsHighSurrogate(value[(MaxValueLength / 2) - 1]) ? (MaxValueLength / 2) - 1 : MaxValueLength / 2;
        return $"{value[..length]}… ({value.Length} characters, digest {FhirIds.Digest(value)})";
    }

    /// <summary>The <c>omitted</c> item for a cut echo of <paramref name="query"/> at <paramref name="where"/> (a facts path).</summary>
    public static OmittedItem Cut(string query, string where)
    {
        ArgumentNullException.ThrowIfNull(query);

        return new OmittedItem($"{where} past its first {MaxLength} bytes as written (the query is {query.Length} characters; digest {FhirIds.Digest(query)})", "size-budget");
    }

    /// <summary>The <c>omitted</c> item for a <c>next</c> call left out because it would repeat a query too long to repeat.</summary>
    public static OmittedItem NextLeftOut(string what) =>
        new($"next: {what} (it would repeat a query longer than {MaxLength} bytes as written; send the same request again with the changed arguments)", "size-budget");
}
