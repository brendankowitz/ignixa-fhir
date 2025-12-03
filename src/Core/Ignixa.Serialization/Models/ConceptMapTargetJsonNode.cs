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
/// Represents a target element in a FHIR ConceptMap element.
/// The target concept that the source concept maps to.
/// </summary>
[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
public class ConceptMapTargetJsonNode : BaseJsonNode
{
    public ConceptMapTargetJsonNode()
    {
    }

    /// <summary>
    /// Internal constructor for JsonConverter (accepts pre-parsed JsonObject).
    /// </summary>
    internal ConceptMapTargetJsonNode(JsonObject jsonObject)
        : base(jsonObject)
    {
    }

    /// <summary>
    /// Public constructor for JsonConverter (accepts pre-parsed JsonObject with optional FHIR version).
    /// </summary>
    public ConceptMapTargetJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    /// <summary>
    /// Code that identifies the target element.
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
    /// Equivalence between source and target concepts.
    /// relatedto | equivalent | equal | wider | subsumes | narrower | specializes | inexact | unmatched | disjoint.
    /// </summary>
    [JsonIgnore]
    public string? Relationship
    {
        get => GetProperty<string>("relationship");
        set => SetProperty("relationship", value);
    }

    /// <summary>
    /// Description of status/issues in mapping.
    /// </summary>
    [JsonIgnore]
    public string? Comment
    {
        get => GetProperty<string>("comment");
        set => SetProperty("comment", value);
    }
}
