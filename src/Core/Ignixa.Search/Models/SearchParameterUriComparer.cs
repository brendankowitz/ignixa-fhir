// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Search.Models;

/// <summary>
/// Preserves <see cref="Uri"/> equality semantics while treating the fragment as part of a search parameter canonical.
/// </summary>
/// <remarks>
/// <see cref="Uri.Equals(Uri)"/> ignores fragments, but derived search parameters use a fragment to remain distinct
/// from their source parameter.
/// </remarks>
public sealed class SearchParameterUriComparer : IEqualityComparer<Uri>
{
    public static SearchParameterUriComparer Instance { get; } = new();

    private SearchParameterUriComparer()
    {
    }

    public bool Equals(Uri x, Uri y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (x is null || y is null)
        {
            return false;
        }

        return x.Equals(y) &&
            string.Equals(GetFragment(x), GetFragment(y), StringComparison.Ordinal);
    }

    public int GetHashCode(Uri obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        return HashCode.Combine(
            obj.GetHashCode(),
            StringComparer.Ordinal.GetHashCode(GetFragment(obj)));
    }

    private static string GetFragment(Uri uri)
    {
        if (uri.IsAbsoluteUri)
        {
            return uri.Fragment;
        }

        int fragmentStart = uri.OriginalString.IndexOf('#', StringComparison.Ordinal);
        return fragmentStart < 0 ? string.Empty : uri.OriginalString[fragmentStart..];
    }
}
