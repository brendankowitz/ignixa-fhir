// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.SourceNodeSerialization.Abstractions;

namespace Ignixa.Application.Features.Bundle;

/// <summary>
/// Represents a single bundle entry ready for execution.
/// Contains parsed metadata from the FHIR Bundle.entry structure.
/// </summary>
public record BundleEntryContext
{
    /// <summary>
    /// Gets the zero-based index of this entry in the bundle.
    /// Used for ordering and error reporting.
    /// </summary>
    public required int Index { get; init; }

    /// <summary>
    /// Gets the HTTP verb for this operation (GET, POST, PUT, PATCH, DELETE, HEAD).
    /// Extracted from bundle.entry.request.method.
    /// </summary>
    public required string HttpVerb { get; init; }

    /// <summary>
    /// Gets the FHIR resource type (e.g., "Patient", "Observation").
    /// Extracted from bundle.entry.request.url or bundle.entry.resource.resourceType.
    /// Null for DELETE or GET operations without a body.
    /// </summary>
    public required string? ResourceType { get; init; }

    /// <summary>
    /// Gets the resource ID if specified in the request URL.
    /// For POST operations, this may be null (server assigns ID).
    /// For PUT/PATCH/DELETE/GET, this is typically required.
    /// </summary>
    public required string? ResourceId { get; init; }

    /// <summary>
    /// Gets the full request URL from bundle.entry.request.url.
    /// Examples: "Patient/123", "Observation?subject=Patient/123", "metadata".
    /// </summary>
    public required string RequestUrl { get; init; }

    /// <summary>
    /// Gets the resource content as ISourceNode.
    /// Present for POST/PUT/PATCH operations.
    /// Null for DELETE/GET/HEAD operations.
    /// </summary>
    public required ISourceNode? Resource { get; init; }

    /// <summary>
    /// Gets the fullUrl from bundle.entry.fullUrl.
    /// Used for reference resolution (e.g., "urn:uuid:05efabf0-4be2-4561-91ce-51548425acb9").
    /// </summary>
    public required string? FullUrl { get; init; }

    /// <summary>
    /// Gets or sets the assigned resource ID for POST operations.
    /// Populated during reference pre-processing phase.
    /// Maps urn:uuid references to actual server-assigned GUIDs.
    /// </summary>
    public string? AssignedResourceId { get; set; }

    /// <summary>
    /// Gets or sets the response after execution.
    /// Populated by BundleEntryExecutor during processing.
    /// </summary>
    public BundleEntryResponse? Response { get; set; }

    /// <summary>
    /// Gets the raw JSON representation of the resource.
    /// Captured during parsing to avoid re-serialization for deferred writes.
    /// Null for GET/DELETE operations without a resource body.
    /// </summary>
    public string? RawJson { get; init; }

    /// <summary>
    /// Gets the If-None-Exist header value from bundle.entry.request.ifNoneExist.
    /// Used for conditional create operations in bundle entries.
    /// Format: Search query string (e.g., "identifier=http://hospital.org/mrn|12345").
    /// </summary>
    public string? IfNoneExist { get; init; }

    /// <summary>
    /// Gets the If-Match header value from bundle.entry.request.ifMatch.
    /// Used for conditional update/delete operations in bundle entries.
    /// Format: ETag value (e.g., "W/\"2\"").
    /// </summary>
    public string? IfMatch { get; init; }
}
