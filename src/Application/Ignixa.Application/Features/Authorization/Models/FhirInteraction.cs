// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Authorization.Models;

/// <summary>
/// FHIR interaction types as defined in the FHIR specification.
/// Maps to CapabilityStatement.rest.resource.interaction.code values.
/// </summary>
public enum FhirInteraction
{
    /// <summary>
    /// Read the current state of a resource (GET /[type]/[id]).
    /// </summary>
    Read,

    /// <summary>
    /// Read the state of a specific version of a resource (GET /[type]/[id]/_history/[vid]).
    /// </summary>
    VRead,

    /// <summary>
    /// Update an existing resource (PUT /[type]/[id]).
    /// </summary>
    Update,

    /// <summary>
    /// Partial update of a resource (PATCH /[type]/[id]).
    /// </summary>
    Patch,

    /// <summary>
    /// Delete a resource (DELETE /[type]/[id]).
    /// </summary>
    Delete,

    /// <summary>
    /// Retrieve the history of a specific resource (GET /[type]/[id]/_history).
    /// </summary>
    HistoryInstance,

    /// <summary>
    /// Retrieve the history of all resources of a type (GET /[type]/_history).
    /// </summary>
    HistoryType,

    /// <summary>
    /// Create a new resource (POST /[type]).
    /// </summary>
    Create,

    /// <summary>
    /// Search resources of a specific type (GET /[type] or POST /[type]/_search).
    /// </summary>
    SearchType,

    /// <summary>
    /// Search across all resource types (GET / or POST /_search).
    /// </summary>
    SearchSystem,

    /// <summary>
    /// Get server capabilities (GET /metadata).
    /// </summary>
    Capabilities,

    /// <summary>
    /// Process a batch bundle (POST /).
    /// </summary>
    Batch,

    /// <summary>
    /// Process a transaction bundle (POST /).
    /// </summary>
    Transaction,

    /// <summary>
    /// Execute an operation on a specific resource instance (POST /[type]/[id]/$[operation]).
    /// </summary>
    OperationInstance,

    /// <summary>
    /// Execute an operation on a resource type (POST /[type]/$[operation]).
    /// </summary>
    OperationType,

    /// <summary>
    /// Execute an operation at the system level (POST /$[operation]).
    /// </summary>
    OperationSystem
}

/// <summary>
/// Extension methods for FhirInteraction enum.
/// </summary>
public static class FhirInteractionExtensions
{
    /// <summary>
    /// Converts FhirInteraction to the FHIR specification interaction code.
    /// </summary>
    /// <param name="interaction">The interaction to convert.</param>
    /// <returns>The FHIR specification interaction code string.</returns>
    public static string ToFhirCode(this FhirInteraction interaction)
    {
        return interaction switch
        {
            FhirInteraction.Read => "read",
            FhirInteraction.VRead => "vread",
            FhirInteraction.Update => "update",
            FhirInteraction.Patch => "patch",
            FhirInteraction.Delete => "delete",
            FhirInteraction.HistoryInstance => "history-instance",
            FhirInteraction.HistoryType => "history-type",
            FhirInteraction.Create => "create",
            FhirInteraction.SearchType => "search-type",
            FhirInteraction.SearchSystem => "search-system",
            FhirInteraction.Capabilities => "capabilities",
            FhirInteraction.Batch => "batch",
            FhirInteraction.Transaction => "transaction",
            FhirInteraction.OperationInstance => "operation-instance",
            FhirInteraction.OperationType => "operation-type",
            FhirInteraction.OperationSystem => "operation-system",
            _ => throw new ArgumentOutOfRangeException(nameof(interaction), interaction, "Unknown FHIR interaction")
        };
    }

    /// <summary>
    /// Maps a FhirInteraction to a SMART on FHIR permission type.
    /// </summary>
    /// <param name="interaction">The interaction to map.</param>
    /// <returns>The SMART permission type (read, create, update, delete, or *).</returns>
    public static string ToSmartPermission(this FhirInteraction interaction)
    {
        return interaction switch
        {
            FhirInteraction.Read or FhirInteraction.VRead or FhirInteraction.SearchType or FhirInteraction.SearchSystem => "read",
            FhirInteraction.Create => "create",
            FhirInteraction.Update or FhirInteraction.Patch => "update",
            FhirInteraction.Delete => "delete",
            _ => "*"
        };
    }

    /// <summary>
    /// Parses an HTTP method and path pattern to determine the FhirInteraction.
    /// </summary>
    /// <param name="httpMethod">The HTTP method (GET, PUT, POST, DELETE, PATCH).</param>
    /// <param name="hasResourceId">Whether a resource ID is present in the path.</param>
    /// <param name="isSearchEndpoint">Whether this is a _search endpoint.</param>
    /// <param name="isHistoryEndpoint">Whether this is a _history endpoint.</param>
    /// <param name="isOperationEndpoint">Whether this is an operation endpoint (starts with $).</param>
    /// <returns>The corresponding FhirInteraction.</returns>
    public static FhirInteraction FromHttpRequest(
        string httpMethod,
        bool hasResourceId,
        bool isSearchEndpoint = false,
        bool isHistoryEndpoint = false,
        bool isOperationEndpoint = false)
    {
        return (httpMethod.ToUpperInvariant(), hasResourceId, isSearchEndpoint, isHistoryEndpoint, isOperationEndpoint) switch
        {
            // Operation endpoints
            (_, true, _, _, true) => FhirInteraction.OperationInstance,
            (_, false, _, _, true) when hasResourceId == false => FhirInteraction.OperationType,

            // History endpoints
            ("GET", true, _, true, _) => FhirInteraction.HistoryInstance,
            ("GET", false, _, true, _) => FhirInteraction.HistoryType,

            // Search endpoints
            (_, _, true, _, _) => FhirInteraction.SearchType,
            ("GET", false, _, _, _) => FhirInteraction.SearchType,

            // CRUD operations
            ("GET", true, _, _, _) => FhirInteraction.Read,
            ("PUT", true, _, _, _) => FhirInteraction.Update,
            ("PUT", false, _, _, _) => FhirInteraction.Update, // Conditional update
            ("POST", false, _, _, _) => FhirInteraction.Create,
            ("DELETE", true, _, _, _) => FhirInteraction.Delete,
            ("DELETE", false, _, _, _) => FhirInteraction.Delete, // Conditional delete
            ("PATCH", true, _, _, _) => FhirInteraction.Patch,
            ("PATCH", false, _, _, _) => FhirInteraction.Patch, // Conditional patch

            _ => throw new ArgumentException($"Cannot determine FHIR interaction from HTTP method: {httpMethod}, hasResourceId: {hasResourceId}")
        };
    }
}
