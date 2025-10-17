// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Ignixa.Application.Features.Metadata.Models;

/// <summary>
/// Represents the security component of a FHIR CapabilityStatement REST definition.
/// </summary>
[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
public class SecurityComponentJsonNode
{
    [JsonPropertyName("cors")]
    public bool? Cors { get; set; }

    [JsonPropertyName("service")]
    public IList<CodeableConceptJsonNode>? Service { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// Represents a CodeableConcept (simplified for CapabilityStatement).
/// </summary>
[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
public class CodeableConceptJsonNode
{
    [JsonPropertyName("coding")]
    public IList<CodingJsonNode>? Coding { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

/// <summary>
/// Represents a Coding (simplified for CapabilityStatement).
/// </summary>
public class CodingJsonNode
{
    [JsonPropertyName("system")]
    public string? System { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("display")]
    public string? Display { get; set; }
}
