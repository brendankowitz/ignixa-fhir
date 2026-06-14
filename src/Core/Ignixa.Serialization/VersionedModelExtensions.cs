// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization;

/// <summary>
/// Runtime-dispatch entry point for typed models keyed by <see cref="FhirVersion"/>.
/// </summary>
public static class VersionedModelExtensions
{
    /// <summary>
    /// Reinterprets this node as the requested version's base-typed facade
    /// (e.g., <c>Ignixa.Models.Patient</c>), zero-copy, with the <see cref="FhirVersion"/> stamped.
    /// </summary>
    /// <param name="node">The source node (already parsed).</param>
    /// <param name="version">The FHIR version to materialise.</param>
    /// <returns>
    /// The version's base-typed facade for this resource type, or — when the version package that
    /// owns the type has not been loaded/registered — <paramref name="node"/> itself with the version
    /// stamped, so callers always get a usable <see cref="ResourceJsonNode"/>.
    /// </returns>
    /// <remarks>
    /// The concrete return type is the shared base facade (<c>Ignixa.Models.X</c>). Reach a version
    /// delta with a further <c>node.As&lt;R4.X&gt;()</c>. Backed by <see cref="VersionedModelRegistry"/>;
    /// the relevant version model package must be referenced so its types self-register on load.
    /// </remarks>
    public static ResourceJsonNode AsVersion(this ResourceJsonNode node, FhirVersion version)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (VersionedModelRegistry.TryCreate(node.ResourceType, version, node.MutableNode, out var versioned))
        {
            return versioned;
        }

        node.FhirVersion = version;
        return node;
    }
}
