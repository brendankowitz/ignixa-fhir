// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Abstractions;
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
    public MetaJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
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
                // Convert to UTC explicitly to ensure consistent timezone representation
                // This prevents local timezone offset from appearing in the serialized output
                var utcValue = value.Value.ToUniversalTime();
                MutableNode["lastUpdated"] = utcValue.ToString("o");
            }
        }
    }

    [JsonIgnore]
    public MutablePrimitiveList<string> Profiles => GetPrimitiveListProperty<string>("profile");

    [JsonIgnore]
    public MutablePrimitiveList<string> Security => GetPrimitiveListProperty<string>("security");

    [JsonIgnore]
    public MutablePrimitiveList<string> Tags => GetPrimitiveListProperty<string>("tag");

    [JsonIgnore]
    public string? Source
    {
        get => GetProperty<string>("source");
        set => SetProperty("source", value);
    }
}
