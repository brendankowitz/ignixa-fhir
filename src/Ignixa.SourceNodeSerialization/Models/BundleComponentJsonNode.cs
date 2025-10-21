// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.SourceNodeSerialization.SourceNodes;

namespace Ignixa.SourceNodeSerialization.Models;

[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
[SuppressMessage("Design", "CA1056", Justification = "POCO style model")]
public class BundleComponentJsonNode : BaseJsonNode
{
    // Cached wrappers for child object properties
    private ResourceJsonNode? _cachedResource;
    private BundleComponentRequestJsonNode? _cachedRequest;
    private BundleComponentResponseJsonNode? _cachedResponse;
    private BundleComponentSearchJsonNode? _cachedSearch;

    [JsonIgnore]
    public string FullUrl
    {
        get => MutableNode["fullUrl"]?.GetValue<string>();
        set
        {
            if (value == null)
            {
                MutableNode.Remove("fullUrl");
            }
            else
            {
                MutableNode["fullUrl"] = value;
            }
        }
    }

    [JsonIgnore]
    public ResourceJsonNode Resource
    {
        get
        {
            if (_cachedResource == null)
            {
                var internalNode = MutableNode;
                if (internalNode.TryGetPropertyValue("resource", out var resourceNode) && resourceNode is JsonObject resourceObject)
                {
                    _cachedResource = new ResourceJsonNode(resourceObject);
                }
            }

            return _cachedResource;
        }
        set
        {
            if (value == null)
            {
                MutableNode.Remove("resource");
                _cachedResource = null;
            }
            else
            {
                MutableNode["resource"] = value.MutableNode;
                _cachedResource = value;
            }
        }
    }

    [JsonIgnore]
    public BundleComponentRequestJsonNode Request
    {
        get
        {
            if (_cachedRequest == null)
            {
                var internalNode = MutableNode;
                if (internalNode.TryGetPropertyValue("request", out var requestNode) && requestNode is JsonObject requestObject)
                {
                    _cachedRequest = new BundleComponentRequestJsonNode { };
                    // Copy properties from JsonObject to the wrapper
                    var json = requestNode.ToJsonString();
                    _cachedRequest = JsonSerializer.Deserialize<BundleComponentRequestJsonNode>(json);
                }
            }

            return _cachedRequest;
        }
        set
        {
            if (value == null)
            {
                MutableNode.Remove("request");
                _cachedRequest = null;
            }
            else
            {
                MutableNode["request"] = value.MutableNode;
                _cachedRequest = value;
            }
        }
    }

    [JsonIgnore]
    public BundleComponentResponseJsonNode Response
    {
        get
        {
            if (_cachedResponse == null)
            {
                var internalNode = MutableNode;
                if (internalNode.TryGetPropertyValue("response", out var responseNode) && responseNode is JsonObject responseObject)
                {
                    var json = responseNode.ToJsonString();
                    _cachedResponse = JsonSerializer.Deserialize<BundleComponentResponseJsonNode>(json);
                }
            }

            return _cachedResponse;
        }
        set
        {
            if (value == null)
            {
                MutableNode.Remove("response");
                _cachedResponse = null;
            }
            else
            {
                MutableNode["response"] = value.MutableNode;
                _cachedResponse = value;
            }
        }
    }

    [JsonIgnore]
    public BundleComponentSearchJsonNode Search
    {
        get
        {
            if (_cachedSearch == null)
            {
                var internalNode = MutableNode;
                if (internalNode.TryGetPropertyValue("search", out var searchNode) && searchNode is JsonObject searchObject)
                {
                    var json = searchNode.ToJsonString();
                    _cachedSearch = JsonSerializer.Deserialize<BundleComponentSearchJsonNode>(json);
                }
            }

            return _cachedSearch;
        }
        set
        {
            if (value == null)
            {
                MutableNode.Remove("search");
                _cachedSearch = null;
            }
            else
            {
                MutableNode["search"] = value.MutableNode;
                _cachedSearch = value;
            }
        }
    }
}
