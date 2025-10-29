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
        get => MutableNode["total"]?.GetValue<int>();
        set
        {
            if (value == null)
            {
                MutableNode.Remove("total");
            }
            else
            {
                MutableNode["total"] = value.Value;
            }
        }
    }

    [JsonIgnore]
    public IReadOnlyList<BundleLinkJsonNode> Link
    {
        get
        {
            if (!MutableNode.TryGetPropertyValue("link", out var linkNode) || linkNode is not JsonArray array)
            {
                return null;
            }

            var list = new List<BundleLinkJsonNode>();
            foreach (var item in array)
            {
                if (item != null)
                {
                    var json = item.ToJsonString();
                    list.Add(JsonSerializer.Deserialize<BundleLinkJsonNode>(json));
                }
            }

            return list;
        }
        set
        {
            if (value == null)
            {
                MutableNode.Remove("link");
            }
            else
            {
                var array = new JsonArray();
                foreach (var item in value)
                {
                    array.Add(item.MutableNode);
                }

                MutableNode["link"] = array;
            }
        }
    }

    [JsonIgnore]
    public IReadOnlyList<BundleComponentJsonNode> Entry
    {
        get
        {
            if (!MutableNode.TryGetPropertyValue("entry", out var entryNode) || entryNode is not JsonArray array)
            {
                return Array.Empty<BundleComponentJsonNode>();
            }

            var list = new List<BundleComponentJsonNode>();
            foreach (var item in array)
            {
                if (item != null)
                {
                    var json = item.ToJsonString();
                    list.Add(JsonSerializer.Deserialize<BundleComponentJsonNode>(json));
                }
            }

            return list;
        }
    }

    /// <summary>
    /// Helper method to add a bundle entry to the underlying JSON array.
    /// This ensures the addition is persisted to the MutableNode.
    /// </summary>
    public void AddEntry(BundleComponentJsonNode entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!MutableNode.TryGetPropertyValue("entry", out var node) || node is not JsonArray array)
        {
            array = new JsonArray();
            MutableNode["entry"] = array;
        }

        array.Add(entry.MutableNode);
    }

    /// <summary>
    /// Helper method to replace all bundle entries.
    /// </summary>
    public void SetEntries(IEnumerable<BundleComponentJsonNode> entries)
    {
        if (entries == null)
        {
            MutableNode.Remove("entry");
        }
        else
        {
            var array = new JsonArray();
            foreach (var item in entries)
            {
                array.Add(item.MutableNode);
            }

            MutableNode["entry"] = array;
        }
    }

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
