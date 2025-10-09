// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Sparky.SourceNodeSerialization.SourceNodes.Models.Converters;

namespace Sparky.SourceNodeSerialization.SourceNodes.Models;

[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
[SuppressMessage("Design", "CA1056", Justification = "POCO style model")]
public class BundleComponentJsonNode
{
    [JsonPropertyName("fullUrl")]
    public string FullUrl { get; set; }

    [JsonConverter(typeof(ResourceConverter))]
    [JsonPropertyName("resource")]
    public ResourceJsonNode Resource { get; set; }

    [JsonPropertyName("request")]
    public BundleComponentRequestJsonNode Request { get; set; }

    [JsonPropertyName("response")]
    public BundleComponentResponseJsonNode Response { get; set; }

    [JsonPropertyName("search")]
    public BundleComponentSearchJsonNode Search { get; set; }
}
