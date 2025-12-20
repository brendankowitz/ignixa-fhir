// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization.Models;

/// <summary>
/// Represents a FHIR Narrative data type.
/// A human-readable summary of the resource.
/// </summary>
public class NarrativeJsonNode : BaseJsonNode
{
    public NarrativeJsonNode()
    {
    }

    public NarrativeJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    /// <summary>
    /// The status of the narrative (generated, extensions, additional, empty).
    /// </summary>
    [JsonIgnore]
    public NarrativeStatus? Status
    {
        get
        {
            var statusStr = GetProperty<string>("status");
            return statusStr is not null ? ParseNarrativeStatus(statusStr) : null;
        }
        set
        {
            if (value is null)
            {
                MutableNode.Remove("status");
            }
            else
            {
                SetProperty("status", GetNarrativeStatusLiteral(value.Value));
            }
        }
    }

    /// <summary>
    /// Limited xhtml content (escaped HTML).
    /// </summary>
    [JsonIgnore]
    public string? Div
    {
        get => GetProperty<string>("div");
        set => SetProperty("div", value);
    }

    private static NarrativeStatus? ParseNarrativeStatus(string value) =>
        value switch
        {
            "generated" => NarrativeStatus.Generated,
            "extensions" => NarrativeStatus.Extensions,
            "additional" => NarrativeStatus.Additional,
            "empty" => NarrativeStatus.Empty,
            _ => null,
        };

    private static string GetNarrativeStatusLiteral(NarrativeStatus status) =>
        status switch
        {
            NarrativeStatus.Generated => "generated",
            NarrativeStatus.Extensions => "extensions",
            NarrativeStatus.Additional => "additional",
            NarrativeStatus.Empty => "empty",
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };

    /// <summary>
    /// The status of a narrative (FHIR NarrativeStatus value set).
    /// </summary>
    public enum NarrativeStatus
    {
        [EnumLiteral("generated")]
        Generated,

        [EnumLiteral("extensions")]
        Extensions,

        [EnumLiteral("additional")]
        Additional,

        [EnumLiteral("empty")]
        Empty,
    }
}
