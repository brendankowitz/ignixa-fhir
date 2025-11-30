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
/// Represents a group element in a FHIR ConceptMap resource.
/// Same source and target systems.
/// </summary>
[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
public class ConceptMapGroupJsonNode : BaseJsonNode
{
    public ConceptMapGroupJsonNode()
    {
    }

    /// <summary>
    /// Internal constructor for JsonConverter (accepts pre-parsed JsonObject).
    /// </summary>
    internal ConceptMapGroupJsonNode(JsonObject jsonObject)
        : base(jsonObject)
    {
    }

    /// <summary>
    /// Public constructor for JsonConverter (accepts pre-parsed JsonObject with optional FHIR version).
    /// </summary>
    public ConceptMapGroupJsonNode(JsonObject jsonObject, FhirSpecification? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    /// <summary>
    /// Source system where concepts to be mapped are defined.
    /// </summary>
    [JsonIgnore]
    public string? Source
    {
        get => GetProperty<string>("source");
        set => SetProperty("source", value);
    }

    /// <summary>
    /// Target system that the concepts are to be mapped to.
    /// </summary>
    [JsonIgnore]
    public string? Target
    {
        get => GetProperty<string>("target");
        set => SetProperty("target", value);
    }

    /// <summary>
    /// Mappings for concepts from the source to the target.
    /// </summary>
    [JsonIgnore]
    public MutableJsonList<ConceptMapElementJsonNode> Element => GetListProperty<ConceptMapElementJsonNode>("element");
}
