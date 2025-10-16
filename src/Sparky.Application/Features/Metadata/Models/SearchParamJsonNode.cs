// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Sparky.SourceNodeSerialization.Utility;

namespace Sparky.Application.Features.Metadata.Models;

/// <summary>
/// Represents a search parameter in a FHIR CapabilityStatement.
/// </summary>
public class SearchParamJsonNode
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("definition")]
    public string? Definition { get; set; }

    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SearchParamType Type { get; set; }

    [JsonPropertyName("documentation")]
    public string? Documentation { get; set; }

    /// <summary>
    /// FHIR SearchParamType value set.
    /// </summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "FHIR-defined value")]
    public enum SearchParamType
    {
        [EnumLiteral("number")]
        Number,

        [EnumLiteral("date")]
        Date,

        [EnumLiteral("string")]
        String,

        [EnumLiteral("token")]
        Token,

        [EnumLiteral("reference")]
        Reference,

        [EnumLiteral("composite")]
        Composite,

        [EnumLiteral("quantity")]
        Quantity,

        [EnumLiteral("uri")]
        Uri,

        [EnumLiteral("special")]
        Special,
    }
}
