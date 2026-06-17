// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.FhirFakes.EdgeCases;

/// <summary>
/// A string-valued leaf located by index within a parent <see cref="JsonArray"/>.
/// </summary>
public sealed class ArrayItemTarget : MutationTarget
{
    private readonly JsonArray _parent;
    private readonly int _index;

    /// <summary>Creates a target for a string-valued element of an array.</summary>
    /// <param name="parent">The parent array holding the element.</param>
    /// <param name="index">The index of the element within the array.</param>
    /// <param name="elementName">The owning property name (the element name).</param>
    /// <param name="path">The computed JSON path to this leaf.</param>
    /// <param name="value">The current string value of this leaf.</param>
    public ArrayItemTarget(JsonArray parent, int index, string elementName, string path, string value)
        : base(elementName, path, value)
    {
        ArgumentNullException.ThrowIfNull(parent);
        _parent = parent;
        _index = index;
    }

    /// <inheritdoc />
    public override void Replace(string newValue) => _parent[_index] = newValue;
}
