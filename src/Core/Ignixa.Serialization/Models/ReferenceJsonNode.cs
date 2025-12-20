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
/// Represents a FHIR Reference data type.
/// A reference from one resource to another.
/// </summary>
public class ReferenceJsonNode : BaseJsonNode
{
    public ReferenceJsonNode()
    {
    }

    public ReferenceJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    /// <summary>
    /// Literal reference, Relative, internal or absolute URL.
    /// </summary>
    [JsonIgnore]
    public string? Reference
    {
        get => GetProperty<string>("reference");
        set => SetProperty("reference", value);
    }

    /// <summary>
    /// Type the reference refers to (e.g. "Patient").
    /// </summary>
    [JsonIgnore]
    public string? Type
    {
        get => GetProperty<string>("type");
        set => SetProperty("type", value);
    }

    /// <summary>
    /// Logical reference, when literal reference is not known.
    /// </summary>
    [JsonIgnore]
    public IdentifierJsonNode? Identifier
    {
        get => GetComplexProperty<IdentifierJsonNode>("identifier");
        set
        {
            if (value is null)
            {
                MutableNode.Remove("identifier");
            }
            else
            {
                MutableNode["identifier"] = value.MutableNode;
            }
        }
    }

    /// <summary>
    /// Text alternative for the resource.
    /// </summary>
    [JsonIgnore]
    public string? Display
    {
        get => GetProperty<string>("display");
        set => SetProperty("display", value);
    }

    /// <summary>
    /// Creates a ReferenceJsonNode from a resource type and id.
    /// </summary>
    public static ReferenceJsonNode FromResourceTypeAndId(string resourceType, string id)
    {
        ArgumentNullException.ThrowIfNull(resourceType);
        ArgumentNullException.ThrowIfNull(id);

        return new ReferenceJsonNode
        {
            Reference = $"{resourceType}/{id}"
        };
    }
}
