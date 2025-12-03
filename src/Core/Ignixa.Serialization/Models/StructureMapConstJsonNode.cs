// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization.Models;

/// <summary>
/// Represents a constant in StructureMap (R5+ only).
/// Constants allow defining named FHIRPath expressions that can be referenced in mapping rules.
/// </summary>
/// <remarks>
/// This element was introduced in FHIR R5 and is not available in R4/R4B.
/// Attempting to use this in earlier FHIR versions will result in validation errors.
/// </remarks>
public class StructureMapConstJsonNode : BaseJsonNode
{
    public StructureMapConstJsonNode()
    {
    }

    /// <summary>
    /// Public constructor for JsonConverter (accepts pre-parsed JsonObject with optional FHIR version).
    /// </summary>
    public StructureMapConstJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    /// <summary>
    /// Name of the constant (identifier for referencing in rules).
    /// </summary>
    [JsonIgnore]
    public string? Name
    {
        get => GetProperty<string>("name");
        set => SetProperty("name", value);
    }

    /// <summary>
    /// FHIRPath expression that defines the constant's value.
    /// The expression is evaluated once and can be referenced throughout the StructureMap.
    /// </summary>
    [JsonIgnore]
    public string? Value
    {
        get => GetProperty<string>("value");
        set => SetProperty("value", value);
    }
}
