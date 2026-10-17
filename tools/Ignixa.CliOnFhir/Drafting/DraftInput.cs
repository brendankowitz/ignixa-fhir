// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Drafting;

/// <summary>
/// Reads a draft's typed input: inline JSON or the path of a file holding it. Text that holds <c>{</c>, <c>[</c>, <c>"</c> or a line
/// break is inline, after any leading byte order mark; anything else is a path, which no message repeats (only its digest), in case
/// it was input meant inline. The input is at most <see cref="MaxBytes"/>, nested at most <see cref="MaxDepth"/> deep, with no member
/// given twice; a JSON error gives only its line and byte, since the parser's message repeats the text.
/// </summary>
internal static class DraftInput
{
    /// <summary>The largest input read: a draft's arguments are a few hundred bytes, and the draft must fit one response whole.</summary>
    public const int MaxBytes = 16 * 1024;

    /// <summary>The deepest nesting read; the deepest member of a draft's schema is at depth 3.</summary>
    public const int MaxDepth = 8;

    private static readonly char[] InlineMarks = ['{', '[', '"', '\n', '\r'];

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = MaxDepth, AllowDuplicateProperties = false, CommentHandling = JsonCommentHandling.Disallow };

    /// <exception cref="ToolRequestException"><c>draft-input-unreadable</c> (the file) or <see cref="DraftArguments.InvalidCode"/> (the JSON).</exception>
    public static JsonObject Read(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        string text = input.TrimStart('﻿');
        if (text.IndexOfAny(InlineMarks) < 0)
        {
            text = FromFile(input, text);
        }
        else if (Encoding.UTF8.GetByteCount(text) > MaxBytes)
        {
            throw DraftArguments.Invalid($"The input is larger than {MaxBytes} bytes.");
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text, nodeOptions: null, Options);
        }
        catch (JsonException ex)
        {
            string where = ex.LineNumber is long line ? $" at line {line + 1}, byte {ex.BytePositionInLine + 1}" : string.Empty;
            throw DraftArguments.Invalid($"The input is not valid JSON{where}, or nests deeper than {MaxDepth} levels, or gives a member twice.");
        }

        return root as JsonObject ?? throw DraftArguments.Invalid("The input must be a JSON object of the draft's members (see the draft section of the README).");
    }

    private static string FromFile(string input, string path)
    {
        string named = $"The input file given (digest {FhirIds.Digest(input)})";
        try
        {
            var info = new FileInfo(Path.GetFullPath(path));
            if (!info.Exists)
            {
                throw ToolRequestException.Error("draft-input-unreadable", $"{named} does not exist; give a file, or inline JSON. The path is not repeated, in case it was input meant inline.");
            }

            byte[] bytes = Clock.FileRegion.Read(info.FullName, 0, MaxBytes, out bool truncated);
            return truncated
                ? throw DraftArguments.Invalid($"{named} is larger than {MaxBytes} bytes.")
                : new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or DecoderFallbackException)
        {
            throw ToolRequestException.Error("draft-input-unreadable", $"{named} cannot be read ({ex.GetType().Name}).");
        }
    }
}
