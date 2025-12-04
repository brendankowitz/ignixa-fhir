// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.Features.Metadata.Models;

/// <summary>
/// Represents an operation definition reference in a FHIR CapabilityStatement.
/// </summary>
public class OperationJsonNode : BaseJsonNode
{
    public OperationJsonNode()
    {
    }

    public OperationJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    [JsonIgnore]
    public string? Name
    {
        get => MutableNode["name"]?.GetValue<string>();
        set => SetProperty("name", value is not null ? JsonValue.Create(value) : null);
    }

    [JsonIgnore]
    public string? Definition
    {
        get => MutableNode["definition"]?.GetValue<string>();
        set => SetProperty("definition", value is not null ? JsonValue.Create(value) : null);
    }

    [JsonIgnore]
    public string? Documentation
    {
        get => MutableNode["documentation"]?.GetValue<string>();
        set => SetProperty("documentation", value is not null ? JsonValue.Create(value) : null);
    }
}
