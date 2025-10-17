// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text.Json.Serialization;

namespace Ignixa.SourceNodeSerialization.SourceNodes.Models;

public class BundleComponentResponseJsonNode
{
    [JsonPropertyName("status")]
    public string Status { get; set; }

    [JsonPropertyName("location")]
    public string Location { get; set; }

    [JsonPropertyName("etag")]
    public string Etag { get; set; }

    [JsonPropertyName("lastModified")]
    public DateTimeOffset? LastModified { get; set; }

    [JsonPropertyName("outcome")]
    public ResourceJsonNode Outcome { get; set; }
}
