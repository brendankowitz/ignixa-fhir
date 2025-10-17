// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Ignixa.SourceNodeSerialization.Utility;

namespace Ignixa.SourceNodeSerialization.SourceNodes.Models;

[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
[SuppressMessage("Design", "CA1819", Justification = "POCO style model")]
public class BundleJsonNode : ResourceJsonNode
{
    public BundleJsonNode()
    {
        ResourceType = "Bundle";
    }

    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BundleType? Type { get; set; }

    [JsonPropertyName("total")]
    public int? Total { get; set; }

    [JsonPropertyName("link")]
    public IReadOnlyList<BundleLinkJsonNode> Link { get; set; }

    [JsonPropertyName("entry")]
    public IList<BundleComponentJsonNode> Entry { get; set; }

    /// <summary>
    /// FHIR Bundle.type value set.
    /// </summary>
    public enum BundleType
    {
        [EnumLiteral("document")]
        Document,

        [EnumLiteral("message")]
        Message,

        [EnumLiteral("transaction")]
        Transaction,

        [EnumLiteral("transaction-response")]
        TransactionResponse,

        [EnumLiteral("batch")]
        Batch,

        [EnumLiteral("batch-response")]
        BatchResponse,

        [EnumLiteral("history")]
        History,

        [EnumLiteral("searchset")]
        Searchset,

        [EnumLiteral("collection")]
        Collection,
    }
}
