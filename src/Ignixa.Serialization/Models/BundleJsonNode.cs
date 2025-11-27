// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization.Models;

[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
[SuppressMessage("Design", "CA1819", Justification = "POCO style model")]
public class BundleJsonNode : ResourceJsonNode
{
    public BundleJsonNode()
    {
        ResourceType = "Bundle";
    }

    /// <summary>
    /// Internal constructor for JsonConverter (accepts pre-parsed JsonObject).
    /// </summary>
    internal BundleJsonNode(JsonObject jsonObject)
        : base(jsonObject)
    {
    }

    /// <summary>
    /// Public constructor for JsonConverter (accepts pre-parsed JsonObject with optional FHIR version).
    /// </summary>
    public BundleJsonNode(JsonObject jsonObject, FhirSpecification? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    [JsonIgnore]
    public BundleType? Type
    {
        get
        {
            var typeStr = MutableNode["type"]?.GetValue<string>();
            return typeStr != null ? ParseBundleType(typeStr) : null;
        }
        set
        {
            if (value == null)
            {
                MutableNode.Remove("type");
            }
            else
            {
                MutableNode["type"] = GetEnumLiteral(value.Value);
            }
        }
    }

    [JsonIgnore]
    public int? Total
    {
        get => GetProperty<int?>("total");
        set => SetProperty("total", value);
    }

    [JsonIgnore]
    public MutableJsonList<BundleLinkJsonNode> Link => GetListProperty<BundleLinkJsonNode>("link");

    [JsonIgnore]
    public MutableJsonList<BundleComponentJsonNode> Entry => GetListProperty<BundleComponentJsonNode>("entry");

    private static BundleType? ParseBundleType(string value)
    {
        return value switch
        {
            "document" => BundleType.Document,
            "message" => BundleType.Message,
            "transaction" => BundleType.Transaction,
            "transaction-response" => BundleType.TransactionResponse,
            "batch" => BundleType.Batch,
            "batch-response" => BundleType.BatchResponse,
            "history" => BundleType.History,
            "searchset" => BundleType.Searchset,
            "collection" => BundleType.Collection,
            _ => null,
        };
    }

    private static string GetEnumLiteral(BundleType type)
    {
        return type switch
        {
            BundleType.Document => "document",
            BundleType.Message => "message",
            BundleType.Transaction => "transaction",
            BundleType.TransactionResponse => "transaction-response",
            BundleType.Batch => "batch",
            BundleType.BatchResponse => "batch-response",
            BundleType.History => "history",
            BundleType.Searchset => "searchset",
            BundleType.Collection => "collection",
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }

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
