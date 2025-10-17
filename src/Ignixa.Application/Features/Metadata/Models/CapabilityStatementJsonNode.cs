// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Ignixa.SourceNodeSerialization.SourceNodes.Models;
using Ignixa.SourceNodeSerialization.Utility;

namespace Ignixa.Application.Features.Metadata.Models;

/// <summary>
/// Represents a FHIR CapabilityStatement resource.
/// </summary>
[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
public class CapabilityStatementJsonNode : ResourceJsonNode
{
    public CapabilityStatementJsonNode()
    {
        ResourceType = "CapabilityStatement";
    }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PublicationStatus Status { get; set; }

    [JsonPropertyName("experimental")]
    public bool? Experimental { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("publisher")]
    public string? Publisher { get; set; }

    [JsonPropertyName("kind")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CapabilityStatementKind Kind { get; set; }

    [JsonPropertyName("software")]
    public SoftwareComponentJsonNode? Software { get; set; }

    [JsonPropertyName("fhirVersion")]
    public string? FhirVersion { get; set; }

    [JsonPropertyName("format")]
    public IList<string>? Format { get; set; }

    [JsonPropertyName("patchFormat")]
    public IList<string>? PatchFormat { get; set; }

    [JsonPropertyName("rest")]
    public IList<RestComponentJsonNode>? Rest { get; set; }

    /// <summary>
    /// The status of the capability statement (FHIR PublicationStatus value set).
    /// </summary>
    public enum PublicationStatus
    {
        [EnumLiteral("draft")]
        Draft,

        [EnumLiteral("active")]
        Active,

        [EnumLiteral("retired")]
        Retired,

        [EnumLiteral("unknown")]
        Unknown,
    }

    /// <summary>
    /// The kind of capability statement (instance, capability, or requirements).
    /// </summary>
    public enum CapabilityStatementKind
    {
        [EnumLiteral("instance")]
        Instance,

        [EnumLiteral("capability")]
        Capability,

        [EnumLiteral("requirements")]
        Requirements,
    }
}
