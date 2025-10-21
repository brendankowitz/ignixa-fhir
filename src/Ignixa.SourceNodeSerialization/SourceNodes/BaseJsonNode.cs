// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.SourceNodeSerialization.Models;

namespace Ignixa.SourceNodeSerialization.SourceNodes;

/// <summary>
/// Base class for all *JsonNode model classes that wrap a mutable JsonObject.
/// </summary>
public abstract class BaseJsonNode : IMutableJsonNode
{
    /// <summary>
    /// Internal storage: Single source of truth (no caching, direct read/write).
    /// </summary>
    private readonly JsonObject _internalNode;

    /// <summary>
    /// Gets or sets the FHIR version for this node.
    /// Set at root node construction and propagated to children.
    /// Used to determine version-specific serialization behavior.
    /// </summary>
    [JsonIgnore]
    public FhirSpecification? FhirVersion { get; set; }

    /// <summary>
    /// Default constructor for deserialization.
    /// Creates a new empty JsonObject.
    /// </summary>
    protected BaseJsonNode()
    {
        _internalNode = new JsonObject();
    }

    /// <summary>
    /// Constructor for wrapping an existing JsonObject without FHIR version.
    /// Used by BaseJsonNodeConverter in Converters folder.
    /// </summary>
    /// <param name="jsonObject">Existing JsonObject to wrap.</param>
    protected BaseJsonNode(JsonObject jsonObject)
    {
        _internalNode = jsonObject ?? throw new ArgumentNullException(nameof(jsonObject));
    }

    /// <summary>
    /// Internal constructor for wrapping existing JsonObject with optional FHIR version.
    /// Used by BaseJsonNodeConverter in Models folder and when accessing nested properties.
    /// </summary>
    /// <param name="jsonObject">Existing JsonObject to wrap.</param>
    /// <param name="fhirVersion">Optional FHIR version (inherited from parent). Can be null.</param>
    protected BaseJsonNode(JsonObject jsonObject, FhirSpecification? fhirVersion)
    {
        _internalNode = jsonObject ?? throw new ArgumentNullException(nameof(jsonObject));
        FhirVersion = fhirVersion;
    }

    /// <summary>
    /// Gets the internal mutable JsonObject for direct manipulation.
    /// Use this for FHIR Patch operations or reference updates.
    /// All changes are immediately reflected in the resource.
    /// </summary>
    public JsonObject MutableNode => _internalNode;

    /// <summary>
    /// Sets a property value in the node.
    /// Convenience method for common mutations.
    /// </summary>
    /// <param name="name">The property name (e.g., "active", "name", "telecom").</param>
    /// <param name="value">The JsonNode value to set.</param>
    public void SetProperty(string name, JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (value == null)
        {
            _internalNode.Remove(name);
        }
        else
        {
            _internalNode[name] = value;
        }
    }
}
