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
[SuppressMessage("Design", "CA1056", Justification = "POCO style model")]
public class BundleComponentRequestJsonNode : BaseJsonNode
{
    public BundleComponentRequestJsonNode()
        : this(new JsonObject(), null)
    {
    }

    /// <summary>
    /// Internal constructor for JsonConverter (accepts pre-parsed JsonObject with optional FHIR version).
    /// </summary>
    public BundleComponentRequestJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }
    [JsonIgnore]
    public string Method
    {
        get => GetProperty<string>("method");
        set => SetProperty("method", value);
    }

    [JsonIgnore]
    public string Url
    {
        get => GetProperty<string>("url");
        set => SetProperty("url", value);
    }
}
