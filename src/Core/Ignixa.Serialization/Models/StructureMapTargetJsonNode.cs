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
/// Represents a target element in a StructureMap rule.
/// </summary>
[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
public class StructureMapTargetJsonNode : BaseJsonNode
{
    public StructureMapTargetJsonNode()
    {
    }

    /// <summary>
    /// Public constructor for JsonConverter (accepts pre-parsed JsonObject with optional FHIR version).
    /// </summary>
    public StructureMapTargetJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    /// <summary>
    /// Type or variable this rule applies to.
    /// </summary>
    [JsonIgnore]
    public string? Context
    {
        get => GetProperty<string>("context");
        set => SetProperty("context", value);
    }

    /// <summary>
    /// Field to create in the context.
    /// </summary>
    [JsonIgnore]
    public string? Element
    {
        get => GetProperty<string>("element");
        set => SetProperty("element", value);
    }

    /// <summary>
    /// Named context for field, if desired.
    /// </summary>
    [JsonIgnore]
    public string? Variable
    {
        get => GetProperty<string>("variable");
        set => SetProperty("variable", value);
    }

    /// <summary>
    /// If field is a list, how to manage the list.
    /// </summary>
    [JsonIgnore]
    public MutablePrimitiveList<string> ListMode => GetPrimitiveListProperty<string>("listMode");

    /// <summary>
    /// Internal rule reference for shared list items.
    /// </summary>
    [JsonIgnore]
    public string? ListRuleId
    {
        get => GetProperty<string>("listRuleId");
        set => SetProperty("listRuleId", value);
    }

    /// <summary>
    /// Transform function to use to create the target.
    /// </summary>
    [JsonIgnore]
    public StructureMapTransform? Transform
    {
        get
        {
            var transformStr = GetProperty<string>("transform");
            return transformStr != null ? EnumUtility.ParseLiteral<StructureMapTransform>(transformStr) : null;
        }
        set => SetProperty("transform", value?.GetLiteral());
    }

    /// <summary>
    /// Parameters to the transform.
    /// </summary>
    [JsonIgnore]
    public MutableJsonList<StructureMapParameterJsonNode> Parameter => GetListProperty<StructureMapParameterJsonNode>("parameter");
}

/// <summary>
/// FHIR StructureMapTransform value set.
/// </summary>
[SuppressMessage("Naming", "CA1720", Justification = "FHIR specification defines these exact enum values")]
public enum StructureMapTransform
{
    [EnumLiteral("create")]
    Create,

    [EnumLiteral("copy")]
    Copy,

    [EnumLiteral("truncate")]
    Truncate,

    [EnumLiteral("escape")]
    Escape,

    [EnumLiteral("cast")]
    Cast,

    [EnumLiteral("append")]
    Append,

    [EnumLiteral("translate")]
    Translate,

    [EnumLiteral("reference")]
    Reference,

    [EnumLiteral("dateOp")]
    DateOp,

    [EnumLiteral("uuid")]
    Uuid,

    [EnumLiteral("pointer")]
    Pointer,

    [EnumLiteral("evaluate")]
    Evaluate,

    [EnumLiteral("cc")]
    Cc,

    [EnumLiteral("c")]
    C,

    [EnumLiteral("qty")]
    Qty,

    [EnumLiteral("id")]
    Id,

    [EnumLiteral("cp")]
    Cp,
}
