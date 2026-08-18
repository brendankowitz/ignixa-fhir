// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.Validation.Tests.TestHelpers;

/// <summary>
/// An <see cref="IElement"/> decorator that counts how many times the tree beneath it is walked.
/// Lets a test observe whether a component touched the element's children at all, through the public
/// element seam rather than by instrumenting the component under test.
/// </summary>
public sealed class ChildAccessRecordingElement(IElement inner) : IElement
{
    private readonly IElement _inner = inner;
    private readonly List<string> _requestedChildNames = [];

    /// <summary>
    /// Gets the number of <see cref="Children(string?)"/> calls made on this element.
    /// </summary>
    public int ChildAccessCount { get; private set; }

    /// <summary>
    /// Gets the names passed to <see cref="Children(string?)"/>, in call order. A null name (all
    /// children) is recorded as the empty string.
    /// </summary>
    public IReadOnlyList<string> RequestedChildNames => _requestedChildNames;

    public string Name => _inner.Name;

    public object? Value => _inner.Value;

    public string InstanceType => _inner.InstanceType;

    public string Location => _inner.Location;

    public IType? Type => _inner.Type;

    public bool HasPrimitiveValue => _inner.HasPrimitiveValue;

    public IReadOnlyList<IElement> Children(string? name = null)
    {
        ChildAccessCount++;
        _requestedChildNames.Add(name ?? string.Empty);
        return _inner.Children(name);
    }

    public T? Meta<T>()
        where T : class => _inner.Meta<T>();
}
