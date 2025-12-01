// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Api.Http;

/// <summary>
/// Custom IResult implementation for FHIR HTTP responses.
/// Supports efficient byte-based responses with FHIR-specific headers (ETag, Last-Modified, Location).
/// </summary>
public sealed class FhirResult : IResult
{

    private readonly ReadOnlyMemory<byte>? _bytes;
    private readonly int _statusCode;
    private readonly string? _location;
    private readonly string? _eTag;
    private readonly DateTimeOffset? _lastModified;
    private readonly object? _minimalBody;

    public FhirResult(
        int statusCode,
        ReadOnlyMemory<byte>? bytes = null,
        string? location = null,
        string? eTag = null,
        DateTimeOffset? lastModified = null,
        object? minimalBody = null)
    {
        _statusCode = statusCode;
        _bytes = bytes;
        _location = location;
        _eTag = eTag;
        _lastModified = lastModified;
        _minimalBody = minimalBody;
    }

    /// <summary>
    /// Sets the ETag header for versioning support.
    /// </summary>
    public FhirResult WithETag(string versionId)
    {
        return new FhirResult(_statusCode, _bytes, _location, versionId, _lastModified, _minimalBody);
    }

    /// <summary>
    /// Sets the Last-Modified header.
    /// </summary>
    public FhirResult WithLastModified(DateTimeOffset lastModified)
    {
        return new FhirResult(_statusCode, _bytes, _location, _eTag, lastModified, _minimalBody);
    }

    /// <summary>
    /// Sets the Location header (typically for 201 Created responses).
    /// </summary>
    public FhirResult WithLocation(string location)
    {
        return new FhirResult(_statusCode, _bytes, location, _eTag, _lastModified, _minimalBody);
    }

    /// <summary>
    /// Sets a minimal JSON body for Prefer: return=minimal responses.
    /// Creates a minimal resource stub with id, resourceType, and meta.
    /// </summary>
    public FhirResult WithMinimalBody(string resourceType, string id, string versionId, DateTimeOffset lastModified)
    {
        var minimalBody = new
        {
            resourceType,
            id,
            meta = new
            {
                versionId,
                lastUpdated = lastModified.ToString("o") // ISO 8601 format
            }
        };

        return new FhirResult(_statusCode, bytes: null, _location, _eTag, _lastModified, minimalBody);
    }

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = _statusCode;
        httpContext.Response.ContentType = KnownContentTypes.ApplicationFhirJson;

        // Set FHIR-specific headers
        if (_eTag != null)
        {
            // Format as weak ETag per FHIR spec
            httpContext.Response.Headers["ETag"] = $"W/\"{_eTag}\"";
        }

        if (_lastModified != null)
        {
            // RFC 1123 format (HTTP date)
            httpContext.Response.Headers["Last-Modified"] = _lastModified.Value.ToString("R");
        }

        if (_location != null)
        {
            httpContext.Response.Headers["Location"] = _location;
        }

        // Write body content
        if (_bytes.HasValue)
        {
            // Zero-copy write of raw FHIR JSON bytes
            await httpContext.Response.Body.WriteAsync(_bytes.Value, httpContext.RequestAborted);
        }
        else if (_minimalBody != null)
        {
            // Serialize minimal body to JSON
            await httpContext.Response.WriteAsJsonAsync(_minimalBody, httpContext.RequestAborted);
        }
    }
}
