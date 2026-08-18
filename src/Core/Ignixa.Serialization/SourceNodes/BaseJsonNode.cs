// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.Abstractions;

namespace Ignixa.Serialization.SourceNodes;

/// <summary>
/// Base class for all *JsonNode model classes that wrap a mutable JsonObject.
/// </summary>
public abstract class BaseJsonNode : IMutableJsonNode
{
    /// <summary>
    /// Internal storage: Single source of truth (no caching, direct read/write).
    /// </summary>
    private readonly JsonObject _internalNode;

    /// <summary>
    /// Gets or sets the FHIR version for this node.
    /// Set at root node construction and propagated to children.
    /// Used to determine version-specific serialization behavior.
    /// </summary>
    [JsonIgnore]
    public FhirVersion? FhirVersion { get; set; }

    /// <summary>
    /// Default constructor for deserialization.
    /// Creates a new empty JsonObject.
    /// </summary>
    protected BaseJsonNode()
    {
        _internalNode = new JsonObject();
    }

    /// <summary>
    /// Constructor for wrapping an existing JsonObject without FHIR version.
    /// Used by BaseJsonNodeConverter in Converters folder.
    /// </summary>
    /// <param name="jsonObject">Existing JsonObject to wrap.</param>
    protected BaseJsonNode(JsonObject jsonObject)
    {
        _internalNode = jsonObject ?? throw new ArgumentNullException(nameof(jsonObject));
    }

    /// <summary>
    /// Internal constructor for wrapping existing JsonObject with optional FHIR version.
    /// Used by BaseJsonNodeConverter in Models folder and when accessing nested properties.
    /// </summary>
    /// <param name="jsonObject">Existing JsonObject to wrap.</param>
    /// <param name="fhirVersion">Optional FHIR version (inherited from parent). Can be null.</param>
    protected BaseJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion)
    {
        _internalNode = jsonObject ?? throw new ArgumentNullException(nameof(jsonObject));
        FhirVersion = fhirVersion;
    }

    /// <summary>
    /// Gets the mutable JSON object backing this facade for Ignixa implementation assemblies.
    /// Public callers should use typed facade properties, serialization helpers, or ISourceNavigator.Meta&lt;JsonNode&gt;().
    /// </summary>
    internal JsonObject MutableNode => _internalNode;

    [SuppressMessage("Design", "CA1033", Justification = "Raw mutable JSON access is intentionally explicit and hidden from derived public API.")]
    JsonObject IMutableJsonNode.MutableNode => _internalNode;

    /// <summary>
    /// Sets a property value in the node.
    /// Convenience method for common mutations.
    /// </summary>
    /// <param name="name">The property name (e.g., "active", "name", "telecom").</param>
    /// <param name="value">The JsonNode value to set.</param>
    /// <remarks>
    /// <paramref name="value"/> is attached in place (zero-copy) only when it has no existing parent. A
    /// <see cref="JsonNode"/> can belong to just one parent, so a value already attached elsewhere (e.g.
    /// read off another facade: <c>a.Code = b.Code</c>) is deep-cloned before assignment rather than
    /// throwing. This means later mutations through the original facade (<c>b</c>) are NOT visible
    /// through the new one (<c>a</c>) in that case — plain aliasing (`a.Code = someUnparentedNode`) is
    /// unaffected and remains zero-copy.
    /// </remarks>
    public void SetProperty(string name, JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (value == null)
        {
            _internalNode.Remove(name);
        }
        else
        {
            _internalNode[name] = Reparentable(value);
        }
    }

    // A JsonNode can have only one parent; assigning one that is already attached elsewhere throws.
    // Clone in that case so reusing a model instance across parents copies rather than crashes.
    private static JsonNode Reparentable(JsonNode value) => value.Parent is null ? value : value.DeepClone();

    protected T? GetProperty<T>(string name)
    {
        if (MutableNode.TryGetPropertyValue(name, out var node) && node is JsonValue valueNode)
        {
            return valueNode.GetValue<T>();
        }
        return default;
    }

    protected void SetProperty<T>(string name, T? value)
    {
        if (value == null)
        {
            MutableNode.Remove(name);
        }
        else if (value is JsonNode jsonNodeValue)
        {
            MutableNode[name] = Reparentable(jsonNodeValue);
        }
        else
        {
            MutableNode[name] = JsonValue.Create(value);
        }
    }

    protected T? GetComplexProperty<T>(string name) where T : BaseJsonNode
    {
        if (MutableNode.TryGetPropertyValue(name, out var node) && node is JsonObject jsonObject)
        {
            // Assuming T has a constructor that takes a JsonObject and optional FhirVersion
            return (T)Activator.CreateInstance(typeof(T), jsonObject, FhirVersion);
        }
        return default;
    }

    protected MutableJsonList<T> GetListProperty<T>(string name) where T : BaseJsonNode
    {
        JsonArray? jsonArray = ReadArrayOrThrow(name);
        return new MutableJsonList<T>(() => GetOrCreateArray(name), jsonArray, FhirVersion);
    }

    protected MutablePrimitiveList<T> GetPrimitiveListProperty<T>(string name)
    {
        JsonArray? jsonArray = ReadArrayOrThrow(name);
        return new MutablePrimitiveList<T>(() => GetOrCreateArray(name), jsonArray);
    }

    /// <summary>
    /// Returns the array at <paramref name="name"/>, or null when the element is absent (or explicitly
    /// JSON null, which FHIR does not distinguish from absent).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The element is present but is not an array. Treating that as "absent" would let the first write
    /// overwrite author-supplied content, so it is surfaced rather than coerced.
    /// </exception>
    private JsonArray? ReadArrayOrThrow(string name)
    {
        if (!MutableNode.TryGetPropertyValue(name, out JsonNode? node) || node is null)
        {
            return null;
        }

        if (node is not JsonArray jsonArray)
        {
            throw new InvalidOperationException(
                $"Element '{name}' on {GetType().Name} is {node.GetValueKind()} but FHIR defines it as an "
                + "array. Fix the resource payload; Ignixa will not coerce or discard it.");
        }

        return jsonArray;
    }

    private JsonArray GetOrCreateArray(string name)
    {
        JsonArray? existing = ReadArrayOrThrow(name);
        if (existing is not null)
        {
            return existing;
        }

        var jsonArray = new JsonArray();
        MutableNode[name] = jsonArray;
        return jsonArray;
    }
}
