// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization.Models;

/// <summary>
/// Represents a FHIR Provenance resource.
/// Provenance is a record that describes entities and processes involved in producing
/// and delivering or otherwise influencing a resource.
/// </summary>
[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
[SuppressMessage("Design", "CA1819", Justification = "POCO style model")]
public class ProvenanceJsonNode : ResourceJsonNode
{
    public ProvenanceJsonNode()
    {
        ResourceType = "Provenance";
    }

    /// <summary>
    /// Internal constructor for JsonConverter (accepts pre-parsed JsonObject).
    /// </summary>
    internal ProvenanceJsonNode(JsonObject jsonObject)
        : base(jsonObject)
    {
    }

    /// <summary>
    /// Internal constructor for JsonConverter (accepts pre-parsed JsonObject with optional FHIR version).
    /// </summary>
    internal ProvenanceJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
    {
    }

    /// <summary>
    /// The resource(s) that this Provenance record relates to.
    /// For X-Provenance header, this is auto-filled by the server.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<ReferenceComponent> Target
    {
        get
        {
            if (!MutableNode.TryGetPropertyValue("target", out var targetNode) || targetNode is not JsonArray array)
            {
                return Array.Empty<ReferenceComponent>();
            }

            var list = new List<ReferenceComponent>();
            foreach (var item in array)
            {
                if (item != null)
                {
                    var json = item.ToJsonString();
                    list.Add(JsonSerializer.Deserialize<ReferenceComponent>(json) ?? new ReferenceComponent());
                }
            }

            return list;
        }
    }

    /// <summary>
    /// Checks if the Provenance has a target reference specified.
    /// </summary>
    [JsonIgnore]
    public bool HasTarget => MutableNode.ContainsKey("target");

    /// <summary>
    /// Helper method to add a target reference to the underlying JSON array.
    /// </summary>
    public void AddTarget(ReferenceComponent target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!MutableNode.TryGetPropertyValue("target", out var node) || node is not JsonArray array)
        {
            array = new JsonArray();
            MutableNode["target"] = array;
        }

        array.Add(target.MutableNode);
    }

    /// <summary>
    /// Helper method to add a target reference by resource type, id, and version.
    /// Commonly used when server auto-fills target after resource creation.
    /// </summary>
    public void AddTarget(string resourceType, string resourceId, string versionId)
    {
        if (string.IsNullOrWhiteSpace(resourceType))
        {
            throw new ArgumentException("Resource type cannot be null or empty", nameof(resourceType));
        }

        if (string.IsNullOrWhiteSpace(resourceId))
        {
            throw new ArgumentException("Resource ID cannot be null or empty", nameof(resourceId));
        }

        if (string.IsNullOrWhiteSpace(versionId))
        {
            throw new ArgumentException("Version ID cannot be null or empty", nameof(versionId));
        }

        var target = new ReferenceComponent
        {
            Reference = $"{resourceType}/{resourceId}/_history/{versionId}"
        };

        AddTarget(target);
    }

    /// <summary>
    /// Helper method to replace all target references.
    /// </summary>
    public void SetTargets(IEnumerable<ReferenceComponent> targets)
    {
        if (targets == null)
        {
            MutableNode.Remove("target");
        }
        else
        {
            var array = new JsonArray();
            foreach (var item in targets)
            {
                array.Add(item.MutableNode);
            }

            MutableNode["target"] = array;
        }
    }

    /// <summary>
    /// The instant of time at which the activity was recorded.
    /// Required for FHIR Provenance resource.
    /// </summary>
    [JsonIgnore]
    public DateTimeOffset? Recorded
    {
        get
        {
            if (MutableNode.TryGetPropertyValue("recorded", out var node) && node != null)
            {
                var value = node.GetValue<string>();
                if (DateTimeOffset.TryParse(value, out var result))
                {
                    return result;
                }
            }

            return null;
        }
        set
        {
            if (value == null)
            {
                MutableNode.Remove("recorded");
            }
            else
            {
                // Store as ISO 8601 string (FHIR instant format)
                MutableNode["recorded"] = value.Value.ToString("o");
            }
        }
    }

    /// <summary>
    /// The agent(s) involved in the activity.
    /// Required for FHIR Provenance resource (at least one agent).
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<AgentComponent> Agent
    {
        get
        {
            if (!MutableNode.TryGetPropertyValue("agent", out var agentNode) || agentNode is not JsonArray array)
            {
                return Array.Empty<AgentComponent>();
            }

            var list = new List<AgentComponent>();
            foreach (var item in array)
            {
                if (item != null)
                {
                    var json = item.ToJsonString();
                    list.Add(JsonSerializer.Deserialize<AgentComponent>(json) ?? new AgentComponent());
                }
            }

            return list;
        }
    }

    /// <summary>
    /// Helper method to add an agent to the underlying JSON array.
    /// </summary>
    public void AddAgent(AgentComponent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        if (!MutableNode.TryGetPropertyValue("agent", out var node) || node is not JsonArray array)
        {
            array = new JsonArray();
            MutableNode["agent"] = array;
        }

        array.Add(agent.MutableNode);
    }

    /// <summary>
    /// Helper method to replace all agents.
    /// </summary>
    public void SetAgents(IEnumerable<AgentComponent> agents)
    {
        if (agents == null)
        {
            MutableNode.Remove("agent");
        }
        else
        {
            var array = new JsonArray();
            foreach (var item in agents)
            {
                array.Add(item.MutableNode);
            }

            MutableNode["agent"] = array;
        }
    }

    /// <summary>
    /// Represents a FHIR Reference component.
    /// </summary>
    [SuppressMessage("Design", "CA1034", Justification = "Nested type matches FHIR structure")]
    public class ReferenceComponent : BaseJsonNode
    {
        /// <summary>
        /// Default constructor for deserialization.
        /// </summary>
        public ReferenceComponent()
        {
        }

        /// <summary>
        /// Internal constructor for JsonConverter (accepts pre-parsed JsonObject with optional FHIR version).
        /// </summary>
        internal ReferenceComponent(JsonObject jsonObject, FhirVersion? fhirVersion = null)
            : base(jsonObject, fhirVersion)
        {
        }

        [JsonIgnore]
        public string? Reference
        {
            get => MutableNode["reference"]?.GetValue<string>();
            set
            {
                if (value == null)
                {
                    MutableNode.Remove("reference");
                }
                else
                {
                    MutableNode["reference"] = value;
                }
            }
        }

        [JsonIgnore]
        public string? Display
        {
            get => MutableNode["display"]?.GetValue<string>();
            set
            {
                if (value == null)
                {
                    MutableNode.Remove("display");
                }
                else
                {
                    MutableNode["display"] = value;
                }
            }
        }
    }

    /// <summary>
    /// Represents an agent involved in the provenance activity.
    /// </summary>
    [SuppressMessage("Design", "CA1034", Justification = "Nested type matches FHIR structure")]
    public class AgentComponent : BaseJsonNode
    {
        /// <summary>
        /// Default constructor for deserialization.
        /// </summary>
        public AgentComponent()
        {
        }

        /// <summary>
        /// Internal constructor for JsonConverter (accepts pre-parsed JsonObject with optional FHIR version).
        /// </summary>
        internal AgentComponent(JsonObject jsonObject, FhirVersion? fhirVersion = null)
            : base(jsonObject, fhirVersion)
        {
        }

        [JsonIgnore]
        public ReferenceComponent? Who
        {
            get
            {
                if (MutableNode.TryGetPropertyValue("who", out var whoNode) && whoNode is JsonObject)
                {
                    var json = whoNode.ToJsonString();
                    return JsonSerializer.Deserialize<ReferenceComponent>(json);
                }

                return null;
            }
            set
            {
                if (value == null)
                {
                    MutableNode.Remove("who");
                }
                else
                {
                    MutableNode["who"] = value.MutableNode;
                }
            }
        }

        [JsonIgnore]
        public ReferenceComponent? OnBehalfOf
        {
            get
            {
                if (MutableNode.TryGetPropertyValue("onBehalfOf", out var onBehalfOfNode) && onBehalfOfNode is JsonObject)
                {
                    var json = onBehalfOfNode.ToJsonString();
                    return JsonSerializer.Deserialize<ReferenceComponent>(json);
                }

                return null;
            }
            set
            {
                if (value == null)
                {
                    MutableNode.Remove("onBehalfOf");
                }
                else
                {
                    MutableNode["onBehalfOf"] = value.MutableNode;
                }
            }
        }
    }
}
