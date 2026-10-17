// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace Ignixa.CliOnFhir.Catalog;

/// <summary>
/// Reads only the top-level <c>resourceType</c> and <c>id</c> of a JSON resource (and, for the catalog, what its kind is told
/// by), skipping every other value, so a folder can be catalogued without building a document per resource.
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
        (string? resourceType, string? id, _, _) = Read(json, strings: null);
        return (resourceType, id);
    }

    /// <summary>
    /// <see cref="Sniff"/> plus the resource's <see cref="RecordKind"/>, from its top-level <c>meta.profile</c>, <c>class</c> and
    /// <c>category</c> (null when it has none), and the system of its identifiers when they all share one (null otherwise).
    /// </summary>
    /// <exception cref="JsonException">The input is not a well-formed JSON object.</exception>
    /// <param name="json">The resource.</param>
    /// <param name="strings">
    /// Hands back the previous instance of a profile, system or code when the same text comes again, as it does on every line of
    /// a file of one kind; a catalog keeps one per walk. Null reads every string anew.
    /// </param>
    public static (string? ResourceType, string? Id, string? Kind, string? IdentifierSystem) SniffWithKind(ReadOnlySpan<byte> json, RecentStrings? strings = null) =>
        Read(json, strings ?? new RecentStrings());

    private static (string? ResourceType, string? Id, string? Kind, string? IdentifierSystem) Read(ReadOnlySpan<byte> json, RecentStrings? strings)
    {
        var reader = new Utf8JsonReader(json, ReaderOptions);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected a JSON object.");
        }

        string? resourceType = null;
        string? id = null;
        List<string>? profiles = null;
        string? classCode = null;
        List<string>? categoryCodes = null;
        string? identifierSystem = null;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            bool isResourceType = reader.ValueTextEquals("resourceType"u8);
            bool isId = !isResourceType && reader.ValueTextEquals("id"u8);
            bool readKind = strings is not null;
            bool isMeta = readKind && reader.ValueTextEquals("meta"u8);
            bool isClass = readKind && profiles is null && reader.ValueTextEquals("class"u8);
            bool isCategory = readKind && profiles is null && reader.ValueTextEquals("category"u8);
            bool isIdentifier = readKind && reader.ValueTextEquals("identifier"u8);
            reader.Read();

            if (isResourceType && reader.TokenType == JsonTokenType.String)
            {
                resourceType = reader.GetString();
            }
            else if (isId && reader.TokenType == JsonTokenType.String)
            {
                id = reader.GetString();
            }
            else if (isMeta && reader.TokenType == JsonTokenType.StartObject)
            {
                profiles = ReadProfiles(ref reader, strings!);
            }
            else if (isClass)
            {
                classCode = ReadClassCode(ref reader, strings!);
            }
            else if (isCategory && reader.TokenType == JsonTokenType.StartArray)
            {
                categoryCodes = ReadConceptCodes(ref reader, strings!);
            }
            else if (isIdentifier && reader.TokenType == JsonTokenType.StartArray)
            {
                identifierSystem = ReadSharedSystem(ref reader, strings!);
            }
            else
            {
                reader.Skip();
            }
        }

        return strings is not null && resourceType is not null
            ? (resourceType, id, RecordKind.Of(resourceType, profiles, classCode, categoryCodes), identifierSystem)
            : (resourceType, id, null, null);
    }

    // meta: {"profile": ["url", ...], ...}; the reader is left on the object's end.
    private static List<string>? ReadProfiles(ref Utf8JsonReader reader, RecentStrings strings)
    {
        List<string>? profiles = null;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            bool isProfile = reader.ValueTextEquals("profile"u8);
            reader.Read();
            if (isProfile && reader.TokenType == JsonTokenType.StartArray)
            {
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.String && strings.Read(ref reader, RecentStrings.Profile) is { Length: > 0 } profile)
                    {
                        (profiles ??= []).Add(profile);
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
            }
            else
            {
                reader.Skip();
            }
        }

        return profiles;
    }

    // R4 Encounter.class is a Coding; R5's is a list of CodeableConcepts, of which the first coded one counts.
    private static string? ReadClassCode(ref Utf8JsonReader reader, RecentStrings strings)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            return ReadCodingCode(ref reader, strings);
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            return ReadConceptCodes(ref reader, strings)?[0];
        }

        reader.Skip();
        return null;
    }

    // A list of CodeableConcepts: every coding's code, in order; the reader is left on the list's end.
    private static List<string>? ReadConceptCodes(ref Utf8JsonReader reader, RecentStrings strings)
    {
        List<string>? codes = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                reader.Skip();
                continue;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                bool isCoding = reader.ValueTextEquals("coding"u8);
                reader.Read();
                if (isCoding && reader.TokenType == JsonTokenType.StartArray)
                {
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType == JsonTokenType.StartObject && ReadCodingCode(ref reader, strings) is { } code)
                        {
                            (codes ??= []).Add(code);
                        }
                        else if (reader.TokenType != JsonTokenType.EndObject)
                        {
                            reader.Skip();
                        }
                    }
                }
                else
                {
                    reader.Skip();
                }
            }
        }

        return codes;
    }

    // A list of Identifiers: the one system they all have, or null when one has none or two differ; the reader is left on the list's end.
    private static string? ReadSharedSystem(ref Utf8JsonReader reader, RecentStrings strings)
    {
        string? shared = null;
        bool agree = true;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                reader.Skip();
                agree = false;
                continue;
            }

            string? system = null;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                bool isSystem = reader.ValueTextEquals("system"u8);
                reader.Read();
                if (isSystem && reader.TokenType == JsonTokenType.String)
                {
                    system = strings.Read(ref reader, RecentStrings.System);
                }
                else
                {
                    reader.Skip();
                }
            }

            agree &= !string.IsNullOrEmpty(system) && (shared is null || string.Equals(shared, system, StringComparison.Ordinal));
            shared ??= system;
        }

        return agree ? shared : null;
    }

    // A Coding object's code; the reader is left on the object's end.
    private static string? ReadCodingCode(ref Utf8JsonReader reader, RecentStrings strings)
    {
        string? code = null;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            bool isCode = reader.ValueTextEquals("code"u8);
            reader.Read();
            if (isCode && reader.TokenType == JsonTokenType.String)
            {
                code = strings.Read(ref reader, RecentStrings.Code) is { Length: > 0 } value ? value : null;
            }
            else
            {
                reader.Skip();
            }
        }

        return code;
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
