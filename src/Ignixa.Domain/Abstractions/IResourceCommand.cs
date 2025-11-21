// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Marker interface for commands that operate on a FHIR resource with JSON representation.
/// Enables pipeline behaviors to access resource type and JSON node without reflection.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Purpose</strong>: Allows Medino pipeline behaviors (e.g., DataAbsentReasonBehavior)
/// to access resource type and JSON representation in a type-safe manner.
/// </para>
/// <para>
/// <strong>Implementers</strong>:
/// - CreateOrUpdateResourceCommand
/// - ValidateResourceCommand
/// - ConditionalCreateCommand
/// - ConditionalUpdateCommand
/// </para>
/// </remarks>
public interface IResourceCommand
{
    /// <summary>
    /// The FHIR resource type (e.g., "Patient", "Observation").
    /// </summary>
    string ResourceType { get; }

    /// <summary>
    /// The resource as a ResourceJsonNode (provides mutable JsonNode tree and cached ISourceNode).
    /// </summary>
    ResourceJsonNode JsonNode { get; }
}
