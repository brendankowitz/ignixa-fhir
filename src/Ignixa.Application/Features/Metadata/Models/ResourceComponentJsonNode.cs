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
using Ignixa.SourceNodeSerialization;
using Ignixa.SourceNodeSerialization.SourceNodes;
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

    public ResourceComponentJsonNode(JsonObject jsonObject)
        : base(jsonObject)
    {
    }

    public ResourceComponentJsonNode(JsonObject jsonObject, FhirSpecification? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    [JsonIgnore]
    public string? Type
    {
        get => MutableNode["type"]?.GetValue<string>();
        set => SetProperty("type", value != null ? JsonValue.Create(value) : null);
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
            if (value == null)
            {
                MutableNode.Remove("profile");
            }
            else
            {
                // VERSION-AWARE: Use FhirVersion to determine storage format
                // STU3: Store as Reference object (with reference and display)
                // R4/R4B/R5: Store as canonical string
                if (FhirVersion == FhirSpecification.Stu3)
                {
                    // STU3: Always use object form
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
    public IList<ReferenceOrCanonicalJsonNode>? SupportedProfile
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
        set
        {
            if (value == null)
            {
                MutableNode.Remove("supportedProfile");
            }
            else
            {
                var array = new JsonArray();
                foreach (var item in value)
                {
                    // VERSION-AWARE: Use FhirVersion to determine storage format
                    // STU3: Store as Reference object (with reference and display)
                    // R4/R4B/R5: Store as canonical string
                    if (FhirVersion == FhirSpecification.Stu3)
                    {
                        // STU3: Always use object form
                        item.FhirVersion = FhirVersion;
                        array.Add(item.MutableNode);
                    }
                    else
                    {
                        // R4/R4B/R5: Use canonical string form
                        array.Add(JsonValue.Create(item.Reference));
                    }
                }

                MutableNode["supportedProfile"] = array;
            }
        }
    }

    [JsonIgnore]
    public string? Documentation
    {
        get => MutableNode["documentation"]?.GetValue<string>();
        set => SetProperty("documentation", value != null ? JsonValue.Create(value) : null);
    }

    [JsonIgnore]
    public IReadOnlyList<ResourceInteractionJsonNode>? Interaction
    {
        get
        {
            if (!MutableNode.TryGetPropertyValue("interaction", out var node) || node is not JsonArray array)
            {
                return null;
            }

            var result = new List<ResourceInteractionJsonNode>();
            foreach (var item in array.OfType<JsonObject>())
            {
                result.Add(new ResourceInteractionJsonNode(item, FhirVersion));
            }

            return result;
        }
        set
        {
            if (value == null)
            {
                MutableNode.Remove("interaction");
            }
            else
            {
                // Propagate FhirVersion to child components
                foreach (var interaction in value)
                {
                    interaction.FhirVersion = FhirVersion;
                }

                var array = new JsonArray(value.Select(i => i.MutableNode).ToArray());
                MutableNode["interaction"] = array;
            }
        }
    }

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
    public IReadOnlyList<string>? SearchInclude
    {
        get
        {
            if (!MutableNode.TryGetPropertyValue("searchInclude", out var node) || node is not JsonArray array)
            {
                return null;
            }

            return array.Select(n => n?.GetValue<string>()).Where(s => s != null).ToList()!;
        }
        set
        {
            if (value == null)
            {
                MutableNode.Remove("searchInclude");
            }
            else
            {
                var array = new JsonArray(value.Select(s => JsonValue.Create(s)).ToArray());
                MutableNode["searchInclude"] = array;
            }
        }
    }

    [JsonIgnore]
    public IReadOnlyList<string>? SearchRevInclude
    {
        get
        {
            if (!MutableNode.TryGetPropertyValue("searchRevInclude", out var node) || node is not JsonArray array)
            {
                return null;
            }

            return array.Select(n => n?.GetValue<string>()).Where(s => s != null).ToList()!;
        }
        set
        {
            if (value == null)
            {
                MutableNode.Remove("searchRevInclude");
            }
            else
            {
                var array = new JsonArray(value.Select(s => JsonValue.Create(s)).ToArray());
                MutableNode["searchRevInclude"] = array;
            }
        }
    }

    [JsonIgnore]
    public IReadOnlyList<SearchParamJsonNode>? SearchParam
    {
        get
        {
            if (!MutableNode.TryGetPropertyValue("searchParam", out var node) || node is not JsonArray array)
            {
                return null;
            }

            var result = new List<SearchParamJsonNode>();
            foreach (var item in array.OfType<JsonObject>())
            {
                result.Add(new SearchParamJsonNode(item, FhirVersion));
            }

            return result;
        }
        set
        {
            if (value == null)
            {
                MutableNode.Remove("searchParam");
            }
            else
            {
                // Propagate FhirVersion to child components
                foreach (var param in value)
                {
                    param.FhirVersion = FhirVersion;
                }

                var array = new JsonArray(value.Select(s => s.MutableNode).ToArray());
                MutableNode["searchParam"] = array;
            }
        }
    }

    /// <summary>
    /// Helper method to add a search parameter to the underlying JSON array.
    /// This ensures the addition is persisted to the MutableNode.
    /// </summary>
    public void AddSearchParam(SearchParamJsonNode searchParam)
    {
        ArgumentNullException.ThrowIfNull(searchParam);

        searchParam.FhirVersion = FhirVersion;

        if (!MutableNode.TryGetPropertyValue("searchParam", out var node) || node is not JsonArray array)
        {
            array = new JsonArray();
            MutableNode["searchParam"] = array;
        }

        array.Add(searchParam.MutableNode);
    }


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
