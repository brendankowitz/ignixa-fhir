// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.SourceNodeSerialization;
using Ignixa.SourceNodeSerialization.SourceNodes;

namespace Ignixa.Application.Features.Metadata.Models;

/// <summary>
/// Represents the REST component of a FHIR CapabilityStatement.
/// </summary>
[SuppressMessage("Design", "CA2227", Justification = "Collection properties for JSON serialization")]
public class RestComponentJsonNode : BaseJsonNode
{
    public RestComponentJsonNode()
    {
    }

    public RestComponentJsonNode(JsonObject jsonObject, FhirSpecification? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    [JsonIgnore]
    public RestfulCapabilityMode Mode
    {
        get => EnumUtility.ParseLiteral<RestfulCapabilityMode>(MutableNode["mode"]?.GetValue<string>()) ?? default;
        set => SetProperty("mode", JsonValue.Create(value.GetLiteral()));
    }

    [JsonIgnore]
    public string? Documentation
    {
        get => MutableNode["documentation"]?.GetValue<string>();
        set => SetProperty("documentation", value != null ? JsonValue.Create(value) : null);
    }

    [JsonIgnore]
    public SecurityComponentJsonNode? Security
    {
        get
        {
            if (!MutableNode.TryGetPropertyValue("security", out var node) || node is not JsonObject jsonObject)
            {
                return null;
            }

            return new SecurityComponentJsonNode(jsonObject);
        }
        set
        {
            if (value == null)
            {
                MutableNode.Remove("security");
            }
            else
            {
                MutableNode["security"] = value.MutableNode;
            }
        }
    }

    [JsonIgnore]
    public IList<ResourceComponentJsonNode>? Resource
    {
        get
        {
            if (!MutableNode.TryGetPropertyValue("resource", out var node) || node is not JsonArray array)
            {
                return null;
            }

            var result = new List<ResourceComponentJsonNode>();
            foreach (var item in array.OfType<JsonObject>())
            {
                result.Add(new ResourceComponentJsonNode(item, FhirVersion));
            }

            return result;
        }
        set
        {
            if (value == null)
            {
                MutableNode.Remove("resource");
            }
            else
            {
                // Propagate FhirVersion to child components
                foreach (var resourceComponent in value)
                {
                    resourceComponent.FhirVersion = FhirVersion;
                }

                var array = new JsonArray(value.Select(r => r.MutableNode).ToArray());
                MutableNode["resource"] = array;
            }
        }
    }

    [JsonIgnore]
    public IList<SystemInteractionJsonNode>? Interaction
    {
        get
        {
            if (!MutableNode.TryGetPropertyValue("interaction", out var node) || node is not JsonArray array)
            {
                return null;
            }

            var result = new List<SystemInteractionJsonNode>();
            foreach (var item in array.OfType<JsonObject>())
            {
                result.Add(new SystemInteractionJsonNode(item));
            }

            return result;
        }
        set
        {
            if (value == null)
            {
                MutableNode.Remove("interaction");
            }
            else
            {
                var array = new JsonArray(value.Select(i => i.MutableNode).ToArray());
                MutableNode["interaction"] = array;
            }
        }
    }

    [JsonIgnore]
    public IList<SearchParamJsonNode>? SearchParam
    {
        get
        {
            if (!MutableNode.TryGetPropertyValue("searchParam", out var node) || node is not JsonArray array)
            {
                return null;
            }

            var result = new List<SearchParamJsonNode>();
            foreach (var item in array.OfType<JsonObject>())
            {
                result.Add(new SearchParamJsonNode(item));
            }

            return result;
        }
        set
        {
            if (value == null)
            {
                MutableNode.Remove("searchParam");
            }
            else
            {
                var array = new JsonArray(value.Select(s => s.MutableNode).ToArray());
                MutableNode["searchParam"] = array;
            }
        }
    }

    [JsonIgnore]
    public IList<OperationJsonNode>? Operation
    {
        get
        {
            if (!MutableNode.TryGetPropertyValue("operation", out var node) || node is not JsonArray array)
            {
                return null;
            }

            var result = new List<OperationJsonNode>();
            foreach (var item in array.OfType<JsonObject>())
            {
                result.Add(new OperationJsonNode(item));
            }

            return result;
        }
        set
        {
            if (value == null)
            {
                MutableNode.Remove("operation");
            }
            else
            {
                var array = new JsonArray(value.Select(o => o.MutableNode).ToArray());
                MutableNode["operation"] = array;
            }
        }
    }

    /// <summary>
    /// The mode of the REST component (client or server).
    /// </summary>
    public enum RestfulCapabilityMode
    {
        [EnumLiteral("client")]
        Client,

        [EnumLiteral("server")]
        Server,
    }
}
