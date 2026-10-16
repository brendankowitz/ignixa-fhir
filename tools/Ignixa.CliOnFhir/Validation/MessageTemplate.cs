// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Ignixa.CliOnFhir.Validation;

/// <summary>
/// A validator message reduced to its template, so a summary can group by it and show it without echoing the resource. The
/// validator interpolates values from the data into its messages (<c>Value 'abc' is not a valid 'date'</c>), so:
/// a quoted part is kept only when it is a single token (no whitespace, quote or control character, at most 128 characters)
/// that is not one of the resource's string values, otherwise it becomes <c>'…'</c>; every whole-token occurrence of one of
/// the resource's string values of three or more characters elsewhere becomes <c>…</c>; and a template still longer than
/// <see cref="MaxLength"/> is dropped (null). A token-shaped quoted part that is not a value (an element or type name, which
/// may come from the data's property names as element paths do) is kept, as paths are.
/// </summary>
internal static partial class MessageTemplate
{
    public const int MaxLength = 240;

    private const string Redacted = "…";

    /// <summary>The string values of <paramref name="resource"/> that a template must not echo, longest first.</summary>
    /// <param name="resource">The resource as parsed.</param>
    /// <param name="exempt">Strings that are never data prose, such as resource type names, which messages name legitimately.</param>
    public static IReadOnlyList<string> Vocabulary(JsonNode resource, IReadOnlySet<string> exempt)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(exempt);

        var values = new HashSet<string>(StringComparer.Ordinal);
        Collect(resource, values);
        return [.. values.Where(value => !exempt.Contains(value)).OrderByDescending(value => value.Length).ThenBy(value => value, StringComparer.Ordinal)];
    }

    /// <summary>The template of <paramref name="message"/>, or null when even the template is too long to show.</summary>
    public static string? Of(string message, IReadOnlyList<string> vocabulary)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(vocabulary);

        var values = new HashSet<string>(vocabulary, StringComparer.Ordinal);
        string quoted = Quoted().Replace(message, match =>
        {
            string inner = match.Groups["single"].Success ? match.Groups["single"].Value : match.Groups["double"].Value;
            return inner.Length <= 128 && Token().IsMatch(inner) && !values.Contains(inner) ? match.Value : $"'{Redacted}'";
        });

        string unquoted = quoted;
        foreach (string value in vocabulary.Where(value => value.Length >= 3))
        {
            unquoted = ReplaceWholeToken(unquoted, value);
        }

        string template = Whitespace().Replace(Control().Replace(unquoted, " "), " ").Trim();
        return template.Length <= MaxLength ? template : null;
    }

    private static void Collect(JsonNode? node, HashSet<string> values)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (KeyValuePair<string, JsonNode?> member in obj)
                {
                    Collect(member.Value, values);
                }

                break;
            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    Collect(item, values);
                }

                break;
            case JsonValue value when value.TryGetValue(out string? text) && text.Length > 0:
                values.Add(text);
                break;
        }
    }

    /// <summary>Replaces each occurrence of <paramref name="value"/> not adjacent to a letter or digit.</summary>
    private static string ReplaceWholeToken(string text, string value)
    {
        int index = text.IndexOf(value, StringComparison.Ordinal);
        if (index < 0)
        {
            return text;
        }

        var result = new StringBuilder(text.Length);
        int start = 0;
        while (index >= 0)
        {
            int end = index + value.Length;
            bool whole = (index == 0 || !char.IsLetterOrDigit(text[index - 1])) && (end == text.Length || !char.IsLetterOrDigit(text[end]));
            result.Append(text, start, index - start).Append(whole ? Redacted : value);
            start = end;
            index = text.IndexOf(value, start, StringComparison.Ordinal);
        }

        return result.Append(text, start, text.Length - start).ToString();
    }

    [GeneratedRegex("'(?<single>[^']*)'|\"(?<double>[^\"]*)\"")]
    private static partial Regex Quoted();

    [GeneratedRegex("^[^\\s\\p{C}'\"`<>\\\\]+$")]
    private static partial Regex Token();

    [GeneratedRegex("\\p{C}")]
    private static partial Regex Control();

    [GeneratedRegex("\\s+")]
    private static partial Regex Whitespace();
}
