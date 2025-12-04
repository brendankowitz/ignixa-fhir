// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

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

    public RestComponentJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
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
        set => SetProperty("documentation", value is not null ? JsonValue.Create(value) : null);
    }

    [JsonIgnore]
    public SecurityComponentJsonNode? Security
    {
        get => GetComplexProperty<SecurityComponentJsonNode>("security");
        set
        {
            if (value is null)
            {
                MutableNode.Remove("security");
            }
            else
            {
                value.FhirVersion = FhirVersion;
                MutableNode["security"] = value.MutableNode;
            }
        }
    }

    [JsonIgnore]
    public MutableJsonList<ResourceComponentJsonNode> Resource => GetListProperty<ResourceComponentJsonNode>("resource");

    [JsonIgnore]
    public MutableJsonList<SystemInteractionJsonNode> Interaction => GetListProperty<SystemInteractionJsonNode>("interaction");

    [JsonIgnore]
    public MutableJsonList<SearchParamJsonNode> SearchParam => GetListProperty<SearchParamJsonNode>("searchParam");

    [JsonIgnore]
    public MutableJsonList<OperationJsonNode> Operation => GetListProperty<OperationJsonNode>("operation");

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
