// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization;

/// <summary>
/// Registry mapping FHIR resource types to factory functions that create typed JsonNode instances.
/// Used by the JsonNodeConverter to create the correct specific type (e.g., ParametersJsonNode)
/// based on the resourceType field in the JSON.
/// </summary>
public static class ResourceTypeRegistry
{
    /// <summary>
    /// Factory functions for creating typed resource instances.
    /// Maps resourceType string to a factory function that takes a JsonObject and returns
    /// a ResourceJsonNode of the specific type.
    /// </summary>
    private static readonly Dictionary<string, Func<JsonObject, ResourceJsonNode>> _factoryMap = new()
    {
        ["Parameters"] = jsonObject => new ParametersJsonNode(jsonObject),
        ["Bundle"] = jsonObject => new BundleJsonNode(jsonObject),
        ["OperationOutcome"] = jsonObject => new OperationOutcomeJsonNode(jsonObject),
        ["Provenance"] = jsonObject => new ProvenanceJsonNode(jsonObject),
        ["SearchParameter"] = jsonObject => new SearchParameterJsonNode(jsonObject),
        // It can be registered separately by the Application layer if needed
    };

    /// <summary>
    /// Attempts to create a typed ResourceJsonNode instance based on the resourceType.
    /// </summary>
    /// <param name="resourceType">The FHIR resource type string (e.g., "Patient", "Parameters").</param>
    /// <param name="jsonObject">The parsed JsonObject containing the resource data.</param>
    /// <param name="instance">
    /// The created instance if the resourceType is registered, otherwise null.
    /// This is a downcast from the specific type to ResourceJsonNode for polymorphic handling.
    /// </param>
    /// <returns>
    /// True if the resourceType was found in the registry and the instance was created successfully;
    /// false if the resourceType is unknown and should fall back to generic ResourceJsonNode.
    /// </returns>
    public static bool TryCreateInstance(
        string resourceType,
        JsonObject jsonObject,
        [NotNullWhen(true)] out ResourceJsonNode? instance)
    {
        if (string.IsNullOrEmpty(resourceType))
        {
            instance = null;
            return false;
        }

        if (_factoryMap.TryGetValue(resourceType, out var factory))
        {
            instance = factory(jsonObject);
            return true;
        }

        instance = null;
        return false;
    }
}
