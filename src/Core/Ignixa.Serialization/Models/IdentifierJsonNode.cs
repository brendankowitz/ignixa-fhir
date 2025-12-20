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
/// Represents a FHIR Identifier data type.
/// A technical identifier - identifies some entity uniquely and unambiguously.
/// </summary>
public class IdentifierJsonNode : BaseJsonNode
{
    public IdentifierJsonNode()
    {
    }

    public IdentifierJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    /// <summary>
    /// The purpose of this identifier (usual | official | temp | secondary | old).
    /// </summary>
    [JsonIgnore]
    public string? Use
    {
        get => GetProperty<string>("use");
        set => SetProperty("use", value);
    }

    /// <summary>
    /// Description of identifier.
    /// </summary>
    [JsonIgnore]
    public CodeableConceptJsonNode? Type
    {
        get => GetComplexProperty<CodeableConceptJsonNode>("type");
        set
        {
            if (value is null)
            {
                MutableNode.Remove("type");
            }
            else
            {
                MutableNode["type"] = value.MutableNode;
            }
        }
    }

    /// <summary>
    /// The namespace for the identifier value.
    /// </summary>
    [JsonIgnore]
    public string? System
    {
        get => GetProperty<string>("system");
        set => SetProperty("system", value);
    }

    /// <summary>
    /// The value that is unique.
    /// </summary>
    [JsonIgnore]
    public string? Value
    {
        get => GetProperty<string>("value");
        set => SetProperty("value", value);
    }

    /// <summary>
    /// Organization that issued id (may be just text).
    /// </summary>
    [JsonIgnore]
    public ReferenceJsonNode? Assigner
    {
        get => GetComplexProperty<ReferenceJsonNode>("assigner");
        set
        {
            if (value is null)
            {
                MutableNode.Remove("assigner");
            }
            else
            {
                MutableNode["assigner"] = value.MutableNode;
            }
        }
    }
}
