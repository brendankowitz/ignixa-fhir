// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// Comparison and normalization rules for FHIR service base URIs.
/// </summary>
/// <remarks>
/// A service base is a directory, so <c>https://host/fhir</c> and <c>https://host/fhir/</c> name the same
/// service. Plain <see cref="Uri.Equals(object)"/> disagrees, which is why a configured base without a
/// trailing slash never matched the base parsed off a reference. Scheme and authority are compared
/// case-insensitively (and <see cref="Uri.Authority"/> already elides a default port), while the path is
/// compared ordinally because FHIR paths are case-sensitive.
/// </remarks>
public static class FhirServiceBaseUri
{
    /// <summary>
    /// Returns <paramref name="uri"/> with a trailing slash on its path, so every stored or compared base
    /// has one canonical spelling. Relative or null input is returned unchanged.
    /// </summary>
    public static Uri? Normalize(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri)
        {
            return uri;
        }

        var text = uri.GetLeftPart(UriPartial.Path);
        return text.EndsWith('/') ? new Uri(text, UriKind.Absolute) : new Uri(text + "/", UriKind.Absolute);
    }

    /// <summary>
    /// Determines whether two absolute URIs name the same FHIR service base, ignoring a trailing-slash
    /// difference, scheme/host casing, and a default port.
    /// </summary>
    public static bool AreEquivalent(Uri? left, Uri? right)
    {
        if (left is null || right is null || !left.IsAbsoluteUri || !right.IsAbsoluteUri)
        {
            return false;
        }

        return string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Authority, right.Authority, StringComparison.OrdinalIgnoreCase)
            && string.Equals(TrailingSlashPath(left), TrailingSlashPath(right), StringComparison.Ordinal);
    }

    private static string TrailingSlashPath(Uri uri)
        => uri.AbsolutePath.EndsWith('/') ? uri.AbsolutePath : uri.AbsolutePath + "/";
}
