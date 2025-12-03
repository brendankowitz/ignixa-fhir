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
/// Represents an element (source concept) in a FHIR ConceptMap group.
/// </summary>
[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
public class ConceptMapElementJsonNode : BaseJsonNode
{
    public ConceptMapElementJsonNode()
    {
    }

    /// <summary>
    /// Internal constructor for JsonConverter (accepts pre-parsed JsonObject).
    /// </summary>
    internal ConceptMapElementJsonNode(JsonObject jsonObject)
        : base(jsonObject)
    {
    }

    /// <summary>
    /// Public constructor for JsonConverter (accepts pre-parsed JsonObject with optional FHIR version).
    /// </summary>
    public ConceptMapElementJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    /// <summary>
    /// Identity (code or path) or the element/item being mapped.
    /// </summary>
    [JsonIgnore]
    public string? Code
    {
        get => GetProperty<string>("code");
        set => SetProperty("code", value);
    }

    /// <summary>
    /// Display for the code.
    /// </summary>
    [JsonIgnore]
    public string? Display
    {
        get => GetProperty<string>("display");
        set => SetProperty("display", value);
    }

    /// <summary>
    /// Concept in target system for element.
    /// </summary>
    [JsonIgnore]
    public MutableJsonList<ConceptMapTargetJsonNode> Target => GetListProperty<ConceptMapTargetJsonNode>("target");
}
