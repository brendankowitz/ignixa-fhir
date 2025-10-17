// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.Application.Features.Metadata.Models;

/// <summary>
/// Represents an operation definition reference in a FHIR CapabilityStatement.
/// </summary>
public class OperationJsonNode
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("definition")]
    public string? Definition { get; set; }

    [JsonPropertyName("documentation")]
    public string? Documentation { get; set; }
}
