// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace Ignixa.CliOnFhir.Catalog;

/// <summary>
/// Reads only the top-level <c>resourceType</c> and <c>id</c> of a JSON resource, skipping every
/// other value, so a folder can be catalogued without building a document per resource.
/// </summary>
internal static class ResourceSniffer
{
    private static readonly JsonReaderOptions ReaderOptions = new() { CommentHandling = JsonCommentHandling.Skip };

    /// <summary>
    /// Returns the top-level <c>resourceType</c> and <c>id</c>; either may be null when absent.
    /// </summary>
    /// <exception cref="JsonException">The input is not a well-formed JSON object.</exception>
    public static (string? ResourceType, string? Id) Sniff(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json, ReaderOptions);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected a JSON object.");
        }

        string? resourceType = null;
        string? id = null;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            bool isResourceType = reader.ValueTextEquals("resourceType"u8);
            bool isId = !isResourceType && reader.ValueTextEquals("id"u8);
            reader.Read();

            if (isResourceType && reader.TokenType == JsonTokenType.String)
            {
                resourceType = reader.GetString();
            }
            else if (isId && reader.TokenType == JsonTokenType.String)
            {
                id = reader.GetString();
            }
            else
            {
                reader.Skip();
            }
        }

        return (resourceType, id);
    }

    /// <summary>
    /// Returns the byte range of every <c>entry[].resource</c> in a Bundle, relative to the start of
    /// <paramref name="bundle"/>, with the entry's <c>fullUrl</c> (null when it has none). Entries without a
    /// <c>resource</c> are not returned.
    /// </summary>
    /// <exception cref="JsonException">The input is not well-formed JSON.</exception>
    public static IReadOnlyList<(int Offset, int Length, string? FullUrl)> LocateBundleEntries(ReadOnlySpan<byte> bundle)
    {
        var ranges = new List<(int Offset, int Length, string? FullUrl)>();
        var reader = new Utf8JsonReader(bundle, ReaderOptions);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected a JSON object.");
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            bool isEntry = reader.ValueTextEquals("entry"u8);
            reader.Read();
            if (isEntry && reader.TokenType == JsonTokenType.StartArray)
            {
                ReadEntries(ref reader, ranges);
            }
            else
            {
                reader.Skip();
            }
        }

        return ranges;
    }

    private static void ReadEntries(ref Utf8JsonReader reader, List<(int Offset, int Length, string? FullUrl)> ranges)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            (int Offset, int Length)? resource = null;
            string? fullUrl = null;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                bool isResource = reader.ValueTextEquals("resource"u8);
                bool isFullUrl = !isResource && reader.ValueTextEquals("fullUrl"u8);
                reader.Read();
                if (isResource && reader.TokenType == JsonTokenType.StartObject)
                {
                    int start = (int)reader.TokenStartIndex;
                    reader.Skip();
                    resource = (start, (int)reader.BytesConsumed - start);
                }
                else if (isFullUrl && reader.TokenType == JsonTokenType.String)
                {
                    fullUrl = reader.GetString();
                }
                else
                {
                    reader.Skip();
                }
            }

            // fullUrl may follow resource, so an entry is complete only at its end.
            if (resource is var (offset, length))
            {
                ranges.Add((offset, length, string.IsNullOrWhiteSpace(fullUrl) ? null : fullUrl));
            }
        }
    }
}
