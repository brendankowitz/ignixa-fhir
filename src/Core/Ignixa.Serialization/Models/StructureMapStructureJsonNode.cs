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
/// Represents a structure definition used by a StructureMap.
/// </summary>
public class StructureMapStructureJsonNode : BaseJsonNode
{
    public StructureMapStructureJsonNode()
    {
    }

    /// <summary>
    /// Public constructor for JsonConverter (accepts pre-parsed JsonObject with optional FHIR version).
    /// </summary>
    public StructureMapStructureJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    /// <summary>
    /// Canonical reference to structure definition.
    /// </summary>
    [JsonIgnore]
    public string? Url
    {
        get => GetProperty<string>("url");
        set => SetProperty("url", value);
    }

    /// <summary>
    /// Name for type in this map (alias).
    /// </summary>
    [JsonIgnore]
    public string? Alias
    {
        get => GetProperty<string>("alias");
        set => SetProperty("alias", value);
    }

    /// <summary>
    /// How the type is used: source | queried | target | produced.
    /// </summary>
    [JsonIgnore]
    public StructureMapModelMode? Mode
    {
        get
        {
            var modeStr = GetProperty<string>("mode");
            return modeStr != null ? EnumUtility.ParseLiteral<StructureMapModelMode>(modeStr) : null;
        }
        set => SetProperty("mode", value?.GetLiteral());
    }

    /// <summary>
    /// Documentation on use of structure.
    /// </summary>
    [JsonIgnore]
    public string? Documentation
    {
        get => GetProperty<string>("documentation");
        set => SetProperty("documentation", value);
    }
}

/// <summary>
/// FHIR StructureMapModelMode value set.
/// </summary>
public enum StructureMapModelMode
{
    [EnumLiteral("source")]
    Source,

    [EnumLiteral("queried")]
    Queried,

    [EnumLiteral("target")]
    Target,

    [EnumLiteral("produced")]
    Produced,
}
