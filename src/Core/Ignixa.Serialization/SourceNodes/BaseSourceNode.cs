// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.ObjectModel;
using Ignixa.Abstractions;

#pragma warning disable CS0618 // Type or member is obsolete

namespace Ignixa.Serialization.SourceNodes;

public abstract class BaseSourceNode<T> : ISourceNavigator
{
    private ReadOnlyDictionary<string, Lazy<IEnumerable<ISourceNavigator>>>? _cachedNodes;

    protected BaseSourceNode(T resource)
    {
        Resource = resource;
    }

    public T Resource { get; }

    public abstract string ResourceType { get; }

    public abstract string Name { get; }

    public abstract string Text { get; }

    public abstract string Location { get; }

    /// <summary>
    /// Resource-level nodes don't have primitive values.
    /// </summary>
    public virtual bool HasPrimitiveValue => false;

    public TMeta? Meta<TMeta>() where TMeta : class
    {
        if (this is TMeta typed)
        {
            return typed;
        }

        return null;
    }

    public IEnumerable<ISourceNavigator> Children(string name = null)
    {
        if (_cachedNodes == null)
        {
            // All properties are now handled in PropertySourceNodes() via MutableNode
            // No need for separate ExtensionSourceNodes()
            _cachedNodes = PropertySourceNodes()
                .ToDictionary(x => x.Name, x => x.Node)
                .AsReadOnly();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return _cachedNodes.SelectMany(x => x.Value.Value);
        }

        if (name.EndsWith(JsonNodeSourceNode.ChoiceTypeSuffix))
        {
            // e.g. value* which should return valueString etc.
            string matchPrefix = name.TrimEnd(JsonNodeSourceNode.ChoiceTypeSuffix);
            return _cachedNodes
                .Where(x => x.Key.StartsWith(matchPrefix, StringComparison.Ordinal))
                .SelectMany(x => x.Value.Value)
                .ToArray();
        }

        // can we have duplicate values?
        //return _cachedNodes
        //    .Where(x => string.Equals(name, x.Name, StringComparison.Ordinal))
        //    .SelectMany(x => x.Node.Value);

        return _cachedNodes.TryGetValue(name, out Lazy<IEnumerable<ISourceNavigator>> cachedNodes)
            ? cachedNodes.Value
            : [];
    }

    protected abstract IEnumerable<(string Name, Lazy<IEnumerable<ISourceNavigator>> Node)> PropertySourceNodes();
}
