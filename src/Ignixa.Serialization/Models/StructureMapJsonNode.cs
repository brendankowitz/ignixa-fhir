// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization.Models;

/// <summary>
/// Strongly-typed model for FHIR StructureMap resource.
/// Represents a map from one set of concepts to one or more other concepts.
/// </summary>
[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
public class StructureMapJsonNode : ResourceJsonNode
{
    public StructureMapJsonNode()
    {
        ResourceType = "StructureMap";
    }

    /// <summary>
    /// Internal constructor for JsonConverter (accepts pre-parsed JsonObject).
    /// </summary>
    internal StructureMapJsonNode(JsonObject jsonObject)
        : base(jsonObject)
    {
    }

    /// <summary>
    /// Public constructor for JsonConverter (accepts pre-parsed JsonObject with optional FHIR version).
    /// </summary>
    public StructureMapJsonNode(JsonObject jsonObject, FhirSpecification? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    /// <summary>
    /// Canonical identifier for this structure map (globally unique).
    /// </summary>
    [JsonIgnore]
    public string? Url
    {
        get => GetProperty<string>("url");
        set => SetProperty("url", value);
    }

    /// <summary>
    /// Name for this structure map (computer friendly).
    /// </summary>
    [JsonIgnore]
    public string? Name
    {
        get => GetProperty<string>("name");
        set => SetProperty("name", value);
    }

    /// <summary>
    /// Business version of the structure map.
    /// </summary>
    [JsonIgnore]
    public string? Version
    {
        get => GetProperty<string>("version");
        set => SetProperty("version", value);
    }

    /// <summary>
    /// Name for this structure map (human friendly).
    /// </summary>
    [JsonIgnore]
    public string? Title
    {
        get => GetProperty<string>("title");
        set => SetProperty("title", value);
    }

    /// <summary>
    /// Publication status: draft | active | retired | unknown.
    /// </summary>
    [JsonIgnore]
    public PublicationStatus? Status
    {
        get
        {
            var statusStr = GetProperty<string>("status");
            return statusStr != null ? EnumUtility.ParseLiteral<PublicationStatus>(statusStr) : null;
        }
        set => SetProperty("status", value?.GetLiteral());
    }

    /// <summary>
    /// Natural language description of the structure map.
    /// </summary>
    [JsonIgnore]
    public string? Description
    {
        get => GetProperty<string>("description");
        set => SetProperty("description", value);
    }

    /// <summary>
    /// Structure definition used by this map.
    /// </summary>
    [JsonIgnore]
    public MutableJsonList<StructureMapStructureJsonNode> Structure => GetListProperty<StructureMapStructureJsonNode>("structure");

    /// <summary>
    /// Other maps used by this map (canonical URLs).
    /// </summary>
    [JsonIgnore]
    public MutablePrimitiveList<string> Import => GetPrimitiveListProperty<string>("import");

    /// <summary>
    /// Named sections for groups of transforms.
    /// </summary>
    [JsonIgnore]
    public MutableJsonList<StructureMapGroupJsonNode> Group => GetListProperty<StructureMapGroupJsonNode>("group");

    /// <summary>
    /// Contained resources.
    /// </summary>
    [JsonIgnore]
    public MutableJsonList<ResourceJsonNode> Contained => GetListProperty<ResourceJsonNode>("contained");
}

/// <summary>
/// FHIR PublicationStatus value set.
/// </summary>
public enum PublicationStatus
{
    [EnumLiteral("draft")]
    Draft,

    [EnumLiteral("active")]
    Active,

    [EnumLiteral("retired")]
    Retired,

    [EnumLiteral("unknown")]
    Unknown,
}
