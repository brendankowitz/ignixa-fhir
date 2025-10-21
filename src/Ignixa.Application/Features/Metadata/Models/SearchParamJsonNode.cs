// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.SourceNodeSerialization;
using Ignixa.SourceNodeSerialization.SourceNodes;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Application.Features.Metadata.Models;

/// <summary>
/// Represents a search parameter in a FHIR CapabilityStatement.
/// </summary>
public class SearchParamJsonNode : BaseJsonNode
{
    public SearchParamJsonNode()
    {
    }

    public SearchParamJsonNode(JsonObject jsonObject, FhirSpecification? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    [JsonIgnore]
    public string? Name
    {
        get => MutableNode["name"]?.GetValue<string>();
        set => SetProperty("name", value != null ? JsonValue.Create(value) : null);
    }

    [JsonIgnore]
    public string? Definition
    {
        get => MutableNode["definition"]?.GetValue<string>();
        set => SetProperty("definition", value != null ? JsonValue.Create(value) : null);
    }

    [JsonIgnore]
    public SearchParamType Type
    {
        get => EnumUtility.ParseLiteral<SearchParamType>(MutableNode["type"]?.GetValue<string>()) ?? default;
        set => SetProperty("type", JsonValue.Create(value.GetLiteral()));
    }

    [JsonIgnore]
    public string? Documentation
    {
        get => MutableNode["documentation"]?.GetValue<string>();
        set => SetProperty("documentation", value != null ? JsonValue.Create(value) : null);
    }
}
