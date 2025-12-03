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
/// Strongly-typed model for FHIR StructureMap resource.
/// Represents a map from one set of concepts to one or more other concepts.
/// </summary>
[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
public class StructureMapJsonNode : ResourceJsonNode
{
    public StructureMapJsonNode()
    {
        ResourceType = "StructureMap";
    }

    /// <summary>
    /// Internal constructor for JsonConverter (accepts pre-parsed JsonObject).
    /// </summary>
    internal StructureMapJsonNode(JsonObject jsonObject)
        : base(jsonObject)
    {
    }

    /// <summary>
    /// Public constructor for JsonConverter (accepts pre-parsed JsonObject with optional FHIR version).
    /// </summary>
    public StructureMapJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    /// <summary>
    /// Canonical identifier for this structure map (globally unique).
    /// </summary>
    [JsonIgnore]
    public string? Url
    {
        get => GetProperty<string>("url");
        set => SetProperty("url", value);
    }

    /// <summary>
    /// Name for this structure map (computer friendly).
    /// </summary>
    [JsonIgnore]
    public string? Name
    {
        get => GetProperty<string>("name");
        set => SetProperty("name", value);
    }

    /// <summary>
    /// Business version of the structure map.
    /// </summary>
    [JsonIgnore]
    public string? Version
    {
        get => GetProperty<string>("version");
        set => SetProperty("version", value);
    }

    /// <summary>
    /// Name for this structure map (human friendly).
    /// </summary>
    [JsonIgnore]
    public string? Title
    {
        get => GetProperty<string>("title");
        set => SetProperty("title", value);
    }

    /// <summary>
    /// Publication status: draft | active | retired | unknown.
    /// </summary>
    [JsonIgnore]
    public PublicationStatus? Status
    {
        get
        {
            var statusStr = GetProperty<string>("status");
            return statusStr != null ? EnumUtility.ParseLiteral<PublicationStatus>(statusStr) : null;
        }
        set => SetProperty("status", value?.GetLiteral());
    }

    /// <summary>
    /// Natural language description of the structure map.
    /// </summary>
    [JsonIgnore]
    public string? Description
    {
        get => GetProperty<string>("description");
        set => SetProperty("description", value);
    }

    /// <summary>
    /// Version algorithm indicator (R5+ only).
    /// Can be a string or Coding indicating how to compare versions.
    /// </summary>
    /// <exception cref="NotSupportedException">Thrown when accessed in FHIR versions prior to R5.</exception>
    [JsonIgnore]
    public string? VersionAlgorithmString
    {
        get
        {
            if (FhirVersion.HasValue && FhirVersion < Ignixa.Abstractions.FhirVersion.R5)
            {
                throw new NotSupportedException(
                    $"VersionAlgorithmString is not supported in {FhirVersion}. This property was introduced in FHIR R5.");
            }
            return GetProperty<string>("versionAlgorithmString");
        }
        set
        {
            if (FhirVersion.HasValue && FhirVersion < Ignixa.Abstractions.FhirVersion.R5)
            {
                throw new NotSupportedException(
                    $"VersionAlgorithmString is not supported in {FhirVersion}. This property was introduced in FHIR R5.");
            }
            SetProperty("versionAlgorithmString", value);
        }
    }

    /// <summary>
    /// Copyright label for the structure map (R5+ only).
    /// Short statement of copyright/IP ownership.
    /// </summary>
    /// <exception cref="NotSupportedException">Thrown when accessed in FHIR versions prior to R5.</exception>
    [JsonIgnore]
    public string? CopyrightLabel
    {
        get
        {
            if (FhirVersion.HasValue && FhirVersion < Ignixa.Abstractions.FhirVersion.R5)
            {
                throw new NotSupportedException(
                    $"CopyrightLabel is not supported in {FhirVersion}. This property was introduced in FHIR R5.");
            }
            return GetProperty<string>("copyrightLabel");
        }
        set
        {
            if (FhirVersion.HasValue && FhirVersion < Ignixa.Abstractions.FhirVersion.R5)
            {
                throw new NotSupportedException(
                    $"CopyrightLabel is not supported in {FhirVersion}. This property was introduced in FHIR R5.");
            }
            SetProperty("copyrightLabel", value);
        }
    }

    /// <summary>
    /// Constants in this structure map (R5+ only).
    /// Defines named FHIRPath expressions that can be referenced throughout the map.
    /// </summary>
    /// <exception cref="NotSupportedException">Thrown when accessed in FHIR versions prior to R5.</exception>
    [JsonIgnore]
    public MutableJsonList<StructureMapConstJsonNode> Const
    {
        get
        {
            if (FhirVersion.HasValue && FhirVersion < Ignixa.Abstractions.FhirVersion.R5)
            {
                throw new NotSupportedException(
                    $"Const is not supported in {FhirVersion}. Constants were introduced in FHIR R5.");
            }
            return GetListProperty<StructureMapConstJsonNode>("const");
        }
    }

    /// <summary>
    /// Structure definition used by this map.
    /// </summary>
    [JsonIgnore]
    public MutableJsonList<StructureMapStructureJsonNode> Structure => GetListProperty<StructureMapStructureJsonNode>("structure");

    /// <summary>
    /// Other maps used by this map (canonical URLs).
    /// </summary>
    [JsonIgnore]
    public MutablePrimitiveList<string> Import => GetPrimitiveListProperty<string>("import");

    /// <summary>
    /// Named sections for groups of transforms.
    /// </summary>
    [JsonIgnore]
    public MutableJsonList<StructureMapGroupJsonNode> Group => GetListProperty<StructureMapGroupJsonNode>("group");

    /// <summary>
    /// Contained resources.
    /// </summary>
    [JsonIgnore]
    public MutableJsonList<ResourceJsonNode> Contained => GetListProperty<ResourceJsonNode>("contained");
}

/// <summary>
/// FHIR PublicationStatus value set.
/// </summary>
public enum PublicationStatus
{
    [EnumLiteral("draft")]
    Draft,

    [EnumLiteral("active")]
    Active,

    [EnumLiteral("retired")]
    Retired,

    [EnumLiteral("unknown")]
    Unknown,
}
