// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Sparky.SourceNodeSerialization.Utility;

namespace Sparky.Application.Features.Metadata.Models;

/// <summary>
/// Represents the REST component of a FHIR CapabilityStatement.
/// </summary>
[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
public class RestComponentJsonNode
{
    [JsonPropertyName("mode")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RestfulCapabilityMode Mode { get; set; }

    [JsonPropertyName("documentation")]
    public string? Documentation { get; set; }

    [JsonPropertyName("security")]
    public SecurityComponentJsonNode? Security { get; set; }

    [JsonPropertyName("resource")]
    public IList<ResourceComponentJsonNode>? Resource { get; set; }

    [JsonPropertyName("interaction")]
    public IList<SystemInteractionJsonNode>? Interaction { get; set; }

    [JsonPropertyName("searchParam")]
    public IList<SearchParamJsonNode>? SearchParam { get; set; }

    [JsonPropertyName("operation")]
    public IList<OperationJsonNode>? Operation { get; set; }

    /// <summary>
    /// The mode of the REST component (client or server).
    /// </summary>
    public enum RestfulCapabilityMode
    {
        [EnumLiteral("client")]
        Client,

        [EnumLiteral("server")]
        Server,
    }
}
