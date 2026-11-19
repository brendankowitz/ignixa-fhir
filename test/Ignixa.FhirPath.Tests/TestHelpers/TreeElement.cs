/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * A minimal in-memory element for building trees a host could hand the evaluator.
 */

using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Tests.TestHelpers;

/// <summary>
/// An element with a fixed name, type, value and children, for trees no JSON parser would produce.
/// </summary>
internal sealed class TreeElement(string name, string type, object? value, IReadOnlyList<IElement> children) : IElement
{
    public string Name => name;

    public string InstanceType => type;

    public object? Value => value;

    public string Location => name;

    public IType? Type => null;

    public bool HasPrimitiveValue => value is not null;

    /// <summary>A chain of <paramref name="depth"/> nested elements ending in a string leaf.</summary>
    public static IElement Chain(int depth, string leaf)
    {
        IElement current = new TreeElement("leaf", "string", leaf, []);
        for (var i = 0; i < depth; i++)
        {
            current = new TreeElement("item", "BackboneElement", null, [current]);
        }

        return current;
    }

    public IReadOnlyList<IElement> Children(string? childName = null) =>
        childName is null ? children : children.Where(child => child.Name == childName).ToList();

    public T? Meta<T>() where T : class => null;
}
