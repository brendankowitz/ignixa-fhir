// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Extensions.ProfileBehaviors.Abstractions;

/// <summary>
/// Visitor pattern for visiting FHIR resource properties with schema metadata.
/// Enables extensible transformations: filtering, mutation, injection, etc.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Use Cases</strong>:
/// - Element filtering (ResourceElementsSerializer): Skip non-requested elements
/// - Data-absent-reason injection (US Core): Add extensions for missing mandatory elements
/// - Custom transformations: Redaction, encryption, normalization
/// </para>
/// <para>
/// <strong>Architecture</strong>:
/// Two visitor implementations:
/// - ExtensibleStreamingSerializer: For byte-level transformations (Utf8JsonReader/Writer)
/// - ExtensibleJsonNodeVisitor: For tree mutation (JsonNode.MutableNode)
/// </para>
/// </remarks>
public interface IResourcePropertyVisitor
{
    /// <summary>
    /// Called when a property is encountered during visitation.
    /// </summary>
    /// <param name="propertyName">The JSON property name (e.g., "name", "birthDate").</param>
    /// <param name="metadata">Schema metadata for this element (cardinality, type, etc.).</param>
    /// <param name="depth">Nesting depth (0 = root level, 1 = nested, etc.).</param>
    /// <param name="context">Visitor context (resource type, options, etc.).</param>
    /// <returns>Action to take: Include, Skip, Mutate.</returns>
    PropertyVisitResult VisitProperty(
        string propertyName,
        ElementMetadata? metadata,
        int depth,
        VisitorContext context);

    /// <summary>
    /// Called when a mandatory property is missing during visitation.
    /// Only invoked for elements with min > 0 that are not present in the resource.
    /// </summary>
    /// <param name="propertyName">The missing property name.</param>
    /// <param name="metadata">Schema metadata for this element.</param>
    /// <param name="depth">Nesting depth (0 = root level).</param>
    /// <param name="context">Visitor context.</param>
    /// <returns>Action to take: Skip (do nothing) or Inject (add property).</returns>
    PropertyVisitResult VisitMissingProperty(
        string propertyName,
        ElementMetadata metadata,
        int depth,
        VisitorContext context);
}
