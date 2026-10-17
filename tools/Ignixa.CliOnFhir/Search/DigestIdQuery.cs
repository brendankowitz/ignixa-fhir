// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.RegularExpressions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Search;

/// <summary>
/// <c>_id</c> values that are digests (the tools' name for a resource whose id is not a FHIR id, <see cref="FhirIds"/>),
/// replaced by that resource's id, search-escaped and percent-encoded, before the query is parsed. The parameter is matched
/// with any modifier (<c>_id:not</c>), its value after percent-decoding (<c>%7E</c>, <c>%2C</c>) as the parser decodes it, and each
/// comma-separated value is looked at alone. A digest that names no resource of the searched type stays as it is, and so
/// matches nothing (or, under <c>:not</c>, excludes nothing). A digest anywhere else in the query (a chain's <c>_id</c>, a
/// reference value) cannot be resolved and fails the request, rather than silently matching nothing.
/// </summary>
internal static partial class DigestIdQuery
{
    public const string UnsupportedCode = "digest-not-supported";

    /// <exception cref="ToolRequestException">A digest appears where it cannot be resolved.</exception>
    public static string? Rewrite(FolderSession session, string resourceType, string? query)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (string.IsNullOrEmpty(query))
        {
            return query;
        }

        return string.Join('&', query.Split('&').Select(parameter => RewriteParameter(session, resourceType, parameter)));
    }

    private static string RewriteParameter(FolderSession session, string resourceType, string parameter)
    {
        int equals = parameter.IndexOf('=', StringComparison.Ordinal);
        // Names are matched as the query parser reads them, undecoded; values after percent-decoding, as it decodes them.
        string name = equals < 0 ? parameter : parameter[..equals];
        string value = equals < 0 ? string.Empty : Decode(parameter[(equals + 1)..]);
        bool isId = name == "_id" || name.StartsWith("_id:", StringComparison.Ordinal);
        if (!DigestAnywhere().IsMatch(value) && !DigestAnywhere().IsMatch(name))
        {
            return parameter;
        }

        if (!isId)
        {
            throw ToolRequestException.Error(
                UnsupportedCode,
                $"'{name}' holds a digest ('~' and 16 hex digits). Only _id (with any modifier) and the compartment argument accept the digest that names a resource whose id is not a FHIR id; elsewhere it would match nothing.");
        }

        IEnumerable<string> values = SplitUnescaped(value).Select(part => FhirIds.IsDigest(part) && session.FindByName(resourceType, part) is { } entry ? Escape(entry.Id) : part);
        return $"{name}={string.Join(',', values.Select(Uri.EscapeDataString))}";
    }

    /// <summary>Percent-decodes as the query parser does; text that is not valid percent-encoding is kept as it is.</summary>
    private static string Decode(string text) => Uri.UnescapeDataString(text);

    /// <summary>The comma-separated parts of a search value, keeping backslash escapes (<c>\,</c>) inside their part.</summary>
    private static IEnumerable<string> SplitUnescaped(string value)
    {
        var part = new StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                part.Append(value, i, 2);
                i++;
            }
            else if (value[i] == ',')
            {
                yield return part.ToString();
                part.Clear();
            }
            else
            {
                part.Append(value[i]);
            }
        }

        yield return part.ToString();
    }

    /// <summary>The FHIR search escape of a value: <c>\</c>, <c>,</c>, <c>$</c> and <c>|</c> each preceded by <c>\</c>.</summary>
    private static string Escape(string value)
    {
        var escaped = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            if (character is '\\' or ',' or '$' or '|')
            {
                escaped.Append('\\');
            }

            escaped.Append(character);
        }

        return escaped.ToString();
    }

    [GeneratedRegex("~[0-9a-f]{16}")]
    private static partial Regex DigestAnywhere();
}
