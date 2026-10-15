// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Ignixa.CliOnFhir.Catalog;

/// <summary>
/// Resource ids as summaries may echo them. An id comes from the folder, which is untrusted (agent-tool-surface.md, risk 1):
/// one that is not a FHIR id (1 to 64 letters, digits, <c>-</c> or <c>.</c>) can carry any text, so summaries, node keys,
/// <c>next</c> and <c>absent</c> name its resource by a digest instead (<c>~</c> and 16 hex digits of the id's SHA-256).
/// The resource's own JSON is returned as written.
/// </summary>
public static partial class FhirIds
{
    /// <summary>The catalog diagnostic code for a resource whose id is not a FHIR id.</summary>
    public const string InvalidCode = "invalid-id";

    /// <summary>Whether <paramref name="id"/> is a FHIR id.</summary>
    public static bool IsValid(string? id) => id is not null && FhirId().IsMatch(id);

    /// <summary>Whether <paramref name="value"/> is a digest <see cref="Digest"/> produced.</summary>
    public static bool IsDigest(string? value) => value is not null && DigestPattern().IsMatch(value);

    /// <summary><paramref name="id"/> when it is a FHIR id, otherwise its digest.</summary>
    public static string Safe(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        return IsValid(id) ? id : Digest(id);
    }

    /// <summary><c>Type/id</c> with the id made <see cref="Safe"/>.</summary>
    public static string Reference(string resourceType, string id) => $"{resourceType}/{Safe(id)}";

    /// <summary>The catalogued resource's <see cref="Reference(string, string)"/>.</summary>
    public static string Reference(CatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return Reference(entry.ResourceType, entry.Id);
    }

    /// <summary>A <c>Type/id</c> the catalog or the index produced (a resolution target), with the id made <see cref="Safe"/>.</summary>
    public static string Target(string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        int slash = reference.IndexOf('/', StringComparison.Ordinal);
        return slash > 0 ? Reference(reference[..slash], reference[(slash + 1)..]) : Safe(reference);
    }

    /// <summary>
    /// A reference as a resource wrote it, for a message or a map that names it: a relative <c>Type/id</c> with its id made
    /// <see cref="Safe"/>; a <c>urn:</c> or http(s) reference kept when it holds no whitespace, control character, quote,
    /// angle bracket, backslash or <c>|</c> (so it cannot carry prose), and at most 256 characters; anything else replaced by
    /// its <see cref="Digest"/>.
    /// </summary>
    public static string Written(string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (RelativeReference().Match(reference) is { Success: true } relative)
        {
            return Reference(relative.Groups["type"].Value, relative.Groups["id"].Value);
        }

        return reference.Length <= 256 && WrittenShape().IsMatch(reference) ? reference : Digest(reference);
    }

    /// <summary><c>~</c> and the first 16 hex digits of the SHA-256 of <paramref name="value"/>.</summary>
    public static string Digest(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return "~" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 8));
    }

    [GeneratedRegex("^[A-Za-z0-9\\-\\.]{1,64}$")]
    private static partial Regex FhirId();

    [GeneratedRegex("^~[0-9a-f]{16}$")]
    private static partial Regex DigestPattern();

    [GeneratedRegex("^(?<type>[A-Z][A-Za-z]{1,63})/(?<id>[^/]+)$")]
    private static partial Regex RelativeReference();

    [GeneratedRegex("^(urn|https?):[^\\s\\p{C}\"'<>\\\\|]+$")]
    private static partial Regex WrittenShape();
}
