// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization;

/// <summary>
/// Registry mapping a <c>(resourceType, <see cref="FhirVersion"/>)</c> pair to a factory that
/// produces the version's base-typed <see cref="ResourceJsonNode"/> facade over a pre-parsed
/// <see cref="JsonObject"/>.
/// </summary>
/// <remarks>
/// This is SEPARATE from <see cref="ResourceTypeRegistry"/>. <see cref="ResourceTypeRegistry"/>
/// remains the version-agnostic default-parse path (string -&gt; hand-written facade). This registry
/// powers the explicit, opt-in <c>AsVersion(FhirVersion)</c> dispatch: each version model package
/// self-registers its resource types on load (via a module initializer / explicit <c>Register()</c>),
/// so the enum API lights up only for referenced version packages.
/// </remarks>
public static class VersionedModelRegistry
{
    private static readonly ConcurrentDictionary<(string ResourceType, FhirVersion Version), Func<JsonObject, ResourceJsonNode>> Factories = new();

    /// <summary>
    /// Registers a factory for a <c>(resourceType, version)</c> pair. Re-registration overwrites the
    /// previous factory (idempotent module initializers are safe to call more than once).
    /// </summary>
    /// <param name="resourceType">The FHIR resource type string (e.g., "Patient").</param>
    /// <param name="version">The FHIR version the factory produces.</param>
    /// <param name="factory">Factory that wraps a <see cref="JsonObject"/> in the version's facade.</param>
    public static void Register(string resourceType, FhirVersion version, Func<JsonObject, ResourceJsonNode> factory)
    {
        ArgumentException.ThrowIfNullOrEmpty(resourceType);
        ArgumentNullException.ThrowIfNull(factory);

        Factories[(resourceType, version)] = factory;
    }

    /// <summary>
    /// Attempts to create the version-specific facade for a <c>(resourceType, version)</c> pair.
    /// </summary>
    /// <param name="resourceType">The FHIR resource type string.</param>
    /// <param name="version">The requested FHIR version.</param>
    /// <param name="jsonObject">The parsed JsonObject to wrap (zero-copy).</param>
    /// <param name="node">The created facade with <see cref="BaseJsonNode.FhirVersion"/> stamped, or null.</param>
    /// <returns>True if a factory was registered for the pair; otherwise false.</returns>
    public static bool TryCreate(
        string resourceType,
        FhirVersion version,
        JsonObject jsonObject,
        [NotNullWhen(true)] out ResourceJsonNode? node)
    {
        ArgumentNullException.ThrowIfNull(jsonObject);

        if (!string.IsNullOrEmpty(resourceType)
            && Factories.TryGetValue((resourceType, version), out var factory))
        {
            node = factory(jsonObject);
            node.FhirVersion = version;
            return true;
        }

        node = null;
        return false;
    }

    /// <summary>
    /// Returns true if a factory is registered for the given <c>(resourceType, version)</c> pair.
    /// </summary>
    public static bool IsRegistered(string resourceType, FhirVersion version)
        => !string.IsNullOrEmpty(resourceType) && Factories.ContainsKey((resourceType, version));
}
