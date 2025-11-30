// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization.Models;

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
    /// Public constructor for wrapping existing JsonObject (used by ResourceJsonNode.Meta).
    /// </summary>
    public MetaJsonNode(JsonObject jsonObject)
        : this(jsonObject, null)
    {
    }

    /// <summary>
    /// Internal constructor for JsonConverter (accepts pre-parsed JsonObject with optional FHIR version).
    /// </summary>
    public MetaJsonNode(JsonObject jsonObject, FhirSpecification? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    [JsonIgnore]
    public string? VersionId
    {
        get => GetProperty<string>("versionId");
        set => SetProperty("versionId", value);
    }

    [JsonIgnore]
    public DateTimeOffset? LastUpdated
    {
        get
        {
            if (MutableNode.TryGetPropertyValue("lastUpdated", out var node) && node is JsonValue valueNode)
            {
                var value = valueNode.GetValue<string>();
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
