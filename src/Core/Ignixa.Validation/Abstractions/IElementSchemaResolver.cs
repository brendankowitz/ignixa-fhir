// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.Validation.Abstractions;

/// <summary>
/// Resolves a validation schema directly from a resource <see cref="IElement"/>, inspecting its
/// <c>resourceType</c> and <c>meta.profile</c> to compose the base StructureDefinition schema with
/// any declared profile schemas.
/// <para>
/// This is a richer contract than <see cref="IValidationSchemaResolver.GetSchema(string)"/>: the
/// canonical-URL lookup cannot see <c>meta.profile</c>, so consumers that have the resource element
/// should prefer this interface to pick up profile composition. Resolvers that support both should
/// implement <see cref="IValidationSchemaResolver"/> and this interface so callers can feature-detect
/// via <c>is IElementSchemaResolver</c> rather than downcasting to a concrete type.
/// </para>
/// </summary>
public interface IElementSchemaResolver
{
    /// <summary>
    /// Resolves the composed validation schema for a resource element.
    /// </summary>
    /// <param name="element">The root resource element to inspect for <c>resourceType</c> and <c>meta.profile</c>.</param>
    /// <returns>
    /// A composed schema, or <c>null</c> if the element has no resolvable resource type or no base
    /// schema. Unresolvable profile URLs do not cause a null return; instead the composed schema may
    /// embed warning-issue checks so callers learn validation was performed base-only.
    /// </returns>
    ValidationSchema? ResolveForElement(IElement element);
}
