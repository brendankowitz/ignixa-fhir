// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Application.Features.Metadata.Models;

/// <summary>
/// Represents a resource component in a FHIR CapabilityStatement REST definition.
/// </summary>
[SuppressMessage("Design", "CA2227", Justification = "Collection properties for JSON serialization")]
public class ResourceComponentJsonNode : BaseJsonNode
{
    public ResourceComponentJsonNode()
    {
    }

    public ResourceComponentJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    [JsonIgnore]
    public string? Type
    {
        get => MutableNode["type"]?.GetValue<string>();
        set => SetProperty("type", value is not null ? JsonValue.Create(value) : null);
    }

    [JsonIgnore]
    public ReferenceOrCanonicalJsonNode? Profile
    {
        get
        {
            if (!MutableNode.TryGetPropertyValue("profile", out var node))
            {
                return null;
            }

            // Handle both string (canonical) and object (Reference) forms
            if (node is JsonValue)
            {
                var canonical = node.GetValue<string>();
                return new ReferenceOrCanonicalJsonNode { Reference = canonical };
            }
            else if (node is JsonObject jsonObject)
            {
                return new ReferenceOrCanonicalJsonNode(jsonObject, FhirVersion);
            }

            return null;
        }
        set
        {
            if (value is null)
            {
                MutableNode.Remove("profile");
            }
            else
            {
                // VERSION-AWARE: Use FhirVersion to determine storage format
                // Stu3: Store as Reference object (with reference and display)
                // R4/R4B/R5: Store as canonical string
                if (FhirVersion == Ignixa.Abstractions.FhirVersion.Stu3)
                {
                    // Stu3: Always use object form
                    value.FhirVersion = FhirVersion;
                    MutableNode["profile"] = value.MutableNode;
                }
                else
                {
                    // R4/R4B/R5: Use canonical string form
                    MutableNode["profile"] = JsonValue.Create(value.Reference);
                }
            }
        }
    }

    [JsonIgnore]
    public IReadOnlyList<ReferenceOrCanonicalJsonNode>? SupportedProfile
    {
        get
        {
            if (!MutableNode.TryGetPropertyValue("supportedProfile", out var node) || node is not JsonArray array)
            {
                return null;
            }

            var result = new List<ReferenceOrCanonicalJsonNode>();
            foreach (var item in array)
            {
                if (item is JsonValue)
                {
                    var canonical = item.GetValue<string>();
                    result.Add(new ReferenceOrCanonicalJsonNode { Reference = canonical });
                }
                else if (item is JsonObject jsonObject)
                {
                    result.Add(new ReferenceOrCanonicalJsonNode(jsonObject, FhirVersion));
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Helper method to add a supported profile to the underlying JSON array.
    /// This ensures the addition is persisted to the MutableNode.
    /// </summary>
    public void AddSupportedProfile(ReferenceOrCanonicalJsonNode supportedProfile)
    {
        ArgumentNullException.ThrowIfNull(supportedProfile);

        supportedProfile.FhirVersion = FhirVersion;

        if (!MutableNode.TryGetPropertyValue("supportedProfile", out var node) || node is not JsonArray array)
        {
            array = [];
            MutableNode["supportedProfile"] = array;
        }

        // VERSION-AWARE: Use FhirVersion to determine storage format
        if (FhirVersion == Ignixa.Abstractions.FhirVersion.Stu3)
        {
            array.Add(supportedProfile.MutableNode);
        }
        else
        {
            array.Add(JsonValue.Create(supportedProfile.Reference));
        }
    }

    /// <summary>
    /// Helper method to replace all supported profiles.
    /// </summary>
    public void SetSupportedProfiles(IEnumerable<ReferenceOrCanonicalJsonNode> supportedProfiles)
    {
        if (supportedProfiles is null)
        {
            MutableNode.Remove("supportedProfile");
        }
        else
        {
            var array = new JsonArray();
            foreach (var item in supportedProfiles)
            {
                item.FhirVersion = FhirVersion;
                // VERSION-AWARE: Use FhirVersion to determine storage format
                if (FhirVersion == Ignixa.Abstractions.FhirVersion.Stu3)
                {
                    array.Add(item.MutableNode);
                }
                else
                {
                    array.Add(JsonValue.Create(item.Reference));
                }
            }

            MutableNode["supportedProfile"] = array;
        }
    }

    [JsonIgnore]
    public string? Documentation
    {
        get => MutableNode["documentation"]?.GetValue<string>();
        set => SetProperty("documentation", value is not null ? JsonValue.Create(value) : null);
    }

    [JsonIgnore]
    public MutableJsonList<ResourceInteractionJsonNode> Interaction => GetListProperty<ResourceInteractionJsonNode>("interaction");

    [JsonIgnore]
    public ResourceVersionPolicy? Versioning
    {
        get => EnumUtility.ParseLiteral<ResourceVersionPolicy>(MutableNode["versioning"]?.GetValue<string>());
        set => SetProperty("versioning", value.HasValue ? JsonValue.Create(value.Value.GetLiteral()) : null);
    }

    [JsonIgnore]
    public bool? ReadHistory
    {
        get => MutableNode["readHistory"]?.GetValue<bool>();
        set => SetProperty("readHistory", value.HasValue ? JsonValue.Create(value.Value) : null);
    }

    [JsonIgnore]
    public bool? UpdateCreate
    {
        get => MutableNode["updateCreate"]?.GetValue<bool>();
        set => SetProperty("updateCreate", value.HasValue ? JsonValue.Create(value.Value) : null);
    }

    [JsonIgnore]
    public bool? ConditionalCreate
    {
        get => MutableNode["conditionalCreate"]?.GetValue<bool>();
        set => SetProperty("conditionalCreate", value.HasValue ? JsonValue.Create(value.Value) : null);
    }

    [JsonIgnore]
    public bool? ConditionalUpdate
    {
        get => MutableNode["conditionalUpdate"]?.GetValue<bool>();
        set => SetProperty("conditionalUpdate", value.HasValue ? JsonValue.Create(value.Value) : null);
    }

    [JsonIgnore]
    public ConditionalDeleteStatus? ConditionalDelete
    {
        get => EnumUtility.ParseLiteral<ConditionalDeleteStatus>(MutableNode["conditionalDelete"]?.GetValue<string>());
        set => SetProperty("conditionalDelete", value.HasValue ? JsonValue.Create(value.Value.GetLiteral()) : null);
    }

    [JsonIgnore]
    public MutablePrimitiveList<string> SearchInclude => GetPrimitiveListProperty<string>("searchInclude");

    [JsonIgnore]
    public MutablePrimitiveList<string> SearchRevInclude => GetPrimitiveListProperty<string>("searchRevInclude");

    [JsonIgnore]
    public MutableJsonList<SearchParamJsonNode> SearchParam => GetListProperty<SearchParamJsonNode>("searchParam");

    [JsonIgnore]
    public MutableJsonList<OperationJsonNode> Operation => GetListProperty<OperationJsonNode>("operation");

    /// <summary>
    /// FHIR ResourceVersionPolicy value set.
    /// </summary>
    public enum ResourceVersionPolicy
    {
        [EnumLiteral("no-version")]
        NoVersion,

        [EnumLiteral("versioned")]
        Versioned,

        [EnumLiteral("versioned-update")]
        VersionedUpdate,
    }
}
