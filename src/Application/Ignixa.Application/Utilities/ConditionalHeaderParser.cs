using System;

namespace Ignixa.Application.Utilities;

/// <summary>
/// Utility for parsing HTTP conditional request headers (RFC 7232).
/// </summary>
public static class ConditionalHeaderParser
{
    /// <summary>
    /// Parses If-None-Match header value (ETag).
    /// Handles weak ETags (W/"5") and strong ETags ("5").
    /// </summary>
    /// <param name="headerValue">Header value from If-None-Match.</param>
    /// <returns>Extracted ETag value (without W/ prefix and quotes), or null if invalid.</returns>
    public static string? ParseIfNoneMatch(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return null;
        }

        // Remove W/ prefix for weak ETags
        var etag = headerValue.Trim();
        if (etag.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
        {
            etag = etag.Substring(2);
        }

        // Remove quotes
        etag = etag.Trim('"');

        return string.IsNullOrWhiteSpace(etag) ? null : etag;
    }

    /// <summary>
    /// Parses If-Modified-Since header value (HTTP date format per RFC 7232).
    /// </summary>
    /// <param name="headerValue">Header value from If-Modified-Since.</param>
    /// <returns>Parsed DateTimeOffset, or null if invalid.</returns>
    public static DateTimeOffset? ParseIfModifiedSince(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return null;
        }

        // RFC 7232 uses HTTP-date format (IMF-fixdate)
        // Example: "Wed, 17 Oct 2025 14:30:00 GMT"
        if (DateTimeOffset.TryParse(headerValue, out var date))
        {
            return date;
        }

        return null;
    }

    /// <summary>
    /// Formats DateTimeOffset as HTTP-date (RFC 7232).
    /// </summary>
    public static string FormatHttpDate(DateTimeOffset date)
    {
        return date.ToUniversalTime().ToString("R"); // RFC 1123 format
    }

    /// <summary>
    /// Formats ETag as weak ETag (W/"value").
    /// </summary>
    public static string FormatETag(string versionId)
    {
        return $"W/\"{versionId}\"";
    }
}
