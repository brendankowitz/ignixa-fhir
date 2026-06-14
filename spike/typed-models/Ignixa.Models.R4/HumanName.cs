// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Models.R4;

/// <summary>
/// FHIR R4 HumanName datatype facade. Zero-copy view over the underlying JsonObject.
/// </summary>
public sealed class HumanName : BaseJsonNode
{
    public HumanName()
    {
    }

    // Public (JsonObject, FhirVersion?) ctor is required by MutableJsonList<T> and
    // GetComplexProperty<T>, which resolve it via reflection with public-only binding.
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
