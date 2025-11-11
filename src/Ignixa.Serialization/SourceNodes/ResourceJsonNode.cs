// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Abstractions;
using Ignixa.Serialization.Models;
using ISourceNode = Ignixa.Abstractions.ISourceNode;
using ITypedElement = Ignixa.Abstractions.ITypedElement;

// For ToTypedElement extension method

namespace Ignixa.Serialization.SourceNodes;

[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
public class ResourceJsonNode : BaseJsonNode, IResourceNode
{
    // Cached wrapper for Meta property (reuse same instance)
    private MetaJsonNode? _cachedMeta;
    private JsonNodeSourceNode? _cachedSourceNode;
    private ITypedElement? _cachedTypedElement;
    private IStructureDefinitionSummaryProvider? _cachedProvider;

    /// <summary>
    /// Default constructor for deserialization.
    /// </summary>
    public ResourceJsonNode()
    {
    }

    /// <summary>
    /// Protected internal constructor for JsonConverter and derived types (accepts pre-parsed JsonObject).
    /// Uses 'protected internal' to allow subclasses in other assemblies to use it.
    /// </summary>
    protected internal ResourceJsonNode(JsonObject jsonObject)
        : base(jsonObject)
    {
    }

    [JsonIgnore]
    public string ResourceType
    {
        get
        {
            var type = MutableNode["resourceType"]?.GetValue<string>() ?? string.Empty;
            
            if (type.Contains('/', StringComparison.Ordinal))
            {
                // get last part of the type
                return type.Substring(type.LastIndexOf('/') + 1);
            }
            
            return type;
        }
        set => MutableNode["resourceType"] = value;
    }

    [JsonIgnore]
    public string Id
    {
        get => MutableNode["id"]?.GetValue<string>() ?? string.Empty;
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                MutableNode.Remove("id");
            }
            else
            {
                MutableNode["id"] = value;
            }
        }
    }

    [JsonIgnore]
    public MetaJsonNode Meta
    {
        get
        {
            // Return cached wrapper if available
            if (_cachedMeta == null)
            {
                var internalNode = MutableNode;

                // Get or create the "meta" JsonObject
                if (!internalNode.TryGetPropertyValue("meta", out var metaNode) || metaNode is not JsonObject metaObject)
                {
                    metaObject = new JsonObject();
                    internalNode["meta"] = metaObject;
                }

                // Cache the wrapper (reuse same instance for subsequent accesses)
                _cachedMeta = new MetaJsonNode(metaObject);
            }

            return _cachedMeta;
        }
        set
        {
            if (value == null)
            {
                MutableNode.Remove("meta");
                _cachedMeta = null;
            }
            else
            {
                // Copy the internal JsonObject from the value
                MutableNode["meta"] = value.MutableNode;
                _cachedMeta = value; // Cache the new value
            }
        }
    }

    /// <summary>
    /// Wraps the JSON representation of the resource in an ISourceNode.
    /// </summary>
    public ISourceNode ToSourceNode()
    {
        _cachedSourceNode ??= JsonNodeSourceNode.FromRoot(MutableNode, ResourceType);
        return _cachedSourceNode;
    }

    /// <summary>
    /// Converts to ITypedElement using the provided schema provider.
    /// Caches the result for repeated calls with the same provider (reference equality).
    /// </summary>
    public ITypedElement ToTypedElement(IStructureDefinitionSummaryProvider provider)
    {
        // Cache hit: Same provider (reference equality check is fast and safe for singletons)
        if (_cachedTypedElement != null && ReferenceEquals(_cachedProvider, provider))
        {
            return _cachedTypedElement;
        }

        // Cache miss: Create and cache new typed element
        _cachedTypedElement = ToSourceNode().ToTypedElement(provider);
        _cachedProvider = provider;
        return _cachedTypedElement;
    }

    /// <summary>
    /// Invalidates cached views after in-place mutations.
    /// Called after PATCH operations to ensure subsequent accesses create fresh cached wrappers.
    /// Safe to call multiple times (idempotent).
    ///
    /// CACHE LIFECYCLE:
    /// - SourceNode and TypedElement caches are created lazily on first access
    /// - Mutations via MutableNode operations (e.g., PATCH) invalidate cached views
    /// - This method ensures next access to ToSourceNode() or ToTypedElement() creates fresh wrappers
    /// - Request-scoped: Each HTTP request gets fresh ResourceJsonNode with empty cache
    ///
    /// SAFE FOR PATCH OPERATIONS:
    /// - PATCH creates fresh ResourceJsonNode instances from repository (caches empty)
    /// - After mutations applied via ApplyPatchAsync(), this method is called
    /// - Subsequent validation/indexing creates fresh cached wrapper with updated state
    /// - No inter-request cache sharing - each request completely isolated
    /// </summary>
    public void InvalidateCaches()
    {
        _cachedSourceNode = null;
        _cachedTypedElement = null;
        _cachedProvider = null;
        // Note: _cachedMeta is NOT invalidated here - it has its own invalidation via Meta setter
    }

    /// <summary>
    /// Uses System.Text.Json to parse a JSON string into a ResourceJsonNode.
    /// </summary>
    public static ResourceJsonNode Parse(string json)
    {
        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(json);
    }

    /// <summary>
    /// Converts this ResourceJsonNode to a strongly-typed subclass (e.g., ParametersJsonNode).
    /// Uses reflection to invoke the internal constructor, providing zero-copy conversion.
    /// </summary>
    /// <typeparam name="T">Target type (must be a ResourceJsonNode subclass with internal JsonObject constructor).</typeparam>
    /// <param name="validate">If true (default), validates that the resource type matches the expected type for T.</param>
    /// <returns>A new instance of T wrapping the same underlying JsonObject.</returns>
    /// <exception cref="InvalidOperationException">If the internal constructor cannot be found.</exception>
    /// <exception cref="InvalidCastException">If validation is enabled and the resource type doesn't match the expected type.</exception>
    public T As<T>(bool validate = true) where T : ResourceJsonNode
    {
        // no-op if already the correct type
        if(this is T thisInstance)
        {
            return thisInstance;
        }

        Type targetType = typeof(T);

        // Downcast if needed
        if (targetType == typeof(ResourceJsonNode))
        {
            return (T)(object)this;
        }

        // Try up-cast to derived type
        string targetResourceType = targetType.Name.Replace("JsonNode", string.Empty, StringComparison.Ordinal);
        if (validate && targetResourceType != ResourceType)
        {
            throw new InvalidCastException($"Cannot convert resource of type '{ResourceType}' to {targetType.Name}, expected '{targetResourceType}'");
        }

        T? instance;
        if (ResourceTypeRegistry.TryCreateInstance(
                targetResourceType,
                MutableNode,
                out ResourceJsonNode? newInstance))
        {
            instance = (T)newInstance;
        }
        else
        {
            // Use reflection to invoke the internal constructor T(JsonObject)
            ConstructorInfo? constructor = targetType.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                [typeof(JsonObject)],
                null);

            if (constructor == null)
            {
                throw new InvalidOperationException(
                    $"Type '{targetType.Name}' does not have an internal constructor with signature (JsonObject)");
            }

            // Create the new instance by invoking the internal constructor with our MutableNode
            instance = (T)constructor.Invoke([MutableNode])
                       ?? throw new InvalidOperationException($"Failed to create instance of {targetType.Name}");
        }

        // Copy FhirVersion to maintain metadata
        instance.FhirVersion = FhirVersion;

        return instance;
    }
}
