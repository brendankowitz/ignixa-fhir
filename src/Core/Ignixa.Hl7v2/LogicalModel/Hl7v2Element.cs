// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.Hl7v2.LogicalModel;

public sealed class Hl7v2Element : IElement
{
    private readonly IReadOnlyList<IElement> _children;

    public Hl7v2Element(
        string name,
        string instanceType,
        object? value = null,
        IEnumerable<IElement>? children = null,
        string? location = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceType);

        Name = name;
        InstanceType = instanceType;
        Value = value;
        Location = location ?? name;
        _children = children?.ToList() ?? [];
    }

    public string Name { get; }

    public object? Value { get; }

    public string InstanceType { get; }

    public string Location { get; }

    public IType? Type => null;

    public bool HasPrimitiveValue => Value is not null;

    public IReadOnlyList<IElement> Children(string? name = null)
    {
        return name is null
            ? _children
            : _children.Where(child => string.Equals(child.Name, name, StringComparison.Ordinal)).ToList();
    }

    public T? Meta<T>() where T : class => null;
}
