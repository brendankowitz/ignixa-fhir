// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using Ignixa.SourceNodeSerialization.ElementModel;
using Ignixa.SourceNodeSerialization.Utility;

namespace Ignixa.SourceNodeSerialization.SourceNodes;

public abstract class BaseSourceNode<T> : ISourceNode, IResourceTypeSupplier, IAnnotated
    where T : IExtensionData
{
    private ReadOnlyDictionary<string, Lazy<IEnumerable<ISourceNode>>> _cachedNodes;

    protected BaseSourceNode(T resource)
    {
        Resource = resource;
    }

    public T Resource { get; }

    public abstract string ResourceType { get; }

    public abstract string Name { get; }

    public abstract string Text { get; }

    public abstract string Location { get; }

    public IEnumerable<object> Annotations(Type type)
    {
        if (type == GetType() || type == typeof(ISourceNode) || type == typeof(IResourceTypeSupplier))
        {
            return [this];
        }

        return Enumerable.Empty<object>();
    }

    public IEnumerable<ISourceNode> Children(string name = null)
    {
        if (_cachedNodes == null)
        {
            _cachedNodes = PropertySourceNodes()
                .Concat(ExtensionSourceNodes())
                .ToDictionary(x => x.Name, x => x.Node)
                .AsReadOnly();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return _cachedNodes.SelectMany(x => x.Value.Value);
        }

        if (name.EndsWith(JsonElementSourceNode.ChoiceTypeSuffix))
        {
            // e.g. value* which should return valueString etc.
            string matchPrefix = name.TrimEnd(JsonElementSourceNode.ChoiceTypeSuffix);
            return _cachedNodes
                .Where(x => x.Key.StartsWith(matchPrefix, StringComparison.Ordinal))
                .SelectMany(x => x.Value.Value)
                .ToArray();
        }

        // can we have duplicate values?
        //return _cachedNodes
        //    .Where(x => string.Equals(name, x.Name, StringComparison.Ordinal))
        //    .SelectMany(x => x.Node.Value);

        return _cachedNodes.TryGetValue(name, out Lazy<IEnumerable<ISourceNode>> cachedNodes) ? cachedNodes.Value : [];
    }

    private List<(string Name, Lazy<IEnumerable<ISourceNode>> Node)> ExtensionSourceNodes()
    {
        Dictionary<string, JsonElement> properties = Resource.ExtensionData ?? new Dictionary<string, JsonElement>();
        return JsonElementSourceNode.ProcessObjectProperties(properties.Select(x => (x.Key, x.Value)), Location);
    }

    protected abstract IEnumerable<(string Name, Lazy<IEnumerable<ISourceNode>> Node)> PropertySourceNodes();
}
