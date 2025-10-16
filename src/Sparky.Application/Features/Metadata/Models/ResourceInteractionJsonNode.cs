// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;
using Sparky.SourceNodeSerialization.Utility;

namespace Sparky.Application.Features.Metadata.Models;

/// <summary>
/// Represents a resource-level interaction in a FHIR CapabilityStatement.
/// </summary>
public class ResourceInteractionJsonNode
{
    [JsonPropertyName("code")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TypeRestfulInteraction Code { get; set; }

    [JsonPropertyName("documentation")]
    public string? Documentation { get; set; }

    /// <summary>
    /// FHIR TypeRestfulInteraction value set (resource-level operations).
    /// </summary>
    public enum TypeRestfulInteraction
    {
        [EnumLiteral("read")]
        Read,

        [EnumLiteral("vread")]
        Vread,

        [EnumLiteral("update")]
        Update,

        [EnumLiteral("patch")]
        Patch,

        [EnumLiteral("delete")]
        Delete,

        [EnumLiteral("history-instance")]
        HistoryInstance,

        [EnumLiteral("history-type")]
        HistoryType,

        [EnumLiteral("create")]
        Create,

        [EnumLiteral("search-type")]
        SearchType,
    }
}
