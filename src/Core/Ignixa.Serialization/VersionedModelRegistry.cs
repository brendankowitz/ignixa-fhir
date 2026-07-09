// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
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

        AssertFactoryProducesMatchingType(resourceType, factory);

        Factories[(resourceType, version)] = factory;
    }

    /// <summary>
    /// Verifies a factory actually produces the resource type it's being registered under. Runs in
    /// every build configuration, not just Debug: this is the only check standing between a mis-wired
    /// <c>Register()</c> call and <see cref="TryCreate"/> silently handing back a wrong-shaped facade --
    /// exactly the class of bug <see cref="CompatibleFhirVersionsAttribute"/> exists to catch on the
    /// read side, except <see cref="TryCreate"/> never goes through <c>As&lt;T&gt;()</c> so that guard
    /// cannot see this path at all. Registration happens a handful of times at process/tenant startup
    /// (module initializers), so the extra probe-construction cost here is immaterial.
    /// </summary>
    private static void AssertFactoryProducesMatchingType(string resourceType, Func<JsonObject, ResourceJsonNode> factory)
    {
        // ResourceType reads from the JSON, so it cannot distinguish a wrong-type factory. The CLR
        // type identity can: generated facades are named exactly after the resource ("Patient");
        // hand-written ones carry a "JsonNode" suffix ("BundleJsonNode"). Compare the produced CLR
        // type's simple name (suffix stripped) to the registered resource type.
        var probe = factory(new JsonObject { ["resourceType"] = resourceType });
        string producedTypeName = probe.GetType().Name.Replace("JsonNode", string.Empty, StringComparison.Ordinal);
        if (!string.Equals(producedTypeName, resourceType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"VersionedModelRegistry factory for '{resourceType}' produced a '{probe.GetType().Name}'. "
                + "The factory is mis-wired to the wrong typed model.");
        }
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
