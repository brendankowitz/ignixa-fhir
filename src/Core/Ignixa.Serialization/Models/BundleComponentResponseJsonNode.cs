// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization.Models;

public class BundleComponentResponseJsonNode : BaseJsonNode
{
    public BundleComponentResponseJsonNode()
        : this(new JsonObject(), null)
    {
    }

    /// <summary>
    /// Internal constructor for JsonConverter (accepts pre-parsed JsonObject with optional FHIR version).
    /// </summary>
    public BundleComponentResponseJsonNode(JsonObject jsonObject, FhirSpecification? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    [JsonIgnore]
    public string Status
    {
        get => GetProperty<string>("status");
        set => SetProperty("status", value);
    }

    [JsonIgnore]
    public string Location
    {
        get => GetProperty<string>("location");
        set => SetProperty("location", value);
    }

    [JsonIgnore]
    public string Etag
    {
        get => GetProperty<string>("etag");
        set => SetProperty("etag", value);
    }

    [JsonIgnore]
    public DateTimeOffset? LastModified
    {
        get => GetProperty<DateTimeOffset?>("lastModified");
        set
        {
            if (value == null)
            {
                MutableNode.Remove("lastModified");
            }
            else
            {
                // Store as ISO 8601 string
                MutableNode["lastModified"] = value.Value.ToString("o");
            }
        }
    }

    [JsonIgnore]
    public ResourceJsonNode Outcome
    {
        get => GetComplexProperty<ResourceJsonNode>("outcome");
        set
        {
            if (value == null)
            {
                MutableNode.Remove("outcome");
            }
            else
            {
                MutableNode["outcome"] = value.MutableNode;
            }
        }
    }
}
