// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Globalization;
using System.Text;
using System.Text.Json;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.Search.Serialization;

/// <summary>
/// Reads search values written by <see cref="CompactSearchValueWriter"/>, rebuilding each exactly as it was indexed.
/// </summary>
/// <remarks>
/// Reading is strict: an unknown kind or property, a missing required field, a field of the wrong JSON type, or a value
/// its type's constructor rejects throws <see cref="JsonException"/> (with the cause as its inner exception), so a
/// caller can tell a store it cannot read from one with nothing in it.
/// </remarks>
public static class CompactSearchValueReader
{
    /// <summary>Reads one search value object from <paramref name="element"/>.</summary>
    /// <exception cref="JsonException">The element is not a search value this format wrote.</exception>
    public static ISearchValue ReadSearchValue(JsonElement element)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(element.GetRawText()));
        reader.Read();
        return Read(ref reader);
    }

    /// <summary>
    /// Reads one search value object; <paramref name="reader"/> must be on its <see cref="JsonTokenType.StartObject"/>
    /// and is left on its <see cref="JsonTokenType.EndObject"/>.
    /// </summary>
    /// <exception cref="JsonException">The object is not a search value this format wrote.</exception>
    public static ISearchValue Read(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected a search value object but found {reader.TokenType}.");
        }

        // Wrong token types and constructor invariants (a token with nothing in it, an empty composite) surface as
        // ArgumentException, InvalidOperationException or FormatException; they all mean the input is not what was written.
        try
        {
            var fields = new CompactSearchValueFields();
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                string name = reader.GetString()!;
                reader.Read();
                fields.Set(name, ref reader);
            }

            if (reader.TokenType != JsonTokenType.EndObject)
            {
                throw new JsonException("The search value object is not terminated.");
            }

            return fields.Build();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new JsonException($"Invalid search value: {ex.Message}", ex);
        }
    }

    /// <summary>What a <see cref="DateTimeSearchValue"/> not built from one partial date prints as.</summary>
    internal static string DefaultDateText(DateTimeOffset start, DateTimeOffset end) =>
        $"{start.ToString("o", CultureInfo.InvariantCulture)}-{end.ToString("o", CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Mirrors what <see cref="StringSearchValue"/>'s constructor undoes: the writer stored the already-unescaped text,
    /// so it is escaped again before being handed back.
    /// </summary>
    internal static string EscapeString(string text) => text.EscapeSearchParameterValue();
}
