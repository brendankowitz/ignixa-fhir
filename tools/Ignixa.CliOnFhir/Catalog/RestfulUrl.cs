// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;

namespace Ignixa.CliOnFhir.Catalog;

/// <summary>
/// A RESTful absolute URL, <c>[base]/[type]/[id]</c> with an optional <c>/_history/[vid]</c>, as R4 bundle.html §2.36.4.1
/// ("Resolving references in Bundles") defines one for a Bundle entry's fullUrl.
/// </summary>
internal static partial class RestfulUrl
{
    /// <summary>Splits <paramref name="url"/> into its base (ending in <c>/</c>), type and id; false when it is not RESTful.</summary>
    public static bool TryParse(string url, out string baseUrl, out string resourceType, out string id)
    {
        ArgumentNullException.ThrowIfNull(url);

        Match match = Pattern().Match(url);
        baseUrl = match.Success ? match.Groups["base"].Value : string.Empty;
        resourceType = match.Success ? match.Groups["type"].Value : string.Empty;
        id = match.Success ? match.Groups["id"].Value : string.Empty;
        return match.Success;
    }

    [GeneratedRegex("^(?<base>(?i:https?)://[^?#]+/)(?<type>[A-Z][A-Za-z]+)/(?<id>[A-Za-z0-9\\-\\.]{1,64})(/_history/[A-Za-z0-9\\-\\.]{1,64})?$")]
    private static partial Regex Pattern();
}
