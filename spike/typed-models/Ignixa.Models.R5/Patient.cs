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
/// FHIR R5 Patient resource facade. Zero-copy view over the underlying JsonObject.
/// Carries the same core elements as the R4 facade plus a deliberately divergent,
/// R5-only element (<see cref="MaritalStatusText"/>) to demonstrate why per-version
/// namespaces matter: the type system, not a runtime guess, encodes which fields a
/// version exposes.
/// </summary>
public sealed class Patient : ResourceJsonNode
{
    public Patient()
    {
        ResourceType = "Patient";
    }

    internal Patient(JsonObject jsonObject)
        : base(jsonObject)
    {
    }

    [JsonIgnore]
    public MutableJsonList<HumanName> Name => GetListProperty<HumanName>("name");

    [JsonIgnore]
    public AdministrativeGender? Gender
    {
        get => EnumUtility.ParseLiteral<AdministrativeGender>(GetProperty<string>("gender"));
        set => SetProperty("gender", value?.GetLiteral());
    }

    [JsonIgnore]
    public string? BirthDate
    {
        get => GetProperty<string>("birthDate");
        set => SetProperty("birthDate", value);
    }

    [JsonIgnore]
    public bool? Active
    {
        get => GetProperty<bool?>("active");
        set => SetProperty("active", value);
    }

    /// <summary>
    /// Spike-only divergent element absent from the R4 facade. Reads
    /// <c>maritalStatus.text</c> as a convenience scalar to show that an R5-namespaced
    /// facade can expose elements the R4 facade does not.
    /// </summary>
    [JsonIgnore]
    public string? MaritalStatusText
    {
        get => (MutableNode["maritalStatus"] as JsonObject)?["text"]?.GetValue<string>();
        set
        {
            if (value is null)
            {
                (MutableNode["maritalStatus"] as JsonObject)?.Remove("text");
                return;
            }

            if (MutableNode["maritalStatus"] is not JsonObject maritalStatus)
            {
                maritalStatus = new JsonObject();
                MutableNode["maritalStatus"] = maritalStatus;
            }

            maritalStatus["text"] = value;
        }
    }
}
