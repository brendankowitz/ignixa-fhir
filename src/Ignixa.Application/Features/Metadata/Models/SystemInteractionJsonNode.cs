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
/// Represents a system-level interaction in a FHIR CapabilityStatement.
/// </summary>
public class SystemInteractionJsonNode : BaseJsonNode
{
    public SystemInteractionJsonNode()
    {
    }

    public SystemInteractionJsonNode(JsonObject jsonObject)
        : base(jsonObject)
    {
    }

    [JsonIgnore]
    public SystemRestfulInteraction Code
    {
        get => EnumUtility.ParseLiteral<SystemRestfulInteraction>(MutableNode["code"]?.GetValue<string>()) ?? default;
        set => SetProperty("code", JsonValue.Create(value.GetLiteral()));
    }

    [JsonIgnore]
    public string? Documentation
    {
        get => MutableNode["documentation"]?.GetValue<string>();
        set => SetProperty("documentation", value != null ? JsonValue.Create(value) : null);
    }
}
