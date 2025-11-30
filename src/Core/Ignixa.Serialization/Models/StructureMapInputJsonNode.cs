// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization.Models;

/// <summary>
/// Represents a named input to a StructureMap group.
/// </summary>
public class StructureMapInputJsonNode : BaseJsonNode
{
    public StructureMapInputJsonNode()
    {
    }

    /// <summary>
    /// Public constructor for JsonConverter (accepts pre-parsed JsonObject with optional FHIR version).
    /// </summary>
    public StructureMapInputJsonNode(JsonObject jsonObject, FhirSpecification? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    /// <summary>
    /// Name for this instance of data.
    /// </summary>
    [JsonIgnore]
    public string? Name
    {
        get => GetProperty<string>("name");
        set => SetProperty("name", value);
    }

    /// <summary>
    /// Type for this instance of data.
    /// </summary>
    [JsonIgnore]
    public string? Type
    {
        get => GetProperty<string>("type");
        set => SetProperty("type", value);
    }

    /// <summary>
    /// Mode for this instance of data: source | target.
    /// </summary>
    [JsonIgnore]
    public StructureMapInputMode? Mode
    {
        get
        {
            var modeStr = GetProperty<string>("mode");
            return modeStr != null ? EnumUtility.ParseLiteral<StructureMapInputMode>(modeStr) : null;
        }
        set => SetProperty("mode", value?.GetLiteral());
    }

    /// <summary>
    /// Documentation for this instance of data.
    /// </summary>
    [JsonIgnore]
    public string? Documentation
    {
        get => GetProperty<string>("documentation");
        set => SetProperty("documentation", value);
    }
}

/// <summary>
/// FHIR StructureMapInputMode value set.
/// </summary>
public enum StructureMapInputMode
{
    [EnumLiteral("source")]
    Source,

    [EnumLiteral("target")]
    Target,
}
