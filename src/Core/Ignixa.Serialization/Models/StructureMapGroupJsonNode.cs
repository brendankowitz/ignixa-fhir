// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization.Models;

/// <summary>
/// Represents a named section for groups of transforms in a StructureMap.
/// </summary>
[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
public class StructureMapGroupJsonNode : BaseJsonNode
{
    public StructureMapGroupJsonNode()
    {
    }

    /// <summary>
    /// Public constructor for JsonConverter (accepts pre-parsed JsonObject with optional FHIR version).
    /// </summary>
    public StructureMapGroupJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    /// <summary>
    /// Human-readable label for the group.
    /// </summary>
    [JsonIgnore]
    public string? Name
    {
        get => GetProperty<string>("name");
        set => SetProperty("name", value);
    }

    /// <summary>
    /// Another group that this group extends.
    /// </summary>
    [JsonIgnore]
    public string? Extends
    {
        get => GetProperty<string>("extends");
        set => SetProperty("extends", value);
    }

    /// <summary>
    /// If this is the default rule set to apply for the source type or target type.
    /// Required in R4/R4B, optional in R5+.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when set to null in FHIR R4/R4B.</exception>
    [JsonIgnore]
    public StructureMapGroupTypeMode? TypeMode
    {
        get
        {
            var typeModeStr = GetProperty<string>("typeMode");
            return typeModeStr != null ? EnumUtility.ParseLiteral<StructureMapGroupTypeMode>(typeModeStr) : null;
        }
        set
        {
            if (value == null && FhirVersion.HasValue && FhirVersion < Ignixa.Abstractions.FhirVersion.R5)
            {
                throw new ArgumentNullException(nameof(value),
                    $"TypeMode is required in {FhirVersion} and cannot be null. In R5+, this field became optional.");
            }
            SetProperty("typeMode", value?.GetLiteral());
        }
    }

    /// <summary>
    /// Additional description/explanation for the group.
    /// </summary>
    [JsonIgnore]
    public string? Documentation
    {
        get => GetProperty<string>("documentation");
        set => SetProperty("documentation", value);
    }

    /// <summary>
    /// Named inputs to the group.
    /// </summary>
    [JsonIgnore]
    public MutableJsonList<StructureMapInputJsonNode> Input => GetListProperty<StructureMapInputJsonNode>("input");

    /// <summary>
    /// Transform rules from source to target.
    /// </summary>
    [JsonIgnore]
    public MutableJsonList<StructureMapRuleJsonNode> Rule => GetListProperty<StructureMapRuleJsonNode>("rule");
}

/// <summary>
/// FHIR StructureMapGroupTypeMode value set.
/// </summary>
public enum StructureMapGroupTypeMode
{
    [EnumLiteral("none")]
    None,

    [EnumLiteral("types")]
    Types,

    [EnumLiteral("type-and-types")]
    TypeAndTypes,
}
