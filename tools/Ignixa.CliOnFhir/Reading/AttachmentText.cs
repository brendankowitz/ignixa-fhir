// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Ignixa.CliOnFhir.Reading;

/// <summary>
/// The text of an inline attachment of a report or document (<c>DiagnosticReport.presentedForm</c>,
/// <c>DocumentReference.content.attachment</c>): its base64 <c>data</c> decoded when its <c>contentType</c> is text/plain or
/// text/html, HTML reduced to its text by fixed rules, and cut at <see cref="MaxBytes"/> with a marker that says so.
/// </summary>
/// <param name="Reference">The resource, <c>Type/id</c>.</param>
/// <param name="Path">Where the attachment is, for example <c>presentedForm[0]</c>.</param>
/// <param name="ContentType">The attachment's contentType as written.</param>
/// <param name="Bytes">The decoded content's size in bytes, before any HTML is removed.</param>
/// <param name="Text">The text; null when it could not be decoded (see <paramref name="Problem"/>).</param>
/// <param name="Problem">Why there is no text: data that is not base64, or a charset the tool does not read.</param>
public sealed partial record AttachmentText(
    string Reference,
    string Path,
    string ContentType,
    int Bytes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Problem = null)
{
    /// <summary>Most UTF-8 bytes of one attachment's text returned; the rest is cut with a marker giving both sizes.</summary>
    public const int MaxBytes = 4000;

    /// <summary>Most attachments of one resource decoded; the rest are listed with a problem, so one resource's text stays within a response.</summary>
    public const int MaxAttachments = 2;

    /// <summary>
    /// The text of every inline text/plain or text/html attachment of <paramref name="resource"/> (a DiagnosticReport or
    /// DocumentReference), in document order; empty for other types and for attachments given by url only.
    /// </summary>
    public static IReadOnlyList<AttachmentText> Of(string reference, JsonObject resource)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(resource);

        IEnumerable<(string Path, JsonObject Attachment)> attachments = JsonText.Of(resource["resourceType"]) switch
        {
            "DiagnosticReport" => Items(resource["presentedForm"]).Select((item, index) => ($"presentedForm[{index}]", item)),
            "DocumentReference" => Items(resource["content"])
                .Select((item, index) => (Path: $"content[{index}].attachment", Attachment: item["attachment"] as JsonObject))
                .Where(item => item.Attachment is not null)
                .Select(item => (item.Path, item.Attachment!)),
            _ => [],
        };
        List<AttachmentText> texts = [.. attachments.Select(attachment => Decode(reference, attachment.Path, attachment.Attachment)).OfType<AttachmentText>()];
        return [.. texts.Select((text, index) => index < MaxAttachments || text.Text is null
            ? text
            : text with { Text = null, Problem = $"not decoded: at most {MaxAttachments} attachments of a resource are; read this one's data with read_resources elements." })];
    }

    /// <summary>
    /// The text of an HTML document: scripts, styles and comments dropped, line breaks for block elements and &lt;br&gt;, other tags
    /// removed, entities decoded, runs of spaces collapsed and blank lines kept to one. Linear in the input (the expressions
    /// run without backtracking), whatever the markup.
    /// </summary>
    public static string StripHtml(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        string text = Hidden().Replace(html, string.Empty);
        text = LineBreaks().Replace(text, "\n");
        text = Tags().Replace(text, string.Empty);
        return Tidy(WebUtility.HtmlDecode(text));
    }

    private static IEnumerable<JsonObject> Items(JsonNode? node) => node is JsonArray array ? array.OfType<JsonObject>() : [];

    private static AttachmentText? Decode(string reference, string path, JsonObject attachment)
    {
        if (JsonText.Of(attachment["contentType"]) is not { } contentType || JsonText.Of(attachment["data"]) is not { } data)
        {
            return null;
        }

        string[] parameters = contentType.Split(';', StringSplitOptions.TrimEntries);
        string mediaType = parameters[0].ToUpperInvariant();
        if (mediaType is not ("TEXT/PLAIN" or "TEXT/HTML"))
        {
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(data);
        }
        catch (FormatException)
        {
            return new AttachmentText(reference, path, contentType, 0, Text: null, "the data is not valid base64.");
        }

        string? charset = parameters.Skip(1)
            .Select(parameter => parameter.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(pair => pair.Length == 2 && string.Equals(pair[0], "charset", StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair[1].Trim('"'))
            .FirstOrDefault();
        if (EncodingOf(charset) is not { } encoding)
        {
            return new AttachmentText(reference, path, contentType, bytes.Length, Text: null, $"the charset '{charset}' is not one the tool reads (utf-8, utf-16, us-ascii, iso-8859-1).");
        }

        string decoded = encoding.GetString(bytes).TrimStart('﻿');
        string text = mediaType == "TEXT/HTML" ? StripHtml(decoded) : decoded.ReplaceLineEndings("\n").TrimEnd();
        return new AttachmentText(reference, path, contentType, bytes.Length, Cut(text));
    }

    private static Encoding? EncodingOf(string? charset) => charset?.ToUpperInvariant() switch
    {
        null or "UTF-8" or "UTF8" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        "UTF-16" or "UTF-16LE" => Encoding.Unicode,
        "UTF-16BE" => Encoding.BigEndianUnicode,
        "US-ASCII" or "ASCII" => Encoding.ASCII,
        "ISO-8859-1" or "LATIN1" => Encoding.Latin1,
        _ => null,
    };

    /// <summary>The text whole, or its first <see cref="MaxBytes"/> UTF-8 bytes (never splitting a character) and a marker with both sizes.</summary>
    private static string Cut(string text)
    {
        int total = Encoding.UTF8.GetByteCount(text);
        if (total <= MaxBytes)
        {
            return text;
        }

        int length = 0;
        int bytes = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > MaxBytes)
            {
                break;
            }

            bytes += rune.Utf8SequenceLength;
            length += rune.Utf16SequenceLength;
        }

        return $"{text[..length]}\n[truncated: the first {bytes} of {total} bytes of text are shown]";
    }

    private static string Tidy(string text)
    {
        IEnumerable<string> lines = text.ReplaceLineEndings("\n").Split('\n').Select(line => Spaces().Replace(line, " ").Trim());
        return BlankLines().Replace(string.Join('\n', lines), "\n\n").Trim();
    }

    [GeneratedRegex(@"<script\b[^>]*>.*?</script\s*>|<style\b[^>]*>.*?</style\s*>|<!--.*?-->", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex Hidden();

    [GeneratedRegex(@"<br\s*/?>|</?(p|div|li|tr|h[1-6]|table|ul|ol|pre|blockquote|section|article|header|footer)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex LineBreaks();

    [GeneratedRegex(@"<[^>]*>", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex Tags();

    [GeneratedRegex(@"[ \t ]+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\n{3,}", RegexOptions.CultureInvariant)]
    private static partial Regex BlankLines();
}
