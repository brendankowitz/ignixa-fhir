// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Exceptions;
using Microsoft.AspNetCore.Http;

namespace Ignixa.Application.Infrastructure.Audit;

/// <summary>
/// Reads caller-supplied custom audit headers. The Ignixa prefix is <c>X-IGNIXA-AUDIT-*</c>; the Azure Health Data
/// Services prefix <c>X-MS-AZUREFHIR-AUDIT-*</c> is also accepted so existing AHDS-compatible clients work unchanged.
/// Both prefixes share the AHDS / microsoft/fhir-server limits and validation.
/// Values must only ever be written to the audit channel (<see cref="Ignixa.Domain.Abstractions.IAuditLogger"/>).
/// </summary>
public static class CustomAuditHeaders
{
    public const string Prefix = "X-IGNIXA-AUDIT-";

    /// <summary>
    /// Azure Health Data Services / microsoft/fhir-server prefix, accepted for compatibility.
    /// </summary>
    public const string AzureFhirCompatibilityPrefix = "X-MS-AZUREFHIR-AUDIT-";

    /// <summary>
    /// Maximum number of custom audit headers per request, counted across both prefixes.
    /// </summary>
    public const int MaximumCount = 10;

    public const int MaximumValueLength = 2048;

    private const string HttpContextItemKey = "Ignixa.CustomAuditHeaders";

    /// <summary>
    /// Reads and validates the custom audit headers of a request, caching the result for the request lifetime.
    /// </summary>
    /// <exception cref="AuditHeaderTooLargeException">A header value is longer than <see cref="MaximumValueLength"/>.</exception>
    /// <exception cref="AuditHeaderCountExceededException">More than <see cref="MaximumCount"/> headers are supplied.</exception>
    public static IReadOnlyDictionary<string, string> Read(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (httpContext.Items.TryGetValue(HttpContextItemKey, out var cached) &&
            cached is IReadOnlyDictionary<string, string> cachedHeaders)
        {
            return cachedHeaders;
        }

        var headers = Extract(httpContext.Request.Headers);
        httpContext.Items[HttpContextItemKey] = headers;
        return headers;
    }

    /// <summary>
    /// Extracts and validates custom audit headers. Header names are kept as received;
    /// multi-valued headers are comma-joined before the length check.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Extract(IHeaderDictionary requestHeaders)
    {
        ArgumentNullException.ThrowIfNull(requestHeaders);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, values) in requestHeaders)
        {
            if (!IsCustomAuditHeader(name))
            {
                continue;
            }

            var value = values.ToString();
            if (value.Length > MaximumValueLength)
            {
                throw new AuditHeaderTooLargeException(name, MaximumValueLength, value.Length);
            }

            headers[name] = value;
        }

        if (headers.Count > MaximumCount)
        {
            throw new AuditHeaderCountExceededException(MaximumCount, headers.Count);
        }

        return headers;
    }

    /// <summary>
    /// Copies every custom audit header from <paramref name="source"/> to <paramref name="target"/>.
    /// Used to carry an outer bundle request's audit headers onto each entry request.
    /// </summary>
    public static void CopyTo(IHeaderDictionary source, IHeaderDictionary target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        foreach (var (name, values) in source)
        {
            if (IsCustomAuditHeader(name))
            {
                target[name] = values;
            }
        }
    }

    public static bool IsCustomAuditHeader(string headerName) =>
        headerName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ||
        headerName.StartsWith(AzureFhirCompatibilityPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Formats headers for the structured audit log as <c>name=value;name=value</c> (AHDS AuditLogs shape),
    /// ordered by name, with control characters replaced to prevent log forging.
    /// </summary>
    public static string Format(IReadOnlyDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        return string.Join(
            ';',
            headers
                .OrderBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
                .Select(header => $"{Sanitize(header.Key)}={Sanitize(header.Value)}"));
    }

    private static string Sanitize(string value) =>
        string.Create(value.Length, value, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = char.IsControl(source[i]) ? ' ' : source[i];
            }
        });
}
