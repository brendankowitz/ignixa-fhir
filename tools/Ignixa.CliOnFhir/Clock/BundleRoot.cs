// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace Ignixa.CliOnFhir.Clock;

/// <summary>
/// Finds a Bundle's own <c>timestamp</c> in the two byte ranges around its entries: before the first catalogued entry's resource,
/// where the Bundle's members up to <c>entry</c> are, and after the last one's, where the rest of the <c>entry</c> array and then
/// the Bundle's later members are. Comments are skipped, as the catalog skips them.
/// </summary>
internal static class BundleRoot
{
    /// <summary>What a range said about the timestamp.</summary>
    public enum Outcome
    {
        /// <summary>The range holds the timestamp member; <see cref="Timestamp.Text"/> is its value, or null when it is not a string.</summary>
        Found,

        /// <summary>The range holds the Bundle's members on its side of the entries, and no timestamp among them.</summary>
        None,

        /// <summary>The range ended, or was not JSON as expected, before it could tell.</summary>
        Unknown,
    }

    /// <summary>A range's answer.</summary>
    public readonly record struct Timestamp(Outcome Outcome, string? Text);

    private static readonly JsonReaderOptions ReaderOptions = new() { CommentHandling = JsonCommentHandling.Skip };

    private static readonly JsonDocumentOptions DocumentOptions = new() { CommentHandling = JsonCommentHandling.Skip, MaxDepth = 64 };

    /// <summary>The timestamp among the Bundle's members before <c>entry</c>, in <paramref name="prefix"/>, which starts at the Bundle's brace.</summary>
    public static Timestamp BeforeEntries(ReadOnlySpan<byte> prefix)
    {
        var reader = new Utf8JsonReader(prefix, isFinalBlock: false, new JsonReaderState(ReaderOptions));
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return new Timestamp(Outcome.Unknown, null);
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("entry"u8))
                {
                    return new Timestamp(Outcome.None, null);
                }

                bool timestamp = reader.ValueTextEquals("timestamp"u8);
                if (!reader.Read())
                {
                    break;
                }

                if (timestamp)
                {
                    return new Timestamp(Outcome.Found, reader.TokenType == JsonTokenType.String ? reader.GetString() : null);
                }

                if (!reader.TrySkip())
                {
                    break;
                }
            }
        }
        catch (JsonException)
        {
            // Not the start of a Bundle as the catalog read it (the file changed): nothing can be told from it.
        }

        return new Timestamp(Outcome.Unknown, null);
    }

    /// <summary>
    /// The timestamp among the Bundle's members after <c>entry</c>, in <paramref name="suffix"/>, which starts just after an entry's
    /// resource, inside that entry's object, and runs to the end of the file.
    /// </summary>
    public static Timestamp AfterEntries(ReadOnlySpan<byte> suffix)
    {
        if (Members(suffix) is not { } members)
        {
            return new Timestamp(Outcome.Unknown, null);
        }

        byte[] document = new byte[members.Length + 1];
        document[0] = (byte)'{';
        members.CopyTo(document.AsSpan(1));
        try
        {
            using JsonDocument parsed = JsonDocument.Parse(document, DocumentOptions);
            return parsed.RootElement.TryGetProperty("timestamp", out JsonElement value)
                ? new Timestamp(Outcome.Found, value.ValueKind == JsonValueKind.String ? value.GetString() : null)
                : new Timestamp(Outcome.None, null);
        }
        catch (JsonException)
        {
            return new Timestamp(Outcome.Unknown, null);
        }
    }

    // The Bundle's members after the entry array, without the comma that leads them, up to and with the Bundle's closing brace; null
    // when the array does not end in the range. Inside an entry's object the depth is 3 (the Bundle, the array, the entry); the array
    // ends where it falls back to 1. Strings and comments are skipped, so a bracket in either counts for nothing.
    private static byte[]? Members(ReadOnlySpan<byte> suffix)
    {
        int depth = 3;
        int i = 0;
        while (i < suffix.Length && depth > 1)
        {
            byte current = suffix[i];
            if (current == '"')
            {
                i = AfterString(suffix, i);
                continue;
            }

            if (current == '/' && AfterComment(suffix, i) is int next)
            {
                i = next;
                continue;
            }

            depth += current switch
            {
                (byte)'{' or (byte)'[' => 1,
                (byte)'}' or (byte)']' => -1,
                _ => 0,
            };
            i++;
        }

        if (depth != 1)
        {
            return null;
        }

        i = SkipBlank(suffix, i);
        if (i < suffix.Length && suffix[i] == ',')
        {
            i++;
        }

        return i < suffix.Length ? suffix[i..].ToArray() : null;
    }

    private static int SkipBlank(ReadOnlySpan<byte> text, int i)
    {
        while (i < text.Length)
        {
            if (text[i] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            {
                i++;
            }
            else if (text[i] == '/' && AfterComment(text, i) is int next)
            {
                i = next;
            }
            else
            {
                break;
            }
        }

        return i;
    }

    // The index after the string starting at start (a quote), escapes included; the end of the text when it does not close.
    private static int AfterString(ReadOnlySpan<byte> text, int start)
    {
        for (int i = start + 1; i < text.Length; i++)
        {
            if (text[i] == '\\')
            {
                i++;
            }
            else if (text[i] == '"')
            {
                return i + 1;
            }
        }

        return text.Length;
    }

    // The index after the comment starting at start, or null when the slash starts none.
    private static int? AfterComment(ReadOnlySpan<byte> text, int start)
    {
        if (start + 1 >= text.Length)
        {
            return null;
        }

        if (text[start + 1] == '/')
        {
            int newline = text[start..].IndexOf((byte)'\n');
            return newline < 0 ? text.Length : start + newline + 1;
        }

        if (text[start + 1] == '*')
        {
            int close = text[(start + 2)..].IndexOf("*/"u8);
            return close < 0 ? text.Length : start + 2 + close + 2;
        }

        return null;
    }
}
