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
/// FHIR R4 Patient resource facade. Zero-copy view over the underlying JsonObject.
/// </summary>
public sealed class Patient : ResourceJsonNode
{
    public Patient()
    {
        ResourceType = "Patient";
    }

    // Non-public (JsonObject) ctor is required by ResourceJsonNode.As<T>()'s reflection
    // fallback, which binds with BindingFlags.NonPublic to a (JsonObject) signature.
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

    /// <summary>
    /// Firely-style primitive wrapper carrying the value plus the <c>_birthDate</c> shadow
    /// (id/extensions). Backed by (MutableNode, "birthDate"); a fresh wrapper per access is
    /// cheap and stateless because the parent JsonObject is the single source of truth.
    /// </summary>
    [JsonIgnore]
    public PrimitiveElement<string> BirthDateElement => new(MutableNode, "birthDate");

    /// <summary>String convenience over <see cref="BirthDateElement"/>'s value.</summary>
    [JsonIgnore]
    public string? BirthDate
    {
        get => BirthDateElement.Value;
        set => BirthDateElement.Value = value;
    }

    [JsonIgnore]
    public bool? Active
    {
        get => GetProperty<bool?>("active");
        set => SetProperty("active", value);
    }
}
