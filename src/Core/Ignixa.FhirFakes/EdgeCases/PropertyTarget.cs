// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.FhirFakes.EdgeCases;

/// <summary>
/// A string-valued leaf located by property name within a parent <see cref="JsonObject"/>.
/// </summary>
public sealed class PropertyTarget : MutationTarget
{
    private readonly JsonObject _parent;
    private readonly string _propertyName;

    /// <summary>Creates a target for a string-valued property of an object.</summary>
    /// <param name="parent">The parent object holding the property.</param>
    /// <param name="propertyName">The property name (also the element name).</param>
    /// <param name="path">The computed JSON path to this leaf.</param>
    /// <param name="value">The current string value of this leaf.</param>
    public PropertyTarget(JsonObject parent, string propertyName, string path, string value)
        : base(propertyName, path, value)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(propertyName);
        _parent = parent;
        _propertyName = propertyName;
    }

    /// <inheritdoc />
    public override void Replace(string newValue) => _parent[_propertyName] = newValue;
}
