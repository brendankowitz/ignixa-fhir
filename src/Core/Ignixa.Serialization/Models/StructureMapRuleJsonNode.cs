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
/// Represents a transformation rule in a StructureMap group.
/// </summary>
[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
public class StructureMapRuleJsonNode : BaseJsonNode
{
    public StructureMapRuleJsonNode()
    {
    }

    /// <summary>
    /// Public constructor for JsonConverter (accepts pre-parsed JsonObject with optional FHIR version).
    /// </summary>
    public StructureMapRuleJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    /// <summary>
    /// Name of the rule for internal references.
    /// Required in R4/R4B, optional in R5+.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when set to null in FHIR R4/R4B.</exception>
    [JsonIgnore]
    public string? Name
    {
        get => GetProperty<string>("name");
        set
        {
            if (string.IsNullOrEmpty(value) && FhirVersion.HasValue && FhirVersion < Ignixa.Abstractions.FhirVersion.R5)
            {
                throw new ArgumentNullException(nameof(value),
                    $"Name is required in {FhirVersion} and cannot be null or empty. In R5+, this field became optional.");
            }
            SetProperty("name", value);
        }
    }

    /// <summary>
    /// Source inputs to the rule.
    /// </summary>
    [JsonIgnore]
    public MutableJsonList<StructureMapSourceJsonNode> Source => GetListProperty<StructureMapSourceJsonNode>("source");

    /// <summary>
    /// Target outputs from the rule.
    /// </summary>
    [JsonIgnore]
    public MutableJsonList<StructureMapTargetJsonNode> Target => GetListProperty<StructureMapTargetJsonNode>("target");

    /// <summary>
    /// Rules contained in this rule (nested rules).
    /// </summary>
    [JsonIgnore]
    [SuppressMessage("Naming", "CA1721:Property names should not match method names", Justification = "FHIR specification uses 'rule'")]
    public MutableJsonList<StructureMapRuleJsonNode> Rule => GetListProperty<StructureMapRuleJsonNode>("rule");

    /// <summary>
    /// Which other rules to apply in the context of this rule.
    /// </summary>
    [JsonIgnore]
    public MutableJsonList<StructureMapDependentJsonNode> Dependent => GetListProperty<StructureMapDependentJsonNode>("dependent");

    /// <summary>
    /// Documentation for this rule.
    /// </summary>
    [JsonIgnore]
    public string? Documentation
    {
        get => GetProperty<string>("documentation");
        set => SetProperty("documentation", value);
    }
}
