// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization.Models;

public class BundleComponentResponseJsonNode : BaseJsonNode
{
    // Cached wrapper for Outcome property
    private ResourceJsonNode? _cachedOutcome;

    [JsonIgnore]
    public string Status
    {
        get => MutableNode["status"]?.GetValue<string>();
        set
        {
            if (value == null)
            {
                MutableNode.Remove("status");
            }
            else
            {
                MutableNode["status"] = value;
            }
        }
    }

    [JsonIgnore]
    public string Location
    {
        get => MutableNode["location"]?.GetValue<string>();
        set
        {
            if (value == null)
            {
                MutableNode.Remove("location");
            }
            else
            {
                MutableNode["location"] = value;
            }
        }
    }

    [JsonIgnore]
    public string Etag
    {
        get => MutableNode["etag"]?.GetValue<string>();
        set
        {
            if (value == null)
            {
                MutableNode.Remove("etag");
            }
            else
            {
                MutableNode["etag"] = value;
            }
        }
    }

    [JsonIgnore]
    public DateTimeOffset? LastModified
    {
        get
        {
            var internalNode = MutableNode;
            if (internalNode.TryGetPropertyValue("lastModified", out var node) && node != null)
            {
                var value = node.GetValue<string>();
                if (DateTimeOffset.TryParse(value, out var result))
                {
                    return result;
                }
            }

            return null;
        }
        set
        {
            if (value == null)
            {
                MutableNode.Remove("lastModified");
            }
            else
            {
                // Store as ISO 8601 string
                MutableNode["lastModified"] = value.Value.ToString("o");
            }
        }
    }

    [JsonIgnore]
    public ResourceJsonNode Outcome
    {
        get
        {
            if (_cachedOutcome == null)
            {
                var internalNode = MutableNode;
                if (internalNode.TryGetPropertyValue("outcome", out var outcomeNode) && outcomeNode is JsonObject outcomeObject)
                {
                    _cachedOutcome = new ResourceJsonNode(outcomeObject);
                }
            }

            return _cachedOutcome;
        }
        set
        {
            if (value == null)
            {
                MutableNode.Remove("outcome");
                _cachedOutcome = null;
            }
            else
            {
                MutableNode["outcome"] = value.MutableNode;
                _cachedOutcome = value;
            }
        }
    }
}
