// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Collections.Concurrent;
using Ignixa.Abstractions;

namespace Ignixa.Serialization.Tests.TestHelpers;

/// <summary>
/// An <see cref="ISourceNavigator"/> decorator that counts <see cref="ISourceNavigator.Text"/> reads
/// per node location. Lets a test observe whether <c>SchemaAwareElement.Value</c>'s memoisation of
/// <see cref="ISourceNavigator.Text"/> actually holds, through the public navigator seam rather than
/// by reaching into the element's private fields. The same counter is shared with every node produced
/// by <see cref="Children"/>, so wrapping the root is enough to observe reads anywhere in the subtree.
/// Counting is thread-safe so the same instance can back a concurrency test.
/// </summary>
public sealed class TextAccessRecordingSourceNavigator : ISourceNavigator
{
    private readonly ISourceNavigator _inner;
    private readonly ConcurrentDictionary<string, int> _reads;

    public TextAccessRecordingSourceNavigator(ISourceNavigator inner)
        : this(inner, new ConcurrentDictionary<string, int>(StringComparer.Ordinal))
    {
    }

    private TextAccessRecordingSourceNavigator(ISourceNavigator inner, ConcurrentDictionary<string, int> reads)
    {
        _inner = inner;
        _reads = reads;
    }

    /// <summary>
    /// Gets the number of times <see cref="Text"/> was read for the node at <paramref name="location"/>.
    /// </summary>
    public int TextReadCount(string location) => _reads.TryGetValue(location, out var count) ? count : 0;

    public string Name => _inner.Name;

    public string Text
    {
        get
        {
            _reads.AddOrUpdate(_inner.Location, 1, static (_, count) => count + 1);
            return _inner.Text;
        }
    }

    public string Location => _inner.Location;

    public string ResourceType => _inner.ResourceType;

    public IEnumerable<ISourceNavigator> Children(string? name = null) =>
        _inner.Children(name).Select(child => new TextAccessRecordingSourceNavigator(child, _reads));

    public T? Meta<T>() where T : class => _inner.Meta<T>();

    public bool HasPrimitiveValue => _inner.HasPrimitiveValue;
}
