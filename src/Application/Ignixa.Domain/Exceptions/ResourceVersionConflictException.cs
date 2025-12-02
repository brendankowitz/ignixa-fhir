// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.Abstractions;
using Ignixa.Serialization.Models;

namespace Ignixa.Domain.Exceptions;

/// <summary>
/// Thrown when a resource update conflicts with a concurrent modification.
/// Indicates that another bundle or operation modified the resource between
/// when this operation read the current version and attempted to write.
/// Returns HTTP 409 Conflict with an appropriate OperationOutcome.
/// </summary>
public class ResourceVersionConflictException : FhirException
{
    public string ResourceType { get; }
    public string ResourceId { get; }
    public long AttemptedSurrogateId { get; }
    public long ExistingSurrogateId { get; }

    public ResourceVersionConflictException(
        string resourceType,
        string resourceId,
        long attemptedSurrogateId,
        long existingSurrogateId)
        : base($"Resource {resourceType}/{resourceId} was modified by another operation. " +
               $"Attempted SurrogateId {attemptedSurrogateId} conflicts with existing SurrogateId {existingSurrogateId}. " +
               $"This typically occurs when multiple bundles process concurrently and modify the same resource.")
    {
        ResourceType = resourceType;
        ResourceId = resourceId;
        AttemptedSurrogateId = attemptedSurrogateId;
        ExistingSurrogateId = existingSurrogateId;

        // Add OperationOutcome issue with appropriate severity and code
        Issues.Add(new OperationOutcomeJsonNode.IssueComponent
        {
            Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
            Code = OperationOutcomeJsonNode.IssueType.Conflict,
            Diagnostics = $"Resource {resourceType}/{resourceId} was modified by another concurrent operation. " +
                          $"Please retry the bundle. (Attempted SurrogateId: {attemptedSurrogateId}, " +
                          $"Existing SurrogateId: {existingSurrogateId})"
        });
    }

    /// <summary>
    /// Returns HTTP 409 Conflict status code.
    /// </summary>
    public override int StatusCode => 409;
}
