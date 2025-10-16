// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Sparky.Application.Features.Metadata.Serialization;
using Sparky.SourceNodeSerialization.Utility;

namespace Sparky.Application.Features.Metadata.Models;

/// <summary>
/// Represents a resource component in a FHIR CapabilityStatement REST definition.
/// </summary>
[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
public class ResourceComponentJsonNode
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("profile")]
    [JsonConverter(typeof(ReferenceOrCanonicalConverter))]
    public ReferenceOrCanonicalJsonNode? Profile { get; set; }

    [JsonPropertyName("supportedProfile")]
    public IList<ReferenceOrCanonicalJsonNode>? SupportedProfile { get; set; }

    [JsonPropertyName("documentation")]
    public string? Documentation { get; set; }

    [JsonPropertyName("interaction")]
    public IList<ResourceInteractionJsonNode>? Interaction { get; set; }

    [JsonPropertyName("versioning")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ResourceVersionPolicy? Versioning { get; set; }

    [JsonPropertyName("readHistory")]
    public bool? ReadHistory { get; set; }

    [JsonPropertyName("updateCreate")]
    public bool? UpdateCreate { get; set; }

    [JsonPropertyName("conditionalCreate")]
    public bool? ConditionalCreate { get; set; }

    [JsonPropertyName("conditionalUpdate")]
    public bool? ConditionalUpdate { get; set; }

    [JsonPropertyName("conditionalDelete")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ConditionalDeleteStatus? ConditionalDelete { get; set; }

    [JsonPropertyName("searchInclude")]
    public IList<string>? SearchInclude { get; set; }

    [JsonPropertyName("searchRevInclude")]
    public IList<string>? SearchRevInclude { get; set; }

    [JsonPropertyName("searchParam")]
    public IList<SearchParamJsonNode>? SearchParam { get; set; }

    /// <summary>
    /// FHIR ResourceVersionPolicy value set.
    /// </summary>
    public enum ResourceVersionPolicy
    {
        [EnumLiteral("no-version")]
        NoVersion,

        [EnumLiteral("versioned")]
        Versioned,

        [EnumLiteral("versioned-update")]
        VersionedUpdate,
    }

    /// <summary>
    /// FHIR ConditionalDeleteStatus value set.
    /// </summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "FHIR-defined value")]
    public enum ConditionalDeleteStatus
    {
        [EnumLiteral("not-supported")]
        NotSupported,

        [EnumLiteral("single")]
        Single,

        [EnumLiteral("multiple")]
        Multiple,
    }
}
