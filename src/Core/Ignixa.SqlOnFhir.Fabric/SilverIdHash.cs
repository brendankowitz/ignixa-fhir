// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;

namespace Ignixa.SqlOnFhir.Fabric;

/// <summary>
/// The Silver identity rules: a namespace-qualified SHA-256 id and the URL stripping used to compare bases.
/// </summary>
internal static class SilverIdHash
{
    /// <summary>
    /// Removes a leading <c>scheme://</c> (word characters followed by <c>://</c>) and one trailing <c>/</c>.
    /// Returns null when nothing remains, so callers can treat an empty base as absent.
    /// </summary>
    public static string? Strip(string url)
    {
        var schemeEnd = 0;
        while (schemeEnd < url.Length && IsWordCharacter(url[schemeEnd]))
        {
            schemeEnd++;
        }

        var stripped = schemeEnd > 0 && url.AsSpan(schemeEnd).StartsWith("://", StringComparison.Ordinal)
            ? url[(schemeEnd + 3)..]
            : url;
        if (stripped.EndsWith('/'))
        {
            stripped = stripped[..^1];
        }

        return stripped.Length == 0 ? null : stripped;
    }

    /// <summary>Lowercase hex SHA-256 of <c>strip(namespace)/type/id</c> in UTF-8.</summary>
    public static string Compute(string strippedNamespace, string resourceType, string resourceId)
    {
        var bytes = Encoding.UTF8.GetBytes($"{strippedNamespace}/{resourceType}/{resourceId}");
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static bool IsWordCharacter(char character) =>
        char.IsLetter(character) || char.IsNumber(character) || character == '_';
}
