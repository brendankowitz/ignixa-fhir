// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.Abstractions;

namespace Ignixa.Serialization.SourceNodes;

/// <summary>
/// Wraps an ISourceNode and adds type information from a structure definition provider.
/// Includes caching for performance optimization: structure definitions and typed children are cached
/// to eliminate O(n) property lookups and redundant schema queries.
/// </summary>
internal class TypedElementOnSourceNode : ITypedElement, IAnnotated
{
    private readonly ISourceNode _source;
    private readonly IStructureDefinitionSummaryProvider _provider;
    private readonly IElementDefinitionSummary? _definition;
    private readonly string? _parentPath; // Track parent path for BackboneElement lookups

    // OPTIMIZATION: Cache structure definition (immutable, safe to cache per-instance)
    private readonly Lazy<IStructureDefinitionSummary?> _structureDefinition;

    public TypedElementOnSourceNode(ISourceNode source, IStructureDefinitionSummaryProvider provider, IElementDefinitionSummary? definition = null, string? parentPath = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _definition = definition;
        _parentPath = parentPath;

        // Lazy initialization - only fetch structure definition if needed
        _structureDefinition = new Lazy<IStructureDefinitionSummary?>(() =>
        {
            var currentType = InstanceType;
            if (currentType == null) return null;

            // Try to get structure definition by type name first
            var structureDef = _provider.Provide(currentType);

            // If the type is BackboneElement and we have a parent path, try the fully qualified path
            if (structureDef != null && structureDef.TypeName == "BackboneElement" && !string.IsNullOrEmpty(_parentPath))
            {
                // Try fully qualified name like "AuditEvent.Agent"
                var fullyQualifiedName = $"{_parentPath}.{char.ToUpperInvariant(_source.Name[0])}{_source.Name.Substring(1)}";
                var specificDef = _provider.Provide(fullyQualifiedName);
                if (specificDef != null)
                {
                    return specificDef;
                }

                // Also try lowercase version like "AuditEvent.agent"
                fullyQualifiedName = $"{_parentPath}.{_source.Name}";
                specificDef = _provider.Provide(fullyQualifiedName);
                if (specificDef != null)
                {
                    return specificDef;
                }
            }

            return structureDef;
        });
    }

    public string Name => _source.Name;

    public string? InstanceType
    {
        get
        {
            // If we have a definition with a single type, use that
            if (_definition?.Type?.Length == 1)
            {
                // Use GetTypeName extension method to handle both IStructureDefinitionSummary and IStructureDefinitionReference
                return _definition.Type[0].GetTypeName();
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
        // Handle polymorphic properties (value[x] in FHIR spec)
        // According to FHIRPath N1 spec section 3.2, accessing "value" should match
        // "valueCode", "valueString", "valueQuantity", etc.
        IEnumerable<ISourceNode> sourceChildren;

        if (name != null && !name.EndsWith("[x]", StringComparison.Ordinal))
        {
            // Try exact match first
            sourceChildren = _source.Children(name);

            // If no exact match and we have a definition, check for polymorphic (choice) properties
            if (!sourceChildren.Any())
            {
                var cachedStructureDef = _structureDefinition.Value;
                if (cachedStructureDef != null)
                {
                    // Check if this is a choice element (IsChoiceElement == true)
                    // OR if there's an element with [x] suffix
                    var choiceElement = cachedStructureDef.GetElements()
                        .FirstOrDefault(e => (e.ElementName == name && e.IsChoiceElement) ||
                                              e.ElementName == name + "[x]");

                    // If this element is polymorphic, match any child starting with the name
                    if (choiceElement != null)
                    {
                        sourceChildren = _source.Children()
                            .Where(c => c.Name.StartsWith(name, StringComparison.Ordinal) && c.Name.Length > name.Length);
                    }
                }
            }
        }
        else
        {
            // No name filter or explicit [x] - return all children
            sourceChildren = _source.Children(name);
        }

        // Wrap source children in ITypedElement
        foreach (var child in sourceChildren)
        {
            // OPTIMIZATION: Cache structure definition lookup (immutable per instance)
            IElementDefinitionSummary? childDef = null;
            var cachedStructureDef = _structureDefinition.Value;
            if (cachedStructureDef != null)
            {
                childDef = cachedStructureDef.GetElements().FirstOrDefault(e => e.ElementName == child.Name);

                // If no exact match, check if this is a choice type variant (e.g., valueString for value[x])
                if (childDef == null)
                {
                    var choiceElement = cachedStructureDef.GetElements()
                        .FirstOrDefault(e => e.ElementName.EndsWith("[x]", StringComparison.Ordinal) &&
                                              child.Name.StartsWith(e.ElementName.TrimEnd("[x]".ToCharArray()), StringComparison.Ordinal));
                    if (choiceElement != null)
                    {
                        childDef = choiceElement;
                    }
                }
            }

            // Build parent path for BackboneElement children
            // For resource root, use resource type name (e.g., "AuditEvent")
            // For nested elements, append element name (e.g., "AuditEvent.agent")
            string? childParentPath = null;
            if (cachedStructureDef != null && cachedStructureDef.IsResource)
            {
                // Root resource element
                childParentPath = cachedStructureDef.TypeName;
            }
            else if (!string.IsNullOrEmpty(_parentPath))
            {
                // Nested element
                childParentPath = $"{_parentPath}.{_source.Name}";
            }
            else if (InstanceType != null && char.IsUpper(InstanceType[0]))
            {
                // Current element is likely a resource or type name
                childParentPath = InstanceType;
            }

            yield return new TypedElementOnSourceNode(child, _provider, childDef, childParentPath);
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
