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
/// Represents the security component of a FHIR CapabilityStatement REST definition.
/// </summary>
[SuppressMessage("Design", "CA2227", Justification = "Collection property for JSON serialization")]
public class SecurityComponentJsonNode : BaseJsonNode
{
    public SecurityComponentJsonNode()
    {
    }

    public SecurityComponentJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    [JsonIgnore]
    public bool? Cors
    {
        get => MutableNode["cors"]?.GetValue<bool>();
        set => SetProperty("cors", value.HasValue ? JsonValue.Create(value.Value) : null);
    }

    [JsonIgnore]
    public string? Description
    {
        get => MutableNode["description"]?.GetValue<string>();
        set => SetProperty("description", value is not null ? JsonValue.Create(value) : null);
    }

    [JsonIgnore]
    public MutableJsonList<CodeableConceptJsonNode> Service => GetListProperty<CodeableConceptJsonNode>("service");
}

/// <summary>
/// Represents a CodeableConcept (simplified for CapabilityStatement).
/// </summary>
[SuppressMessage("Design", "CA2227", Justification = "Collection property for JSON serialization")]
public class CodeableConceptJsonNode : BaseJsonNode
{
    public CodeableConceptJsonNode()
    {
    }

    public CodeableConceptJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    [JsonIgnore]
    public MutableJsonList<CodingJsonNode> Coding => GetListProperty<CodingJsonNode>("coding");

    [JsonIgnore]
    public string? Text
    {
        get => MutableNode["text"]?.GetValue<string>();
        set => SetProperty("text", value is not null ? JsonValue.Create(value) : null);
    }
}

/// <summary>
/// Represents a Coding (simplified for CapabilityStatement).
/// </summary>
public class CodingJsonNode : BaseJsonNode
{
    public CodingJsonNode()
    {
    }

    public CodingJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    [JsonIgnore]
    public string? System
    {
        get => MutableNode["system"]?.GetValue<string>();
        set => SetProperty("system", value is not null ? JsonValue.Create(value) : null);
    }

    [JsonIgnore]
    public string? Code
    {
        get => MutableNode["code"]?.GetValue<string>();
        set => SetProperty("code", value is not null ? JsonValue.Create(value) : null);
    }

    [JsonIgnore]
    public string? Display
    {
        get => MutableNode["display"]?.GetValue<string>();
        set => SetProperty("display", value is not null ? JsonValue.Create(value) : null);
    }
}
