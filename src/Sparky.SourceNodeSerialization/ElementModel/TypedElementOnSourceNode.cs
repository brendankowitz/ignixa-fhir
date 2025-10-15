// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Sparky.SourceNodeSerialization.Specification;
using Sparky.SourceNodeSerialization.Utility;

namespace Sparky.SourceNodeSerialization.ElementModel;

/// <summary>
/// Wraps an ISourceNode and adds type information from a structure definition provider.
/// </summary>
internal class TypedElementOnSourceNode : ITypedElement, IAnnotated
{
    private readonly ISourceNode _source;
    private readonly IStructureDefinitionSummaryProvider _provider;
    private readonly IElementDefinitionSummary? _definition;

    public TypedElementOnSourceNode(ISourceNode source, IStructureDefinitionSummaryProvider provider, IElementDefinitionSummary? definition = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _definition = definition;
    }

    public string Name => _source.Name;

    public string? InstanceType
    {
        get
        {
            // If we have a definition with a single type, use that
            if (_definition?.Type?.Length == 1 && _definition.Type[0] is IStructureDefinitionSummary sds)
            {
                return sds.TypeName;
            }

            // For resources, check for resourceType element
            var resourceType = _source.Children("resourceType").FirstOrDefault()?.Text;
            if (!string.IsNullOrEmpty(resourceType))
            {
                return resourceType;
            }

            // Fallback to element name if it's uppercase (likely a resource or complex type)
            if (!string.IsNullOrEmpty(_source.Name) && char.IsUpper(_source.Name[0]))
            {
                return _source.Name;
            }

            return null;
        }
    }

    public object? Value => _source.Text;

    public string Location => _source.Location;

    public IElementDefinitionSummary? Definition => _definition;

    public IEnumerable<ITypedElement> Children(string? name = null)
    {
        foreach (var child in _source.Children(name))
        {
            // Try to find definition for this child
            // We can look up child definitions even when _definition is null,
            // as long as we have an InstanceType (e.g., root resources have no parent definition)
            IElementDefinitionSummary? childDef = null;
            var currentType = InstanceType;
            if (currentType != null)
            {
                var structureDef = _provider.Provide(currentType);
                childDef = structureDef?.GetElements().FirstOrDefault(e => e.ElementName == child.Name);
            }

            yield return new TypedElementOnSourceNode(child, _provider, childDef);
        }
    }

    public IEnumerable<object> Annotations(Type type)
    {
        if (_source is IAnnotated annotated)
        {
            return annotated.Annotations(type);
        }

        return Enumerable.Empty<object>();
    }
}

/// <summary>
/// Extension methods for converting ISourceNode to ITypedElement.
/// </summary>
public static class TypedElementExtensions
{
    /// <summary>
    /// Converts an ISourceNode to an ITypedElement using structure definition metadata.
    /// </summary>
    /// <param name="source">The source node to wrap.</param>
    /// <param name="provider">The structure definition provider for type information.</param>
    /// <returns>An ITypedElement with type information from the provider.</returns>
    public static ITypedElement ToTypedElement(this ISourceNode source, IStructureDefinitionSummaryProvider provider)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(provider);

        return new TypedElementOnSourceNode(source, provider);
    }

    /// <summary>
    /// Gets the scalar value of a child element by name.
    /// </summary>
    /// <param name="element">The typed element to query.</param>
    /// <param name="name">The name of the child element.</param>
    /// <returns>The value of the first matching child element, or null if not found.</returns>
    public static object? Scalar(this ITypedElement element, string name)
    {
        if (element == null) return null;

        return element.Children(name).FirstOrDefault()?.Value;
    }
}
