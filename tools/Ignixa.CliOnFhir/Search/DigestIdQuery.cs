// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Search;

/// <summary>
/// <c>_id</c> values that are digests (the tools' name for a resource whose id is not a FHIR id, <see cref="FhirIds"/>),
/// replaced by that resource's id, search-escaped and percent-encoded, before the query is parsed. A digest that names no
/// resource of the searched type is left as it is and so matches nothing.
/// </summary>
internal static class DigestIdQuery
{
    public static string? Rewrite(FolderSession session, string resourceType, string? query)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (string.IsNullOrEmpty(query) || !query.Contains('~', StringComparison.Ordinal))
        {
            return query;
        }

        return string.Join('&', query.Split('&').Select(parameter => parameter.StartsWith("_id=", StringComparison.Ordinal)
            ? "_id=" + string.Join(',', parameter["_id=".Length..].Split(',').Select(value => Resolve(session, resourceType, value)))
            : parameter));
    }

    private static string Resolve(FolderSession session, string resourceType, string encoded) =>
        FhirIds.IsDigest(Uri.UnescapeDataString(encoded)) && session.FindByName(resourceType, Uri.UnescapeDataString(encoded)) is { } entry
            ? Uri.EscapeDataString(Escape(entry.Id))
            : encoded;

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
}
