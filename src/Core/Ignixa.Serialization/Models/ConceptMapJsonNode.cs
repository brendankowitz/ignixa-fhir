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

/// <summary>
/// Strongly-typed model for FHIR ConceptMap resource.
/// Represents a mapping from one set of concepts to one or more other concepts.
/// </summary>
[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
public class ConceptMapJsonNode : ResourceJsonNode
{
    public ConceptMapJsonNode()
    {
        ResourceType = "ConceptMap";
    }

    /// <summary>
    /// Internal constructor for JsonConverter (accepts pre-parsed JsonObject).
    /// </summary>
    internal ConceptMapJsonNode(JsonObject jsonObject)
        : base(jsonObject)
    {
    }

    /// <summary>
    /// Public constructor for JsonConverter (accepts pre-parsed JsonObject with optional FHIR version).
    /// </summary>
    public ConceptMapJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    /// <summary>
    /// Canonical identifier for this concept map (globally unique).
    /// </summary>
    [JsonIgnore]
    public string? Url
    {
        get => GetProperty<string>("url");
        set => SetProperty("url", value);
    }

    /// <summary>
    /// Name for this concept map (computer friendly).
    /// </summary>
    [JsonIgnore]
    public string? Name
    {
        get => GetProperty<string>("name");
        set => SetProperty("name", value);
    }

    /// <summary>
    /// Business version of the concept map.
    /// </summary>
    [JsonIgnore]
    public string? Version
    {
        get => GetProperty<string>("version");
        set => SetProperty("version", value);
    }

    /// <summary>
    /// Name for this concept map (human friendly).
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
    /// Natural language description of the concept map.
    /// </summary>
    [JsonIgnore]
    public string? Description
    {
        get => GetProperty<string>("description");
        set => SetProperty("description", value);
    }

    /// <summary>
    /// Same source code systems that are mapped to the target system.
    /// </summary>
    [JsonIgnore]
    public MutableJsonList<ConceptMapGroupJsonNode> Group => GetListProperty<ConceptMapGroupJsonNode>("group");
}
