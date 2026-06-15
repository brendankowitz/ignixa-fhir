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
    /// <returns>The version's base-typed facade for this resource type.</returns>
    /// <exception cref="InvalidOperationException">
    /// No typed model is registered for <c>(node.ResourceType, version)</c>. This means the version
    /// model package that owns the type was not referenced (so its module initializer never ran) or
    /// <c>{version}Models.Register()</c> was never called. Reference the package / call
    /// <c>Register()</c>, or use <see cref="TryAsVersion"/> for a best-effort caller.
    /// </exception>
    /// <remarks>
    /// The concrete return type is the shared base facade (<c>Ignixa.Models.X</c>). Reach a version
    /// delta with a further <c>node.As&lt;R4.X&gt;()</c>. Backed by <see cref="VersionedModelRegistry"/>;
    /// the relevant version model package must be referenced so its types self-register on load. On a
    /// registry miss this throws rather than returning a wrong-typed node — a silently mistyped facade
    /// is a correctness bug, not a usable fallback. Callers that can tolerate a miss should use
    /// <see cref="TryAsVersion"/>.
    /// </remarks>
    public static ResourceJsonNode AsVersion(this ResourceJsonNode node, FhirVersion version)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (VersionedModelRegistry.TryCreate(node.ResourceType, version, node.MutableNode, out var versioned))
        {
            return versioned;
        }

        throw new InvalidOperationException(
            $"No typed model is registered for resource type '{node.ResourceType}' at FHIR version {version}. "
            + $"Reference the Ignixa.Models.{version} package (or call {version}Models.Register()) before calling AsVersion.");
    }

    /// <summary>
    /// Best-effort variant of <see cref="AsVersion"/>: reinterprets this node as the requested
    /// version's base-typed facade when a typed model is registered, without throwing on a miss.
    /// </summary>
    /// <param name="node">The source node (already parsed).</param>
    /// <param name="version">The FHIR version to materialise.</param>
    /// <param name="versioned">
    /// On a hit, the version's base-typed facade (with <see cref="FhirVersion"/> stamped). On a miss,
    /// <paramref name="node"/> itself, unmodified (its <see cref="FhirVersion"/> is NOT changed).
    /// </param>
    /// <returns>True if a typed model was registered for the pair; otherwise false.</returns>
    public static bool TryAsVersion(this ResourceJsonNode node, FhirVersion version, out ResourceJsonNode versioned)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (VersionedModelRegistry.TryCreate(node.ResourceType, version, node.MutableNode, out var created))
        {
            versioned = created;
            return true;
        }

        versioned = node;
        return false;
    }
}
