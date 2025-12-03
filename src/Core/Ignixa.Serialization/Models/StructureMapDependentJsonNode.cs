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
/// Represents a dependent group call in a StructureMap rule.
/// Specifies which other rules to apply in the context of this rule.
/// </summary>
[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
public class StructureMapDependentJsonNode : BaseJsonNode
{
    public StructureMapDependentJsonNode()
    {
    }

    /// <summary>
    /// Public constructor for JsonConverter (accepts pre-parsed JsonObject with optional FHIR version).
    /// </summary>
    public StructureMapDependentJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    /// <summary>
    /// Name of a rule or group to apply.
    /// </summary>
    [JsonIgnore]
    public string? Name
    {
        get => GetProperty<string>("name");
        set => SetProperty("name", value);
    }

    /// <summary>
    /// Variables to pass to the rule or group (R4/R4B only).
    /// Simple string array of variable names.
    /// In R5+, use Parameter property instead.
    /// </summary>
    /// <exception cref="NotSupportedException">Thrown when accessed in FHIR R5 or later.</exception>
    [JsonIgnore]
    public MutablePrimitiveList<string> Variable
    {
        get
        {
            if (FhirVersion.HasValue && FhirVersion >= Ignixa.Abstractions.FhirVersion.R5)
            {
                throw new NotSupportedException(
                    $"Variable is not supported in {FhirVersion}. In R5+, use the Parameter property instead for structured parameters.");
            }
            return GetPrimitiveListProperty<string>("variable");
        }
    }

    /// <summary>
    /// Parameters to pass to the rule or group (R5+ only).
    /// Structured parameters with typed values.
    /// In R4/R4B, use Variable property instead.
    /// </summary>
    /// <exception cref="NotSupportedException">Thrown when accessed in FHIR versions prior to R5.</exception>
    [JsonIgnore]
    public MutableJsonList<StructureMapParameterJsonNode> Parameter
    {
        get
        {
            if (FhirVersion.HasValue && FhirVersion < Ignixa.Abstractions.FhirVersion.R5)
            {
                throw new NotSupportedException(
                    $"Parameter is not supported in {FhirVersion}. In R4/R4B, use the Variable property instead for simple string variables.");
            }
            return GetListProperty<StructureMapParameterJsonNode>("parameter");
        }
    }
}
