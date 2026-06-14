// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Models.R4;

/// <summary>FHIR R4 Quantity datatype facade (minimal spike subset).</summary>
public sealed class Quantity : BaseJsonNode
{
    public Quantity()
    {
    }

    public Quantity(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    [JsonIgnore]
    public decimal? Value
    {
        get => GetProperty<decimal?>("value");
        set => SetProperty("value", value);
    }

    [JsonIgnore]
    public string? Unit
    {
        get => GetProperty<string>("unit");
        set => SetProperty("unit", value);
    }

    [JsonIgnore]
    public string? Code
    {
        get => GetProperty<string>("code");
        set => SetProperty("code", value);
    }
}
