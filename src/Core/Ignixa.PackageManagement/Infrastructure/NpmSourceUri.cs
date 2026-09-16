using System.Text;

namespace Ignixa.PackageManagement.Infrastructure;

internal static class NpmSourceUri
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static void ValidatePrefix(Uri? uri, string parameterName)
    {
        if (uri is null || !IsUnambiguousHttps(uri) || uri.OriginalString.Contains('?', StringComparison.Ordinal) ||
            !uri.OriginalString.EndsWith('/'))
        {
            throw new ArgumentException("An unambiguous absolute HTTPS directory prefix is required.", parameterName);
        }
    }

    internal static bool IsUnambiguousHttps(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps ||
            uri.UserInfo.Length != 0)
        {
            return false;
        }

        return TryCanonicalPath(uri.OriginalString, out _);
    }

    // Authorization uses decoded path segments; the request uses the original escaped spelling.
    // Do not decode the opaque query or let System.Uri erase traversal before this check.
    internal static Uri PreserveWireUri(Uri uri) =>
        new(uri.OriginalString, new UriCreationOptions { DangerousDisablePathAndQueryCanonicalization = true });

    internal static bool IsSafeReference(string reference)
    {
        if (reference.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '\\' or '#'))
        {
            return false;
        }
        int queryStart = reference.IndexOf('?', StringComparison.Ordinal);
        if (queryStart >= 0 && reference[(queryStart + 1)..].Any(c => !char.IsAscii(c)))
        {
            return false;
        }

        for (int index = 0; index < reference.Length; index++)
        {
            if (reference[index] != '%')
            {
                continue;
            }
            if (index + 2 >= reference.Length || !Uri.IsHexDigit(reference[index + 1]) ||
                !Uri.IsHexDigit(reference[index + 2]))
            {
                return false;
            }
            int value = Convert.ToInt32(reference.Substring(index + 1, 2), 16);
            if (value < 32 || value == 127)
            {
                return false;
            }
            index += 2;
        }
        return true;
    }

    internal static bool IsWithin(Uri uri, Uri prefix) =>
        uri.Scheme == prefix.Scheme &&
        string.Equals(uri.IdnHost, prefix.IdnHost, StringComparison.OrdinalIgnoreCase) &&
        uri.Port == prefix.Port &&
        TryCanonicalPath(uri.OriginalString, out string path) &&
        TryCanonicalPath(prefix.OriginalString, out string prefixPath) &&
        path.StartsWith(prefixPath, StringComparison.Ordinal);

    internal static bool TryResolveRedirect(Uri origin, Uri location, out Uri? destination)
    {
        destination = null;
        string reference = location.OriginalString;
        if (!IsSafeReference(reference))
        {
            return false;
        }
        if (reference.Length == 0)
        {
            destination = origin;
        }
        else if (location.IsAbsoluteUri)
        {
            destination = location;
        }
        else
        {
            string absolute = reference.StartsWith("//", StringComparison.Ordinal)
                ? $"{origin.Scheme}:{reference}"
                : reference.StartsWith('/') ? origin.GetLeftPart(UriPartial.Authority) + reference
                : reference.StartsWith('?') ? origin.GetLeftPart(UriPartial.Authority) + origin.AbsolutePath + reference
                : origin.GetLeftPart(UriPartial.Authority) +
                    origin.AbsolutePath[..(origin.AbsolutePath.LastIndexOf('/') + 1)] + reference;
            if (!Uri.TryCreate(absolute, UriKind.Absolute, out destination))
            {
                return false;
            }
        }
        return IsUnambiguousHttps(destination);
    }

    private static bool TryCanonicalPath(string original, out string path)
    {
        path = string.Empty;
        if (!original.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || !IsSafeReference(original))
        {
            return false;
        }
        int authorityEnd = original.IndexOfAny(['/', '?'], 8);
        string authority = authorityEnd < 0 ? original[8..] : original[8..authorityEnd];
        if (authority.Contains('%', StringComparison.Ordinal) || authority.Contains('@', StringComparison.Ordinal))
        {
            return false;
        }
        string rawPath = authorityEnd < 0 || original[authorityEnd] == '?' ? "/" : original[authorityEnd..].Split('?', 2)[0];
        if (rawPath.Contains("//", StringComparison.Ordinal))
        {
            return false;
        }
        var canonical = new StringBuilder();
        foreach (string segment in rawPath.Split('/'))
        {
            var bytes = new List<byte>(segment.Length);
            for (int index = 0; index < segment.Length; index++)
            {
                char value = segment[index];
                if (value == '%')
                {
                    bytes.Add(Convert.ToByte(segment.Substring(index + 1, 2), 16));
                    index += 2;
                }
                else if (char.IsAsciiLetterOrDigit(value) || "-._~!$&'()*+,;=:@".Contains(value, StringComparison.Ordinal))
                {
                    bytes.Add((byte)value);
                }
                else
                {
                    return false;
                }
            }
            string decoded;
            try
            {
                decoded = StrictUtf8.GetString(bytes.ToArray());
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
            if (decoded is "." or ".." || decoded.Any(c => char.IsControl(c) || c is '/' or '\\' or '%' or '?' or '#'))
            {
                return false;
            }
            canonical.Append(decoded).Append('/');
        }
        path = canonical.ToString(0, canonical.Length - 1);
        return true;
    }
}
