// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
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

    public SystemInteractionJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
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
        set => SetProperty("documentation", value is not null ? JsonValue.Create(value) : null);
    }
}
