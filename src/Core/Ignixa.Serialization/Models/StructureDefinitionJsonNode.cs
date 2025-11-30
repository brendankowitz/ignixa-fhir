// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Text.Json.Nodes;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;

namespace Ignixa.Serialization.Models;

/// <summary>
/// Type-safe wrapper for FHIR StructureDefinition JSON parsed via Ignixa.Serialization.
/// Provides strongly-typed property access without raw string indexing.
/// Used by PackageResourceProvider for parsing StructureDefinitions from packages.
/// </summary>
public sealed class StructureDefinitionJsonNode
{
    private readonly ResourceJsonNode _resourceNode;
    private readonly ILogger _logger;

    private StructureDefinitionJsonNode(ResourceJsonNode resourceNode, ILogger logger)
    {
        _resourceNode = resourceNode;
        _logger = logger;
    }

    /// <summary>
    /// Parses StructureDefinition JSON string using Ignixa.Serialization.
    /// Returns null if parsing fails or resourceType is not "StructureDefinition".
    /// </summary>
    public static StructureDefinitionJsonNode? Parse(string resourceJson, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceJson);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            // Use Ignixa.Serialization to parse (not System.Text.Json.Nodes directly)
            var resourceNode = JsonSourceNodeFactory.Parse<ResourceJsonNode>(resourceJson);
            if (resourceNode == null)
            {
                logger.LogWarning("Failed to parse StructureDefinition JSON: null result");
                return null;
            }

            var sdNode = new StructureDefinitionJsonNode(resourceNode, logger);

            // Validate resourceType
            if (!string.Equals(sdNode.ResourceType, "StructureDefinition", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "Expected resourceType='StructureDefinition', got '{ResourceType}'",
                    sdNode.ResourceType);
                return null;
            }

            return sdNode;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse StructureDefinition JSON");
            return null;
        }
    }

    // ============ Typed Property Access ============

    /// <summary>
    /// Gets the resourceType property (should be "StructureDefinition").
    /// </summary>
    public string? ResourceType => _resourceNode.MutableNode["resourceType"]?.GetValue<string>();

    /// <summary>
    /// Gets the canonical URL of the structure definition.
    /// </summary>
    public string? Url => GetString("url");

    /// <summary>
    /// Gets the human-readable name.
    /// </summary>
    public string? Name => GetString("name");

    /// <summary>
    /// Gets the title.
    /// </summary>
    public string? Title => GetString("title");

    /// <summary>
    /// Gets the version.
    /// </summary>
    public string? Version => GetString("version");

    /// <summary>
    /// Gets the type this structure definition constrains (e.g., "Patient", "Observation").
    /// </summary>
    public string? Type => GetString("type");

    /// <summary>
    /// Gets the kind of structure (resource|complex-type|primitive-type|logical).
    /// </summary>
    public string? Kind => GetString("kind");

    /// <summary>
    /// Gets whether this is an abstract definition.
    /// </summary>
    public bool IsAbstract => GetBoolean("abstract") ?? false;

    /// <summary>
    /// Gets the base definition URL.
    /// </summary>
    public string? BaseDefinition => GetString("baseDefinition");

    /// <summary>
    /// Gets the derivation type (specialization|constraint).
    /// "specialization" = defines a new resource type (custom resource).
    /// "constraint" = profiles an existing resource type (standard profile).
    /// </summary>
    public string? Derivation => GetString("derivation");

    /// <summary>
    /// Gets the description.
    /// </summary>
    public string? Description => GetString("description");

    /// <summary>
    /// Gets snapshot.element[] array for element definitions.
    /// Returns null if snapshot or elements are missing or not an array.
    /// </summary>
    public JsonArray? GetSnapshotElements()
    {
        try
        {
            var snapshot = _resourceNode.MutableNode["snapshot"];
            if (snapshot == null)
            {
                _logger.LogDebug("No snapshot found in StructureDefinition");
                return null;
            }

            var elements = snapshot["element"];
            if (elements is not JsonArray arrayElements)
            {
                _logger.LogWarning("snapshot.element is not an array");
                return null;
            }

            return arrayElements;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting snapshot elements");
            return null;
        }
    }

    // ============ Helper Methods ============

    private string? GetString(string propertyName)
    {
        try
        {
            return _resourceNode.MutableNode[propertyName]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    private bool? GetBoolean(string propertyName)
    {
        try
        {
            return _resourceNode.MutableNode[propertyName]?.GetValue<bool>();
        }
        catch
        {
            return null;
        }
    }
}
