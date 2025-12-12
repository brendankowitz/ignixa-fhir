// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.Extensions;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Api.Http;

/// <summary>
/// Static factory class for creating FHIR HTTP responses.
/// Provides fluent API for building responses with FHIR-specific headers.
/// </summary>
public static class FhirResults
{
    /// <summary>
    /// Creates a 200 OK response with FHIR resource bytes.
    /// </summary>
    /// <param name="resourceBytes">Raw JSON bytes of the FHIR resource</param>
    public static FhirResult Ok(ReadOnlyMemory<byte> resourceBytes)
    {
        return new FhirResult(StatusCodes.Status200OK, resourceBytes);
    }

    /// <summary>
    /// Creates a 201 Created response with FHIR resource bytes.
    /// Typically used after successful POST operations.
    /// </summary>
    /// <param name="location">The URI of the created resource (e.g., "/Patient/123")</param>
    /// <param name="resourceBytes">Raw JSON bytes of the created FHIR resource</param>
    public static FhirResult Created(string location, ReadOnlyMemory<byte> resourceBytes)
    {
        return new FhirResult(StatusCodes.Status201Created, resourceBytes, location);
    }

    /// <summary>
    /// Creates a 201 Created response without a body (for Prefer: return=minimal).
    /// </summary>
    /// <param name="location">The URI of the created resource (e.g., "/Patient/123")</param>
    public static FhirResult Created(string location)
    {
        return new FhirResult(StatusCodes.Status201Created, bytes: null, location);
    }

    /// <summary>
    /// Creates a 304 Not Modified response.
    /// Used when conditional GET headers indicate the resource hasn't changed.
    /// </summary>
    public static FhirResult NotModified()
    {
        return new FhirResult(StatusCodes.Status304NotModified);
    }

    /// <summary>
    /// Creates a 200 OK response with a FHIR resource, automatically applying _pretty formatting.
    /// </summary>
    /// <param name="resource">The FHIR resource to serialize</param>
    /// <param name="httpContext">The HTTP context (used to extract _pretty parameter)</param>
    public static FhirResult Ok(ResourceJsonNode resource, HttpContext httpContext)
    {
        bool pretty = httpContext.Request.Query.GetPrettyParameter();
        var bytes = resource.SerializeToBytes(pretty);
        return new FhirResult(StatusCodes.Status200OK, bytes, httpContext: httpContext);
    }

    /// <summary>
    /// Creates a 201 Created response with a FHIR resource, automatically applying _pretty formatting.
    /// </summary>
    /// <param name="location">The URI of the created resource</param>
    /// <param name="resource">The FHIR resource to serialize</param>
    /// <param name="httpContext">The HTTP context (used to extract _pretty parameter)</param>
    public static FhirResult Created(string location, ResourceJsonNode resource, HttpContext httpContext)
    {
        bool pretty = httpContext.Request.Query.GetPrettyParameter();
        var bytes = resource.SerializeToBytes(pretty);
        return new FhirResult(StatusCodes.Status201Created, bytes, location, httpContext: httpContext);
    }

    /// <summary>
    /// Creates a 200 OK response with pre-serialized FHIR resource bytes.
    /// If _pretty=true is requested, deserializes and re-serializes with formatting.
    /// Otherwise, returns the bytes as-is for optimal performance.
    /// </summary>
    /// <param name="resourceBytes">Pre-serialized resource bytes (minified)</param>
    /// <param name="httpContext">The HTTP context (used to extract _pretty parameter)</param>
    /// <remarks>
    /// Performance Note: This method has a performance cost when _pretty=true is requested
    /// due to deserialization and re-serialization. However, this is acceptable for
    /// development/debugging scenarios where formatted output is valuable.
    /// When _pretty is not requested, this uses the fast path with zero overhead.
    /// </remarks>
    public static FhirResult Ok(ReadOnlyMemory<byte> resourceBytes, HttpContext httpContext)
    {
        bool pretty = httpContext.Request.Query.GetPrettyParameter();

        if (pretty)
        {
            // Deserialize and re-serialize with pretty formatting
            // This has a performance cost but only when explicitly requested
            var resource = JsonSourceNodeFactory.Parse(resourceBytes);
            var prettyBytes = resource.SerializeToBytes(true);
            return new FhirResult(StatusCodes.Status200OK, prettyBytes, httpContext: httpContext);
        }
        else
        {
            // Fast path: return stored bytes as-is (no deserialization overhead)
            return new FhirResult(StatusCodes.Status200OK, resourceBytes, httpContext: httpContext);
        }
    }

    /// <summary>
    /// Creates a 201 Created response with pre-serialized FHIR resource bytes.
    /// If _pretty=true is requested, deserializes and re-serializes with formatting.
    /// Otherwise, returns the bytes as-is for optimal performance.
    /// </summary>
    /// <param name="location">The URI of the created resource</param>
    /// <param name="resourceBytes">Pre-serialized resource bytes (minified)</param>
    /// <param name="httpContext">The HTTP context (used to extract _pretty parameter)</param>
    /// <remarks>
    /// Performance Note: This method has a performance cost when _pretty=true is requested
    /// due to deserialization and re-serialization. However, this is acceptable for
    /// development/debugging scenarios where formatted output is valuable.
    /// When _pretty is not requested, this uses the fast path with zero overhead.
    /// </remarks>
    public static FhirResult Created(string location, ReadOnlyMemory<byte> resourceBytes, HttpContext httpContext)
    {
        bool pretty = httpContext.Request.Query.GetPrettyParameter();

        if (pretty)
        {
            // Deserialize and re-serialize with pretty formatting
            // This has a performance cost but only when explicitly requested
            var resource = JsonSourceNodeFactory.Parse(resourceBytes);
            var prettyBytes = resource.SerializeToBytes(true);
            return new FhirResult(StatusCodes.Status201Created, prettyBytes, location, httpContext: httpContext);
        }
        else
        {
            // Fast path: return stored bytes as-is (no deserialization overhead)
            return new FhirResult(StatusCodes.Status201Created, resourceBytes, location, httpContext: httpContext);
        }
    }
}
