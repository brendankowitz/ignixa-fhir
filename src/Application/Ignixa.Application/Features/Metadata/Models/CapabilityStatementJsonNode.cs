// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.Features.Metadata.Models;

/// <summary>
/// Represents a FHIR CapabilityStatement resource.
/// </summary>
[SuppressMessage("Design", "CA2227", Justification = "Collection properties for JSON serialization")]
public class CapabilityStatementJsonNode : ResourceJsonNode
{
    /// <summary>
    /// Default constructor for deserialization.
    /// </summary>
    public CapabilityStatementJsonNode()
        : base()
    {
        ResourceType = "CapabilityStatement";
    }

    /// <summary>
    /// Internal constructor for JsonConverter (accepts pre-parsed JsonObject).
    /// </summary>
    public CapabilityStatementJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    [JsonIgnore]
    public string? Url
    {
        get => MutableNode["url"]?.GetValue<string>();
        set => SetProperty("url", value is not null ? JsonValue.Create(value) : null);
    }

    [JsonIgnore]
    public string? Version
    {
        get => MutableNode["version"]?.GetValue<string>();
        set => SetProperty("version", value is not null ? JsonValue.Create(value) : null);
    }

    [JsonIgnore]
    public string? Name
    {
        get => MutableNode["name"]?.GetValue<string>();
        set => SetProperty("name", value is not null ? JsonValue.Create(value) : null);
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
        set => SetProperty("date", value is not null ? JsonValue.Create(value) : null);
    }

    [JsonIgnore]
    public string? Publisher
    {
        get => MutableNode["publisher"]?.GetValue<string>();
        set => SetProperty("publisher", value is not null ? JsonValue.Create(value) : null);
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
        get => GetComplexProperty<SoftwareComponentJsonNode>("software");
        set
        {
            if (value is null)
            {
                MutableNode.Remove("software");
            }
            else
            {
                value.FhirVersion = FhirVersion;
                MutableNode["software"] = value.MutableNode;
            }
        }
    }

    [JsonIgnore]
    public string? FhirVersionString
    {
        get => MutableNode["fhirVersion"]?.GetValue<string>();
        set => SetProperty("fhirVersion", value is not null ? JsonValue.Create(value) : null);
    }

    [JsonIgnore]
    public MutablePrimitiveList<string> Format => GetPrimitiveListProperty<string>("format");

    [JsonIgnore]
    public MutablePrimitiveList<string> PatchFormat => GetPrimitiveListProperty<string>("patchFormat");

    [JsonIgnore]
    public MutableJsonList<RestComponentJsonNode> Rest => GetListProperty<RestComponentJsonNode>("rest");

    /// <summary>
    /// Helper method to add a system-level operation to the first REST component.
    /// Creates the REST component if it doesn't exist.
    /// </summary>
    public void AddSystemOperation(string operationName, string definition, string? documentation = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(operationName);
        ArgumentException.ThrowIfNullOrEmpty(definition);

        // Get or create first REST component
        if (Rest.Count == 0)
        {
            Rest.Add(new RestComponentJsonNode
            {
                FhirVersion = FhirVersion,
                Mode = RestComponentJsonNode.RestfulCapabilityMode.Server,
            });
        }

        var restComponent = Rest[0];

        // Add operation to the REST component
        restComponent.Operation.Add(new OperationJsonNode
        {
            FhirVersion = FhirVersion,
            Name = operationName,
            Definition = definition,
            Documentation = documentation,
        });
    }

    /// <summary>
    /// Helper method to add a resource-level operation to a specific resource type.
    /// Adds the operation to the resource component in the first REST component.
    /// Creates the REST and resource components if they don't exist.
    /// </summary>
    public void AddResourceOperation(string resourceType, string operationName, string definition, string? documentation = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(resourceType);
        ArgumentException.ThrowIfNullOrEmpty(operationName);
        ArgumentException.ThrowIfNullOrEmpty(definition);

        // Get or create first REST component
        if (Rest.Count == 0)
        {
            Rest.Add(new RestComponentJsonNode
            {
                FhirVersion = FhirVersion,
                Mode = RestComponentJsonNode.RestfulCapabilityMode.Server,
            });
        }

        var restComponent = Rest[0];

        // Find or create resource component
        var resourceComponent = restComponent.Resource.FirstOrDefault(r => r.Type == resourceType);
        if (resourceComponent is null)
        {
            resourceComponent = new ResourceComponentJsonNode
            {
                FhirVersion = FhirVersion,
                Type = resourceType,
            };
            restComponent.Resource.Add(resourceComponent);
        }

        // Add operation to the resource component
        resourceComponent.Operation.Add(new OperationJsonNode
        {
            FhirVersion = FhirVersion,
            Name = operationName,
            Definition = definition,
            Documentation = documentation,
        });
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
