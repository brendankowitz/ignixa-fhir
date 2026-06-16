// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.FhirFakes.EdgeCases;

/// <summary>
/// A single mutable string-valued leaf in a resource tree, located either by property name within a
/// parent <see cref="JsonObject"/> or by index within a parent <see cref="JsonArray"/>.
/// </summary>
/// <remarks>
/// Exactly one of (<see cref="ParentObject"/>, <see cref="PropertyName"/>) or
/// (<see cref="ParentArray"/>, <see cref="ArrayIndex"/>) is populated. Use the factory methods
/// <see cref="ForProperty"/> / <see cref="ForArrayItem"/> to construct instances correctly.
/// </remarks>
public sealed class MutationTarget
{
    private MutationTarget(
        JsonObject? parentObject,
        string? propertyName,
        JsonArray? parentArray,
        int arrayIndex,
        string elementName,
        string path,
        string value)
    {
        ParentObject = parentObject;
        PropertyName = propertyName;
        ParentArray = parentArray;
        ArrayIndex = arrayIndex;
        ElementName = elementName;
        Path = path;
        Value = value;
    }

    /// <summary>The parent object when this leaf is an object property; otherwise null.</summary>
    public JsonObject? ParentObject { get; }

    /// <summary>The property name within <see cref="ParentObject"/>; otherwise null.</summary>
    public string? PropertyName { get; }

    /// <summary>The parent array when this leaf is an array element; otherwise null.</summary>
    public JsonArray? ParentArray { get; }

    /// <summary>The index within <see cref="ParentArray"/>; otherwise -1.</summary>
    public int ArrayIndex { get; }

    /// <summary>The leaf element name (the property key, e.g. "family"). For array items this is the owning property name.</summary>
    public string ElementName { get; }

    /// <summary>The computed JSON path to this leaf (e.g. "name[0].family", "birthDate").</summary>
    public string Path { get; }

    /// <summary>The current string value of this leaf.</summary>
    public string Value { get; }

    /// <summary>Creates a target for a string-valued property of an object.</summary>
    public static MutationTarget ForProperty(JsonObject parent, string propertyName, string path, string value)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(propertyName);
        return new MutationTarget(parent, propertyName, null, -1, propertyName, path, value);
    }

    /// <summary>Creates a target for a string-valued element of an array.</summary>
    public static MutationTarget ForArrayItem(JsonArray parent, int index, string elementName, string path, string value)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(elementName);
        return new MutationTarget(null, null, parent, index, elementName, path, value);
    }

    /// <summary>Replaces this leaf's value in its parent container in place.</summary>
    public void Replace(string newValue)
    {
        if (ParentObject is not null && PropertyName is not null)
        {
            ParentObject[PropertyName] = newValue;
            return;
        }

        if (ParentArray is not null)
        {
            ParentArray[ArrayIndex] = newValue;
            return;
        }

        throw new InvalidOperationException("MutationTarget has no resolvable parent container.");
    }
}
