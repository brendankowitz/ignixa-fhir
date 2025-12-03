// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization.Models;

public class SearchParameterJsonNode : ResourceJsonNode
{
    /// <summary>
    /// Default constructor for deserialization.
    /// </summary>
    public SearchParameterJsonNode()
    {
        ResourceType = "SearchParameter";
    }

    /// <summary>
    /// Internal constructor for JsonConverter (accepts pre-parsed JsonObject).
    /// </summary>
    internal SearchParameterJsonNode(JsonObject jsonObject)
        : base(jsonObject)
    {
    }

    /// <summary>
    /// Internal constructor for JsonConverter (accepts pre-parsed JsonObject with optional FHIR version).
    /// </summary>
    internal SearchParameterJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    [JsonIgnore]
    public string Name
    {
        get => MutableNode["name"]?.GetValue<string>();
        set
        {
            if (value == null)
            {
                MutableNode.Remove("name");
            }
            else
            {
                MutableNode["name"] = value;
            }
        }
    }

    [JsonIgnore]
    public string Code
    {
        get => MutableNode["code"]?.GetValue<string>();
        set
        {
            if (value == null)
            {
                MutableNode.Remove("code");
            }
            else
            {
                MutableNode["code"] = value;
            }
        }
    }

    [JsonIgnore]
    public string Description
    {
        get => MutableNode["description"]?.GetValue<string>();
        set
        {
            if (value == null)
            {
                MutableNode.Remove("description");
            }
            else
            {
                MutableNode["description"] = value;
            }
        }
    }

    [JsonIgnore]
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "This is a POCO.")]
    public string Url
    {
        get => MutableNode["url"]?.GetValue<string>();
        set
        {
            if (value == null)
            {
                MutableNode.Remove("url");
            }
            else
            {
                MutableNode["url"] = value;
            }
        }
    }

    [JsonIgnore]
    public string Type
    {
        get => MutableNode["type"]?.GetValue<string>();
        set
        {
            if (value == null)
            {
                MutableNode.Remove("type");
            }
            else
            {
                MutableNode["type"] = value;
            }
        }
    }

    [JsonIgnore]
    public string Expression
    {
        get => MutableNode["expression"]?.GetValue<string>();
        set
        {
            if (value == null)
            {
                MutableNode.Remove("expression");
            }
            else
            {
                MutableNode["expression"] = value;
            }
        }
    }

    [JsonIgnore]
    public IReadOnlyList<string> Base
    {
        get
        {
            if (!MutableNode.TryGetPropertyValue("base", out var baseNode) || baseNode is not JsonArray array)
            {
                return null;
            }

            var list = new List<string>();
            foreach (var item in array)
            {
                var value = item?.GetValue<string>();
                if (value != null)
                {
                    list.Add(value);
                }
            }

            return list;
        }
        set
        {
            if (value == null)
            {
                MutableNode.Remove("base");
            }
            else
            {
                var array = new JsonArray();
                foreach (var item in value)
                {
                    array.Add(item);
                }

                MutableNode["base"] = array;
            }
        }
    }

    [JsonIgnore]
    public IReadOnlyList<string> Target
    {
        get
        {
            if (!MutableNode.TryGetPropertyValue("target", out var targetNode) || targetNode is not JsonArray array)
            {
                return null;
            }

            var list = new List<string>();
            foreach (var item in array)
            {
                var value = item?.GetValue<string>();
                if (value != null)
                {
                    list.Add(value);
                }
            }

            return list;
        }
        set
        {
            if (value == null)
            {
                MutableNode.Remove("target");
            }
            else
            {
                var array = new JsonArray();
                foreach (var item in value)
                {
                    array.Add(item);
                }

                MutableNode["target"] = array;
            }
        }
    }
}
