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

public class ExtensionJsonNode : BaseJsonNode
{
    public ExtensionJsonNode()
        : this(new JsonObject(), null)
    {
    }

    /// <summary>
    /// Internal constructor for JsonConverter (accepts pre-parsed JsonObject with optional FHIR version).
    /// </summary>
    public ExtensionJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "This is a POCO.")]
    [JsonIgnore]
    public string Url
    {
        get => GetProperty<string>("url");
        set => SetProperty("url", value);
    }

    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "FHIR valueUri is a string.")]
    [JsonIgnore]
    public string? ValueUri
    {
        get => GetProperty<string>("valueUri");
        set => SetProperty("valueUri", value);
    }

    [JsonIgnore]
    public string? ValueString
    {
        get => GetProperty<string>("valueString");
        set => SetProperty("valueString", value);
    }

    [JsonIgnore]
    public MutableJsonList<ExtensionJsonNode> Extension => GetListProperty<ExtensionJsonNode>("extension");
}
