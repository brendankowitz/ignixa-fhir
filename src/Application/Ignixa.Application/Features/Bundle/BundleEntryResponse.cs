// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Bundle;

/// <summary>
/// Represents the response for a single bundle entry after execution.
/// Maps to FHIR Bundle.entry.response structure.
/// </summary>
public record BundleEntryResponse
{
    /// <summary>
    /// Gets the HTTP status code (e.g., 200, 201, 404, 500).
    /// </summary>
    public required int StatusCode { get; init; }

    /// <summary>
    /// Gets the status string (e.g., "200 OK", "201 Created", "404 Not Found").
    /// </summary>
    public required string? Status { get; init; }

    /// <summary>
    /// Gets the Location header for created resources.
    /// Example: "Patient/123/_history/1".
    /// </summary>
    public string? Location { get; init; }

    /// <summary>
    /// Gets the ETag header for versioning.
    /// Example: W/"1".
    /// </summary>
    public string? ETag { get; init; }

    /// <summary>
    /// Gets the resource JSON returned from the operation.
    /// Present for successful GET/POST/PUT operations.
    /// Null for DELETE operations or errors.
    /// </summary>
    public string? ResourceJson { get; init; }

    /// <summary>
    /// Gets the LastModified timestamp for the resource.
    /// </summary>
    public DateTimeOffset? LastModified { get; init; }
}
