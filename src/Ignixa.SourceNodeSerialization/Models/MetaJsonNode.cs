// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.SourceNodeSerialization.SourceNodes;

namespace Ignixa.SourceNodeSerialization.Models;

[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
public class MetaJsonNode : BaseJsonNode
{
    /// <summary>
    /// Default constructor for deserialization.
    /// </summary>
    public MetaJsonNode()
        : base()
    {
    }

    /// <summary>
    /// Internal constructor for wrapping existing JsonObject (used by ResourceJsonNode.Meta).
    /// </summary>
    internal MetaJsonNode(JsonObject jsonObject)
        : base(jsonObject)
    {
    }

    [JsonIgnore]
    public string? VersionId
    {
        get => MutableNode["versionId"]?.GetValue<string>();
        set
        {
            if (value == null)
            {
                MutableNode.Remove("versionId");
            }
            else
            {
                MutableNode["versionId"] = value;
            }
        }
    }

    [JsonIgnore]
    public DateTimeOffset? LastUpdated
    {
        get
        {
            var internalNode = MutableNode;
            if (internalNode.TryGetPropertyValue("lastUpdated", out var node) && node != null)
            {
                var value = node.GetValue<string>();
                if (DateTimeOffset.TryParse(value, out var result))
                {
                    return result;
                }
            }

            return null;
        }
        set
        {
            if (value == null)
            {
                MutableNode.Remove("lastUpdated");
            }
            else
            {
                // Store as ISO 8601 string in UTC format (FHIR requires UTC)
                // Use UtcDateTime to get the DateTime in UTC, then create DateTimeOffset with zero offset
                MutableNode["lastUpdated"] = value.Value.ToString("o");
            }
        }
    }

}
