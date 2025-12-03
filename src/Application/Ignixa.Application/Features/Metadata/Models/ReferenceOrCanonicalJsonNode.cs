// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.Features.Metadata.Models;

/// <summary>
/// Represents a property that serializes differently between FHIR versions:
/// - Stu3: Reference object with "reference" and "display" properties
/// - R4+: Simple canonical string
/// Used for profile, instantiates, and implementationGuide properties.
/// </summary>
public class ReferenceOrCanonicalJsonNode : BaseJsonNode
{
    public ReferenceOrCanonicalJsonNode()
    {
    }

    public ReferenceOrCanonicalJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    /// <summary>
    /// The canonical URL or reference string.
    /// </summary>
    [JsonIgnore]
    public string? Reference
    {
        get => MutableNode["reference"]?.GetValue<string>();
        set => SetProperty("reference", value != null ? JsonValue.Create(value) : null);
    }

    /// <summary>
    /// Optional display text (primarily for Stu3 Reference objects).
    /// </summary>
    [JsonIgnore]
    public string? Display
    {
        get => MutableNode["display"]?.GetValue<string>();
        set => SetProperty("display", value != null ? JsonValue.Create(value) : null);
    }

    /// <summary>
    /// Creates a ReferenceOrCanonicalJsonNode from a canonical URL.
    /// </summary>
    public static ReferenceOrCanonicalJsonNode FromCanonical(string canonicalUrl, string? display = null)
    {
        return new ReferenceOrCanonicalJsonNode
        {
            Reference = canonicalUrl,
            Display = display,
        };
    }

    /// <summary>
    /// Implicit conversion from string to ReferenceOrCanonicalJsonNode.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2225:Operator overloads have named alternates", Justification = "FromCanonical provides named alternative")]
    public static implicit operator ReferenceOrCanonicalJsonNode?(string? canonicalUrl)
    {
        return canonicalUrl == null ? null : new ReferenceOrCanonicalJsonNode { Reference = canonicalUrl };
    }

    /// <summary>
    /// Implicit conversion from ReferenceOrCanonicalJsonNode to string.
    /// </summary>
    public static implicit operator string?(ReferenceOrCanonicalJsonNode? node)
    {
        return node?.Reference;
    }

    public override string ToString()
    {
        return Reference ?? string.Empty;
    }
}
