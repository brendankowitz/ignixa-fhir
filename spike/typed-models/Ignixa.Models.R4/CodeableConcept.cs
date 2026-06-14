// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Models.R4;

/// <summary>FHIR R4 CodeableConcept datatype facade (minimal spike subset).</summary>
public sealed class CodeableConcept : BaseJsonNode
{
    public CodeableConcept()
    {
    }

    public CodeableConcept(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    [JsonIgnore]
    public string? Text
    {
        get => GetProperty<string>("text");
        set => SetProperty("text", value);
    }
}
