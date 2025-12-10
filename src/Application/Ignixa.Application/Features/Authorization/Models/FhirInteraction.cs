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
    /// These codes are used in CapabilityStatement and SMART v2 scope matching.
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
}
