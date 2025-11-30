// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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
}
