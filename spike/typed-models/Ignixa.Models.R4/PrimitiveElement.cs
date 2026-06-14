// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.Models.R4;

/// <summary>
/// Firely-style wrapper over a FHIR primitive element. A FHIR primitive spans TWO sibling
/// keys on the parent object: the value key (<c>birthDate</c>) and an optional shadow
/// (<c>_birthDate</c>) carrying <c>id</c>/<c>extension</c>.
/// <para>
/// This wrapper deliberately does NOT derive from <c>BaseJsonNode</c>: that base wraps a single
/// <c>JsonObject</c>, but a primitive's identity is a (parent, propertyName) pair where the value
/// is a <c>JsonValue</c> living at <c>parent[name]</c> and the metadata lives at
/// <c>parent["_" + name]</c>. The backing is therefore the parent object plus the property name.
/// </para>
/// <para>
/// Shadow lifecycle: the <c>_name</c> object is created lazily the first time an extension or id is
/// written, and removed when it becomes empty (no id and no extensions). The wrapper owns this
/// invariant so callers never manage the shadow directly.
/// </para>
/// </summary>
/// <typeparam name="T">The CLR primitive type stored at the value key (e.g. <see cref="string"/>).</typeparam>
public sealed class PrimitiveElement<T>
{
    private readonly JsonObject _parent;
    private readonly string _name;

    public PrimitiveElement(JsonObject parent, string name)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentException.ThrowIfNullOrEmpty(name);
        _parent = parent;
        _name = name;
    }

    private string ShadowName => "_" + _name;

    /// <summary>
    /// The primitive value at <c>parent[name]</c>. Setting null removes only the value key;
    /// any shadow (extensions/id) is preserved.
    /// </summary>
    public T? Value
    {
        get => _parent[_name] is JsonValue v ? v.GetValue<T>() : default;
        set
        {
            if (value is null)
            {
                _parent.Remove(_name);
            }
            else
            {
                _parent[_name] = JsonValue.Create(value);
            }
        }
    }

    /// <summary>The <c>id</c> from the shadow object, or null.</summary>
    public string? Id
    {
        get => Shadow(create: false)?["id"]?.GetValue<string>();
        set
        {
            if (value is null)
            {
                var shadow = Shadow(create: false);
                shadow?.Remove("id");
                PruneShadowIfEmpty();
            }
            else
            {
                Shadow(create: true)!["id"] = value;
            }
        }
    }

    /// <summary>
    /// The shadow's <c>extension</c> array. Reading creates the shadow + array on demand (so callers
    /// can <c>Add</c>); call <see cref="PruneEmptyShadow"/> after clearing to drop an empty shadow.
    /// </summary>
    public JsonArray Extension
    {
        get
        {
            var shadow = Shadow(create: true)!;
            if (shadow["extension"] is not JsonArray array)
            {
                array = new JsonArray();
                shadow["extension"] = array;
            }

            return array;
        }
    }

    /// <summary>True when a shadow object with id or extensions is present.</summary>
    public bool HasExtensions
    {
        get
        {
            var shadow = Shadow(create: false);
            return shadow is not null
                   && ((shadow["extension"] is JsonArray a && a.Count > 0) || shadow["id"] is not null);
        }
    }

    /// <summary>Removes the shadow object if it carries no id and no (non-empty) extensions.</summary>
    public void PruneEmptyShadow() => PruneShadowIfEmpty();

    private JsonObject? Shadow(bool create)
    {
        if (_parent[ShadowName] is JsonObject existing)
        {
            return existing;
        }

        if (!create)
        {
            return null;
        }

        var shadow = new JsonObject();
        _parent[ShadowName] = shadow;
        return shadow;
    }

    private void PruneShadowIfEmpty()
    {
        if (_parent[ShadowName] is not JsonObject shadow)
        {
            return;
        }

        bool hasExtensions = shadow["extension"] is JsonArray a && a.Count > 0;
        bool hasId = shadow["id"] is not null;

        if (shadow["extension"] is JsonArray empty && empty.Count == 0)
        {
            shadow.Remove("extension");
        }

        if (!hasExtensions && !hasId)
        {
            _parent.Remove(ShadowName);
        }
    }
}
