// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Models.R4;

/// <summary>
/// FHIR R4 Observation facade (minimal spike subset) demonstrating <c>value[x]</c> choice handling.
/// Only one <c>value*</c> key may be present at a time; the per-variant setters enforce this by
/// clearing the other variants.
/// </summary>
public sealed class Observation : ResourceJsonNode
{
    private static readonly string[] ValueVariantKeys =
        ["valueQuantity", "valueString", "valueCodeableConcept"];

    public Observation()
    {
        ResourceType = "Observation";
    }

    internal Observation(JsonObject jsonObject)
        : base(jsonObject)
    {
    }

    [JsonIgnore]
    public string? Status
    {
        get => GetProperty<string>("status");
        set => SetProperty("status", value);
    }

    [JsonIgnore]
    public ObservationValueType ValueType
    {
        get
        {
            if (MutableNode["valueQuantity"] is not null)
            {
                return ObservationValueType.Quantity;
            }

            if (MutableNode["valueString"] is not null)
            {
                return ObservationValueType.String;
            }

            if (MutableNode["valueCodeableConcept"] is not null)
            {
                return ObservationValueType.CodeableConcept;
            }

            return ObservationValueType.None;
        }
    }

    [JsonIgnore]
    public Quantity? ValueQuantity
    {
        get => GetComplexProperty<Quantity>("valueQuantity");
        set => SetVariant("valueQuantity", value?.MutableNode);
    }

    [JsonIgnore]
    public string? ValueString
    {
        get => GetProperty<string>("valueString");
        set => SetVariant("valueString", value is null ? null : JsonValue.Create(value));
    }

    [JsonIgnore]
    public CodeableConcept? ValueCodeableConcept
    {
        get => GetComplexProperty<CodeableConcept>("valueCodeableConcept");
        set => SetVariant("valueCodeableConcept", value?.MutableNode);
    }

    /// <summary>The raw node of whichever <c>value*</c> variant is present, or null.</summary>
    [JsonIgnore]
    public JsonNode? Value
    {
        get
        {
            foreach (var key in ValueVariantKeys)
            {
                if (MutableNode[key] is JsonNode node)
                {
                    return node;
                }
            }

            return null;
        }
    }

    private void SetVariant(string key, JsonNode? value)
    {
        foreach (var variant in ValueVariantKeys)
        {
            if (variant != key)
            {
                MutableNode.Remove(variant);
            }
        }

        if (value is null)
        {
            MutableNode.Remove(key);
        }
        else
        {
            MutableNode[key] = value;
        }
    }
}
