// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Ignixa.Api.Infrastructure;

/// <summary>
/// Helper for parsing and validating the X-TTL header for Time-To-Live resource expiration.
/// X-TTL header sets an expiration timestamp for resources using ISO 8601 duration format.
/// - Absent header: Resource lives forever (ExpiresAt = null)
/// - Empty or "0": Clear TTL (ExpiresAt = null)
/// - ISO8601 duration: Resource expires at (now + duration), e.g. "P30D" = 30 days, "PT1H" = 1 hour
/// </summary>
public static class TtlHeaderHelper
{
    private const string XTtlHeader = "X-TTL";

    /// <summary>
    /// Attempts to parse the X-TTL header from the request.
    /// </summary>
    /// <param name="headers">HTTP request headers.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <returns>
    /// Parsed expiration timestamp, or null if:
    /// - Header not present (resource lives forever)
    /// - Header value is empty or "0" (clear TTL)
    /// </returns>
    /// <exception cref="BadRequestException">Thrown when header contains invalid duration format.</exception>
    /// <remarks>
    /// Valid X-TTL header formats:
    /// - ISO8601 duration: "P30D" (30 days), "PT1H" (1 hour), "P1Y2M3DT4H5M6S"
    /// - Empty or "0": Clears TTL
    /// </remarks>
    public static DateTimeOffset? TryParseTtlHeader(
        IHeaderDictionary headers,
        ILogger logger)
    {
        if (!headers.TryGetValue(XTtlHeader, out var headerValue))
        {
            // No header present - resource lives forever
            return null;
        }

        var ttlValue = headerValue.ToString().Trim();

        // Empty or "0" means clear TTL (resource lives forever)
        if (string.IsNullOrWhiteSpace(ttlValue) || ttlValue == "0")
        {
            logger.LogDebug("X-TTL header is empty or '0' - clearing TTL");
            return null;
        }

        // Parse ISO8601 duration (e.g., "P30D", "PT1H", "P1Y2M3DT4H5M6S")
        TimeSpan duration;
        try
        {
            duration = System.Xml.XmlConvert.ToTimeSpan(ttlValue);
        }
        catch (FormatException ex)
        {
            logger.LogWarning(ex, "X-TTL header contains invalid ISO 8601 duration format: {Value}", ttlValue);
            throw new BadRequestException(
                $"X-TTL header contains invalid ISO 8601 duration format: '{ttlValue}'. Expected format like 'P30D' (30 days) or 'PT1H' (1 hour).");
        }

        // Validate duration is positive
        if (duration <= TimeSpan.Zero)
        {
            logger.LogWarning("X-TTL header specifies non-positive duration: {Duration}", duration);
            throw new BadRequestException(
                $"X-TTL header must specify a positive duration. Received: '{ttlValue}'");
        }

        // Calculate absolute expiration timestamp
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(duration);

        logger.LogInformation(
            "Parsed X-TTL header: duration={Duration}, resource expires at {ExpiresAt}",
            duration,
            expiresAt);

        return expiresAt;
    }
}
