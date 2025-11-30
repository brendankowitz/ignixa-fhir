// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.Features.Metadata.Models;

/// <summary>
/// Represents the software component of a FHIR CapabilityStatement.
/// </summary>
public class SoftwareComponentJsonNode : BaseJsonNode
{
    public SoftwareComponentJsonNode()
    {
    }

    public SoftwareComponentJsonNode(JsonObject jsonObject)
        : base(jsonObject)
    {
    }

    [JsonIgnore]
    public string? Name
    {
        get => MutableNode["name"]?.GetValue<string>();
        set => SetProperty("name", value != null ? JsonValue.Create(value) : null);
    }

    [JsonIgnore]
    public string? Version
    {
        get => MutableNode["version"]?.GetValue<string>();
        set => SetProperty("version", value != null ? JsonValue.Create(value) : null);
    }

    [JsonIgnore]
    public string? ReleaseDate
    {
        get => MutableNode["releaseDate"]?.GetValue<string>();
        set => SetProperty("releaseDate", value != null ? JsonValue.Create(value) : null);
    }
}
