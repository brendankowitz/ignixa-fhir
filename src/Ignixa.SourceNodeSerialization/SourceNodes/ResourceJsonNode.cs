// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.SourceNodeSerialization.Abstractions;
using Ignixa.SourceNodeSerialization.Models;
using ISourceNode = Ignixa.SourceNodeSerialization.Abstractions.ISourceNode;
using ITypedElement = Ignixa.SourceNodeSerialization.Abstractions.ITypedElement;

// For ToTypedElement extension method

namespace Ignixa.SourceNodeSerialization.SourceNodes;

[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
public class ResourceJsonNode : BaseJsonNode, IResourceNode
{
    // Cached wrapper for Meta property (reuse same instance)
    private MetaJsonNode? _cachedMeta;
    private JsonNodeSourceNode? _cachedSourceNode;
    private ITypedElement? _cachedTypedElement;
    private IStructureDefinitionSummaryProvider? _cachedProvider;

    /// <summary>
    /// Default constructor for deserialization.
    /// </summary>
    public ResourceJsonNode()
    {
    }

    /// <summary>
    /// Internal constructor for JsonConverter and other types in this assembly (accepts pre-parsed JsonObject).
    /// </summary>
    internal ResourceJsonNode(JsonObject jsonObject)
        : base(jsonObject)
    {
    }

    [JsonIgnore]
    public string ResourceType
    {
        get => MutableNode["resourceType"]?.GetValue<string>() ?? string.Empty;
        set => MutableNode["resourceType"] = value;
    }

    [JsonIgnore]
    public string Id
    {
        get => MutableNode["id"]?.GetValue<string>() ?? string.Empty;
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                MutableNode.Remove("id");
            }
            else
            {
                MutableNode["id"] = value;
            }
        }
    }

    [JsonIgnore]
    public MetaJsonNode Meta
    {
        get
        {
            // Return cached wrapper if available
            if (_cachedMeta == null)
            {
                var internalNode = MutableNode;

                // Get or create the "meta" JsonObject
                if (!internalNode.TryGetPropertyValue("meta", out var metaNode) || metaNode is not JsonObject metaObject)
                {
                    metaObject = new JsonObject();
                    internalNode["meta"] = metaObject;
                }

                // Cache the wrapper (reuse same instance for subsequent accesses)
                _cachedMeta = new MetaJsonNode(metaObject);
            }

            return _cachedMeta;
        }
        set
        {
            if (value == null)
            {
                MutableNode.Remove("meta");
                _cachedMeta = null;
            }
            else
            {
                // Copy the internal JsonObject from the value
                MutableNode["meta"] = value.MutableNode;
                _cachedMeta = value; // Cache the new value
            }
        }
    }

    /// <summary>
    /// Wraps the JSON representation of the resource in an ISourceNode.
    /// </summary>
    public ISourceNode ToSourceNode()
    {
        _cachedSourceNode ??= JsonNodeSourceNode.FromRoot(MutableNode, ResourceType);
        return _cachedSourceNode;
    }

    /// <summary>
    /// Converts to ITypedElement using the provided schema provider.
    /// Caches the result for repeated calls with the same provider (reference equality).
    /// </summary>
    public ITypedElement ToTypedElement(IStructureDefinitionSummaryProvider provider)
    {
        // Cache hit: Same provider (reference equality check is fast and safe for singletons)
        if (_cachedTypedElement != null && ReferenceEquals(_cachedProvider, provider))
        {
            return _cachedTypedElement;
        }

        // Cache miss: Create and cache new typed element
        _cachedTypedElement = ToSourceNode().ToTypedElement(provider);
        _cachedProvider = provider;
        return _cachedTypedElement;
    }

    /// <summary>
    /// Uses System.Text.Json to parse a JSON string into a ResourceJsonNode.
    /// </summary>
    public static ResourceJsonNode Parse(string json)
    {
        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);
    }
}
