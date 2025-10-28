// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.Features.Metadata.Models;

/// <summary>
/// Represents a FHIR CapabilityStatement resource.
/// </summary>
[SuppressMessage("Design", "CA2227", Justification = "Collection properties for JSON serialization")]
public class CapabilityStatementJsonNode : ResourceJsonNode
{
    private SoftwareComponentJsonNode? _cachedSoftware;

    /// <summary>
    /// Default constructor for deserialization.
    /// </summary>
    public CapabilityStatementJsonNode()
        : base()
    {
        ResourceType = "CapabilityStatement";
    }

    [JsonIgnore]
    public string? Url
    {
        get => MutableNode["url"]?.GetValue<string>();
        set => SetProperty("url", value != null ? JsonValue.Create(value) : null);
    }

    [JsonIgnore]
    public string? Version
    {
        get => MutableNode["version"]?.GetValue<string>();
        set => SetProperty("version", value != null ? JsonValue.Create(value) : null);
    }

    [JsonIgnore]
    public string? Name
    {
        get => MutableNode["name"]?.GetValue<string>();
        set => SetProperty("name", value != null ? JsonValue.Create(value) : null);
    }

    [JsonIgnore]
    public PublicationStatus Status
    {
        get => EnumUtility.ParseLiteral<PublicationStatus>(MutableNode["status"]?.GetValue<string>()) ?? default;
        set => SetProperty("status", JsonValue.Create(value.GetLiteral()));
    }

    [JsonIgnore]
    public bool? Experimental
    {
        get => MutableNode["experimental"]?.GetValue<bool>();
        set => SetProperty("experimental", value.HasValue ? JsonValue.Create(value.Value) : null);
    }

    [JsonIgnore]
    public string? Date
    {
        get => MutableNode["date"]?.GetValue<string>();
        set => SetProperty("date", value != null ? JsonValue.Create(value) : null);
    }

    [JsonIgnore]
    public string? Publisher
    {
        get => MutableNode["publisher"]?.GetValue<string>();
        set => SetProperty("publisher", value != null ? JsonValue.Create(value) : null);
    }

    [JsonIgnore]
    public CapabilityStatementKind Kind
    {
        get => EnumUtility.ParseLiteral<CapabilityStatementKind>(MutableNode["kind"]?.GetValue<string>()) ?? default;
        set => SetProperty("kind", JsonValue.Create(value.GetLiteral()));
    }

    [JsonIgnore]
    public SoftwareComponentJsonNode? Software
    {
        get
        {
            if (_cachedSoftware == null)
            {
                if (MutableNode.TryGetPropertyValue("software", out var softwareNode) && softwareNode is JsonObject softwareObject)
                {
                    _cachedSoftware = new SoftwareComponentJsonNode(softwareObject);
                }
            }

            return _cachedSoftware;
        }
        set
        {
            if (value == null)
            {
                MutableNode.Remove("software");
                _cachedSoftware = null;
            }
            else
            {
                MutableNode["software"] = value.MutableNode;
                _cachedSoftware = value;
            }
        }
    }

    [JsonIgnore]
    public string? FhirVersionString
    {
        get => MutableNode["fhirVersion"]?.GetValue<string>();
        set => SetProperty("fhirVersion", value != null ? JsonValue.Create(value) : null);
    }

    [JsonIgnore]
    public IReadOnlyList<string>? Format
    {
        get
        {
            if (!MutableNode.TryGetPropertyValue("format", out var formatNode) || formatNode is not JsonArray formatArray)
            {
                return null;
            }

            return formatArray.Select(n => n?.GetValue<string>()).Where(s => s != null).ToList()!;
        }
    }

    /// <summary>
    /// Helper method to add a format to the underlying JSON array.
    /// This ensures the addition is persisted to the MutableNode.
    /// </summary>
    public void AddFormat(string format)
    {
        if (string.IsNullOrEmpty(format))
        {
            throw new ArgumentException("Format cannot be null or empty", nameof(format));
        }

        if (!MutableNode.TryGetPropertyValue("format", out var node) || node is not JsonArray array)
        {
            array = new JsonArray();
            MutableNode["format"] = array;
        }

        array.Add(JsonValue.Create(format));
    }

    /// <summary>
    /// Helper method to replace all formats.
    /// </summary>
    public void SetFormats(IEnumerable<string> formats)
    {
        if (formats == null)
        {
            MutableNode.Remove("format");
        }
        else
        {
            var array = new JsonArray(formats.Select(s => JsonValue.Create(s)).ToArray());
            MutableNode["format"] = array;
        }
    }

    [JsonIgnore]
    public IReadOnlyList<string>? PatchFormat
    {
        get
        {
            if (!MutableNode.TryGetPropertyValue("patchFormat", out var patchFormatNode) || patchFormatNode is not JsonArray patchFormatArray)
            {
                return null;
            }

            return patchFormatArray.Select(n => n?.GetValue<string>()).Where(s => s != null).ToList()!;
        }
    }

    /// <summary>
    /// Helper method to add a patch format to the underlying JSON array.
    /// This ensures the addition is persisted to the MutableNode.
    /// </summary>
    public void AddPatchFormat(string patchFormat)
    {
        if (string.IsNullOrEmpty(patchFormat))
        {
            throw new ArgumentException("PatchFormat cannot be null or empty", nameof(patchFormat));
        }

        if (!MutableNode.TryGetPropertyValue("patchFormat", out var node) || node is not JsonArray array)
        {
            array = new JsonArray();
            MutableNode["patchFormat"] = array;
        }

        array.Add(JsonValue.Create(patchFormat));
    }

    /// <summary>
    /// Helper method to replace all patch formats.
    /// </summary>
    public void SetPatchFormats(IEnumerable<string> patchFormats)
    {
        if (patchFormats == null)
        {
            MutableNode.Remove("patchFormat");
        }
        else
        {
            var array = new JsonArray(patchFormats.Select(s => JsonValue.Create(s)).ToArray());
            MutableNode["patchFormat"] = array;
        }
    }

    [JsonIgnore]
    public IReadOnlyList<RestComponentJsonNode>? Rest
    {
        get
        {
            if (!MutableNode.TryGetPropertyValue("rest", out var restNode) || restNode is not JsonArray restArray)
            {
                return null;
            }

            var result = new List<RestComponentJsonNode>();
            foreach (var item in restArray.OfType<JsonObject>())
            {
                result.Add(new RestComponentJsonNode(item, FhirVersion));
            }

            return result;
        }
    }

    /// <summary>
    /// Helper method to add a REST component to the underlying JSON array.
    /// This ensures the addition is persisted to the MutableNode.
    /// </summary>
    public void AddRest(RestComponentJsonNode rest)
    {
        ArgumentNullException.ThrowIfNull(rest);

        rest.FhirVersion = FhirVersion;

        if (!MutableNode.TryGetPropertyValue("rest", out var node) || node is not JsonArray array)
        {
            array = new JsonArray();
            MutableNode["rest"] = array;
        }

        array.Add(rest.MutableNode);
    }

    /// <summary>
    /// Helper method to replace all REST components.
    /// </summary>
    public void SetRest(IEnumerable<RestComponentJsonNode> rest)
    {
        if (rest == null)
        {
            MutableNode.Remove("rest");
        }
        else
        {
            // Propagate FhirVersion to child components
            var restList = rest as List<RestComponentJsonNode> ?? rest.ToList();
            foreach (var restComponent in restList)
            {
                restComponent.FhirVersion = FhirVersion;
            }

            var array = new JsonArray(restList.Select(r => r.MutableNode).ToArray());
            MutableNode["rest"] = array;
        }
    }

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
