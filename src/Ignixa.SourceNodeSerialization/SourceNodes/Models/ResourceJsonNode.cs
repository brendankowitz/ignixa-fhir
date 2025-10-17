// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ignixa.SourceNodeSerialization.ElementModel;
using Ignixa.SourceNodeSerialization.Specification;
using ISourceNode = Ignixa.SourceNodeSerialization.ElementModel.ISourceNode;
using ITypedElement = Ignixa.SourceNodeSerialization.ElementModel.ITypedElement;

// For ToTypedElement extension method

namespace Ignixa.SourceNodeSerialization.SourceNodes.Models;

[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
public class ResourceJsonNode : IExtensionData, IResourceNode
{
    private ISourceNode _sourceNode;
    private ITypedElement _typedElement;
    private IStructureDefinitionSummaryProvider _cachedProvider;

    [JsonPropertyName("resourceType")]
    public string ResourceType { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("meta")]
    public MetaJsonNode Meta { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; }

    /// <summary>
    /// Wraps the JSON representation of the resource in an ISourceNode.
    /// Cached after first call.
    /// </summary>
    public ISourceNode ToSourceNode()
    {
        _sourceNode ??= new ReflectedSourceNode(this, null);

        return _sourceNode;
    }

    /// <summary>
    /// Converts to ITypedElement using the provided schema provider.
    /// Caches the result if called with the same provider instance.
    /// </summary>
    public ITypedElement ToTypedElement(IStructureDefinitionSummaryProvider provider)
    {
        // Cache only if same provider instance (to support multi-version scenarios)
        if (_typedElement == null || _cachedProvider != provider)
        {
            _typedElement = ToSourceNode().ToTypedElement(provider);
            _cachedProvider = provider;
        }

        return _typedElement;
    }

    /// <summary>
    /// Uses System.Text.Json to parse a JSON string into a ResourceJsonNode.
    /// </summary>
    public static ResourceJsonNode Parse(string json)
    {
        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);
    }
}
