// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Abstractions;
using Ignixa.Models;

// For ToElement extension method

#pragma warning disable CS0618 // Type or member is obsolete

namespace Ignixa.Serialization.SourceNodes;

[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
public class ResourceJsonNode : BaseJsonNode, IResourceNode
{
    // Cached wrapper for Meta property (reuse same instance)
    private Meta? _cachedMeta;
    private JsonNodeSourceNode? _cachedSourceNode;
    private IElement? _cachedElement;
    private ISchema? _cachedProvider;

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

    /// <summary>
    /// Public constructor for JsonConverter and derived types (accepts pre-parsed JsonObject and optional FHIR version).
    /// Must be public so generic constructor lookup (Activator.CreateInstance / Type.GetConstructor) used by
    /// GetComplexProperty&lt;T&gt; and MutableJsonList&lt;T&gt; can find it when T = ResourceJsonNode.
    /// </summary>
    public ResourceJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
        : base(jsonObject, fhirVersion)
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
    public Meta Meta
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
                _cachedMeta = new Meta(metaObject);
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
    /// Wraps the JSON representation of the resource in an ISourceNavigator.
    /// </summary>
    public ISourceNavigator ToSourceNavigator()
    {
        _cachedSourceNode ??= JsonNodeSourceNode.FromRoot(MutableNode, ResourceType);
        return _cachedSourceNode;
    }

    /// <summary>
    /// Converts to IElement using the provided schema provider.
    /// Caches the result for repeated calls with the same provider (reference equality).
    /// </summary>
    public IElement ToElement(ISchema schema)
    {
        // Cache hit: Same schema provider (reference equality check is fast and safe for singletons)
        if (_cachedElement != null && ReferenceEquals(_cachedProvider, schema))
        {
            return _cachedElement;
        }

        // Cache miss: Create and cache new element
        _cachedElement = ToSourceNavigator().ToElement(schema);
        _cachedProvider = schema;
        return _cachedElement;
    }

    /// <summary>
    /// Invalidates cached views after in-place mutations.
    /// Called after PATCH operations to ensure subsequent accesses create fresh cached wrappers.
    /// Safe to call multiple times (idempotent).
    ///
    /// CACHE LIFECYCLE:
    /// - SourceNode and IElement caches are created lazily on first access
    /// - Mutations via MutableNode operations (e.g., PATCH) invalidate cached views
    /// - This method ensures next access to ToSourceNavigator() or ToElement() creates fresh wrappers
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
        _cachedElement = null;
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

    private static readonly ConcurrentDictionary<Type, FhirVersion[]?> CompatibleVersionsCache = new();

    /// <summary>
    /// Converts this ResourceJsonNode to a strongly-typed subclass (e.g., ParametersJsonNode).
    /// Uses reflection to invoke the internal constructor, providing zero-copy conversion.
    /// </summary>
    /// <typeparam name="T">Target type (must be a ResourceJsonNode subclass with internal JsonObject constructor).</typeparam>
    /// <param name="validate">
    /// If true (default), validates that the resource type matches the expected type for T, and --
    /// when both this node's <see cref="BaseJsonNode.FhirVersion"/> and T's
    /// <see cref="CompatibleFhirVersionsAttribute"/> are present -- that the node's version is one T
    /// actually supports. Pass false to bypass both checks as an expert escape hatch (e.g. reinterpreting
    /// a node whose version is deliberately unknown or being intentionally overridden).
    /// </param>
    /// <returns>A new instance of T wrapping the same underlying JsonObject.</returns>
    /// <exception cref="InvalidOperationException">If the internal constructor cannot be found.</exception>
    /// <exception cref="InvalidCastException">
    /// If validation is enabled and the resource type doesn't match the expected type, or this node's
    /// FhirVersion is set to a version T's <see cref="CompatibleFhirVersionsAttribute"/> doesn't list.
    /// </exception>
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

        FhirVersion[]? compatibleVersions = GetCompatibleVersions(targetType);

        // A version-tagged node reinterpreted through a version-marked facade that doesn't list it is a
        // silent misread waiting to happen (e.g. STU3 JSON read through an R4/R5-shaped accessor) --
        // structurally identical property names can mean different things across versions. Unspecified
        // is a deliberate "assume latest" choice (see FhirVersion docs), not "unknown", but it isn't a
        // hard version constraint either, so it's exempt from this check the same as an untagged (null)
        // node. Unmarked target types (hand-written, version-agnostic facades) are never checked.
        if (validate
            && FhirVersion is { } sourceVersion
            && sourceVersion != global::Ignixa.Abstractions.FhirVersion.Unspecified
            && compatibleVersions is not null
            && Array.IndexOf(compatibleVersions, sourceVersion) < 0)
        {
            throw new InvalidCastException(
                $"Cannot convert a {sourceVersion} resource to {targetType.Name}, which only supports "
                + $"[{string.Join(", ", compatibleVersions)}]. Use TryAsVersion/AsVersion for safe "
                + "dispatch, or As<T>(validate: false) to bypass this check.");
        }

        T? instance = null;
        if (ResourceTypeRegistry.TryCreateInstance(
                targetResourceType,
                MutableNode,
                out ResourceJsonNode? newInstance)
            && newInstance is T typedInstance)
        {
            instance = typedInstance;
        }

        instance ??= CreateViaReflectionConstructor<T>(targetType, MutableNode);

        // Copy FhirVersion to maintain metadata; when this node is untagged and T is unambiguously
        // single-version, stamp that version instead of leaving the result untagged too.
        instance.FhirVersion = FhirVersion ?? (compatibleVersions is { Length: 1 } single ? single[0] : null);

        return instance;
    }

    private static FhirVersion[]? GetCompatibleVersions(Type type) =>
        CompatibleVersionsCache.GetOrAdd(type, static t =>
            ((CompatibleFhirVersionsAttribute?)Attribute.GetCustomAttribute(t, typeof(CompatibleFhirVersionsAttribute), inherit: false))
                ?.Versions.ToArray());

    /// <summary>
    /// Builds a <typeparamref name="T"/> by invoking its internal <c>T(JsonObject)</c> constructor via
    /// reflection. Used when <see cref="ResourceTypeRegistry"/> has no factory for the target type, or its
    /// factory produces a different runtime type than <typeparamref name="T"/> (e.g. a registered
    /// hand-written facade when the caller asked for a generated typed-model subclass).
    /// </summary>
    private static T CreateViaReflectionConstructor<T>(Type targetType, JsonObject mutableNode) where T : ResourceJsonNode
    {
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

        return (T)constructor.Invoke([mutableNode])
               ?? throw new InvalidOperationException($"Failed to create instance of {targetType.Name}");
    }
}
