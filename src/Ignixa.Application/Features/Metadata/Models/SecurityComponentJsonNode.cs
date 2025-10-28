// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.Features.Metadata.Models;

/// <summary>
/// Represents the security component of a FHIR CapabilityStatement REST definition.
/// </summary>
[SuppressMessage("Design", "CA2227", Justification = "Collection property for JSON serialization")]
public class SecurityComponentJsonNode : BaseJsonNode
{
    public SecurityComponentJsonNode()
    {
    }

    public SecurityComponentJsonNode(JsonObject jsonObject)
        : base(jsonObject)
    {
    }

    [JsonIgnore]
    public bool? Cors
    {
        get => MutableNode["cors"]?.GetValue<bool>();
        set => SetProperty("cors", value.HasValue ? JsonValue.Create(value.Value) : null);
    }

    [JsonIgnore]
    public string? Description
    {
        get => MutableNode["description"]?.GetValue<string>();
        set => SetProperty("description", value != null ? JsonValue.Create(value) : null);
    }

    [JsonIgnore]
    public IReadOnlyList<CodeableConceptJsonNode>? Service
    {
        get
        {
            if (!MutableNode.TryGetPropertyValue("service", out var node) || node is not JsonArray array)
            {
                return null;
            }

            var result = new List<CodeableConceptJsonNode>();
            foreach (var item in array.OfType<JsonObject>())
            {
                result.Add(new CodeableConceptJsonNode(item));
            }

            return result;
        }
    }

    /// <summary>
    /// Helper method to add a service to the underlying JSON array.
    /// This ensures the addition is persisted to the MutableNode.
    /// </summary>
    public void AddService(CodeableConceptJsonNode service)
    {
        ArgumentNullException.ThrowIfNull(service);

        if (!MutableNode.TryGetPropertyValue("service", out var node) || node is not JsonArray array)
        {
            array = new JsonArray();
            MutableNode["service"] = array;
        }

        array.Add(service.MutableNode);
    }

    /// <summary>
    /// Helper method to replace all services.
    /// </summary>
    public void SetServices(IEnumerable<CodeableConceptJsonNode> services)
    {
        if (services == null)
        {
            MutableNode.Remove("service");
        }
        else
        {
            var array = new JsonArray(services.Select(s => s.MutableNode).ToArray());
            MutableNode["service"] = array;
        }
    }
}

/// <summary>
/// Represents a CodeableConcept (simplified for CapabilityStatement).
/// </summary>
[SuppressMessage("Design", "CA2227", Justification = "Collection property for JSON serialization")]
public class CodeableConceptJsonNode : BaseJsonNode
{
    public CodeableConceptJsonNode()
    {
    }

    public CodeableConceptJsonNode(JsonObject jsonObject)
        : base(jsonObject)
    {
    }

    [JsonIgnore]
    public IReadOnlyList<CodingJsonNode>? Coding
    {
        get
        {
            if (!MutableNode.TryGetPropertyValue("coding", out var node) || node is not JsonArray array)
            {
                return null;
            }

            var result = new List<CodingJsonNode>();
            foreach (var item in array.OfType<JsonObject>())
            {
                result.Add(new CodingJsonNode(item));
            }

            return result;
        }
    }

    /// <summary>
    /// Helper method to add a coding to the underlying JSON array.
    /// This ensures the addition is persisted to the MutableNode.
    /// </summary>
    public void AddCoding(CodingJsonNode coding)
    {
        ArgumentNullException.ThrowIfNull(coding);

        if (!MutableNode.TryGetPropertyValue("coding", out var node) || node is not JsonArray array)
        {
            array = new JsonArray();
            MutableNode["coding"] = array;
        }

        array.Add(coding.MutableNode);
    }

    /// <summary>
    /// Helper method to replace all codings.
    /// </summary>
    public void SetCodings(IEnumerable<CodingJsonNode> codings)
    {
        if (codings == null)
        {
            MutableNode.Remove("coding");
        }
        else
        {
            var array = new JsonArray(codings.Select(c => c.MutableNode).ToArray());
            MutableNode["coding"] = array;
        }
    }

    [JsonIgnore]
    public string? Text
    {
        get => MutableNode["text"]?.GetValue<string>();
        set => SetProperty("text", value != null ? JsonValue.Create(value) : null);
    }
}

/// <summary>
/// Represents a Coding (simplified for CapabilityStatement).
/// </summary>
public class CodingJsonNode : BaseJsonNode
{
    public CodingJsonNode()
    {
    }

    public CodingJsonNode(JsonObject jsonObject)
        : base(jsonObject)
    {
    }

    [JsonIgnore]
    public string? System
    {
        get => MutableNode["system"]?.GetValue<string>();
        set => SetProperty("system", value != null ? JsonValue.Create(value) : null);
    }

    [JsonIgnore]
    public string? Code
    {
        get => MutableNode["code"]?.GetValue<string>();
        set => SetProperty("code", value != null ? JsonValue.Create(value) : null);
    }

    [JsonIgnore]
    public string? Display
    {
        get => MutableNode["display"]?.GetValue<string>();
        set => SetProperty("display", value != null ? JsonValue.Create(value) : null);
    }
}
