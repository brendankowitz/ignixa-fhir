// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;
using Ignixa.SourceNodeSerialization.Utility;

namespace Ignixa.Application.Features.Metadata.Models;

/// <summary>
/// Represents a system-level interaction in a FHIR CapabilityStatement.
/// </summary>
public class SystemInteractionJsonNode
{
    [JsonPropertyName("code")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SystemRestfulInteraction Code { get; set; }

    [JsonPropertyName("documentation")]
    public string? Documentation { get; set; }

    /// <summary>
    /// FHIR SystemRestfulInteraction value set (system-level operations).
    /// </summary>
    public enum SystemRestfulInteraction
    {
        [EnumLiteral("transaction")]
        Transaction,

        [EnumLiteral("batch")]
        Batch,

        [EnumLiteral("search-system")]
        SearchSystem,

        [EnumLiteral("history-system")]
        HistorySystem,
    }
}
