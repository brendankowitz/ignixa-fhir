// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Models.R5;

/// <summary>
/// FHIR R5 HumanName datatype facade. Zero-copy view over the underlying JsonObject.
/// </summary>
public sealed class HumanName : BaseJsonNode
{
    public HumanName()
    {
    }

    public HumanName(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    [JsonIgnore]
    public string? Family
    {
        get => GetProperty<string>("family");
        set => SetProperty("family", value);
    }

    [JsonIgnore]
    public MutablePrimitiveList<string> Given => GetPrimitiveListProperty<string>("given");
}
