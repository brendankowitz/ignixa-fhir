// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Abstractions;

namespace Ignixa.Serialization.SourceNodes;

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
    public FhirVersion? FhirVersion { get; set; }

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
    protected BaseJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion)
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

    protected T? GetProperty<T>(string name)
    {
        if (MutableNode.TryGetPropertyValue(name, out var node) && node is JsonValue valueNode)
        {
            return valueNode.GetValue<T>();
        }
        return default;
    }

    protected void SetProperty<T>(string name, T? value)
    {
        if (value == null)
        {
            MutableNode.Remove(name);
        }
        else if (value is JsonNode jsonNodeValue)
        {
            MutableNode[name] = jsonNodeValue;
        }
        else
        {
            MutableNode[name] = JsonValue.Create(value);
        }
    }

    protected T? GetComplexProperty<T>(string name) where T : BaseJsonNode
    {
        if (MutableNode.TryGetPropertyValue(name, out var node) && node is JsonObject jsonObject)
        {
            // Assuming T has a constructor that takes a JsonObject and optional FhirVersion
            return (T)Activator.CreateInstance(typeof(T), jsonObject, FhirVersion);
        }
        return default;
    }

    protected MutableJsonList<T> GetListProperty<T>(string name) where T : BaseJsonNode
    {
        if (!MutableNode.TryGetPropertyValue(name, out var node) || node is not JsonArray jsonArray)
        {
            jsonArray = null;
        }
        return new MutableJsonList<T>(() => GetOrCreateArray(name), jsonArray, FhirVersion);
    }

    protected MutablePrimitiveList<T> GetPrimitiveListProperty<T>(string name)
    {
        if (!MutableNode.TryGetPropertyValue(name, out var node) || node is not JsonArray jsonArray)
        {
            jsonArray = null;
        }
        return new MutablePrimitiveList<T>(() => GetOrCreateArray(name), jsonArray);
    }

    private JsonArray GetOrCreateArray(string name)
    {
        if (!MutableNode.TryGetPropertyValue(name, out var node) || node is not JsonArray jsonArray)
        {
            jsonArray = new JsonArray();
            MutableNode[name] = jsonArray;
        }
        return jsonArray;
    }
}
