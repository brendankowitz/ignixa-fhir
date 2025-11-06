// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
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

    // OPTIMIZATION: Cache for child element definitions (avoid repeated lookups)
    // Key: element name, Value: IElementDefinitionSummary (can be null)
    // Using ConcurrentDictionary for thread-safe concurrent access
    private readonly Lazy<ConcurrentDictionary<string, IElementDefinitionSummary?>> _childDefinitionCache =
        new(() => new ConcurrentDictionary<string, IElementDefinitionSummary?>());

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

            // Handle choice types (e.g., value[x] → valueQuantity means type is "Quantity")
            // Check if this is a choice element with multiple types
            if (_definition != null && (_definition.IsChoiceElement || _definition.ElementName?.EndsWith("[x]", StringComparison.Ordinal) == true))
            {
                // Extract type from property name suffix
                // For "valueQuantity" with definition "value[x]", extract "Quantity"
                var elementBaseName = _definition.ElementName?.TrimEnd("[x]".ToCharArray());
                if (!string.IsNullOrEmpty(elementBaseName) && _source.Name.StartsWith(elementBaseName, StringComparison.Ordinal))
                {
                    var typeSuffix = _source.Name.Substring(elementBaseName.Length);
                    if (!string.IsNullOrEmpty(typeSuffix))
                    {
                        // Return the extracted type (e.g., "Quantity", "String", "CodeableConcept")
                        return typeSuffix;
                    }
                }
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

    public object? Value
    {
        get
        {
            var text = _source.Text;
            if (text == null) return null;

            // Convert primitive FHIR types to their native C# types for proper FHIRPath evaluation
            return InstanceType switch
            {
                "boolean" => bool.TryParse(text, out var b) ? b : text,
                "integer" or "unsignedInt" or "positiveInt" => int.TryParse(text, out var i) ? i : text,
                "decimal" => decimal.TryParse(text, out var d) ? d : text,
                // All other types remain as strings (string, date, dateTime, code, id, uri, etc.)
                _ => text
            };
        }
    }

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
            // OPTIMIZATION: Use cached structure definition lookup (immutable per instance)
            var cachedStructureDef = _structureDefinition.Value;
            IElementDefinitionSummary? childDef = null;

            if (cachedStructureDef != null)
            {
                // Use child definition cache to avoid repeated lookups
                childDef = GetCachedChildDefinition(child.Name, cachedStructureDef);
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

    /// <summary>
    /// Gets or creates a cache of child element definitions.
    /// Avoids repeated lookups of the same child name across multiple navigations.
    /// Returns null if no definition found (valid - not all elements have definitions).
    /// Thread-safe: uses ConcurrentDictionary for atomic get-or-add semantics.
    /// </summary>
    private IElementDefinitionSummary? GetCachedChildDefinition(string childName, IStructureDefinitionSummary? cachedStructureDef)
    {
        // No structure definition? Can't cache anything
        if (cachedStructureDef == null)
            return null;

        var cache = _childDefinitionCache.Value;

        // Return from cache if found (even if value is null, which is valid)
        if (cache.TryGetValue(childName, out var cachedDef))
            return cachedDef;

        // Cache miss: Look up definition
        // First try exact match (for unqualified names like "value")
        var childDef = cachedStructureDef.GetElements().FirstOrDefault(e => e.ElementName == childName);

        // If no exact match, try qualified name (e.g., "Quantity.value")
        if (childDef == null)
        {
            var qualifiedName = $"{cachedStructureDef.TypeName}.{childName}";
            childDef = cachedStructureDef.GetElements().FirstOrDefault(e => e.ElementName == qualifiedName);
        }

        // If still no match, check if this is a choice type variant (e.g., valueString for value[x])
        if (childDef == null)
        {
            var choiceElement = cachedStructureDef.GetElements()
                .FirstOrDefault(e =>
                {
                    // Check if it's a choice element by flag OR by [x] suffix
                    if (!e.IsChoiceElement && !e.ElementName.EndsWith("[x]", StringComparison.Ordinal))
                        return false;

                    // Extract base name: "value[x]" → "value" or just use "value" if IsChoiceElement
                    var baseName = e.ElementName.EndsWith("[x]", StringComparison.Ordinal)
                        ? e.ElementName.TrimEnd("[x]".ToCharArray())
                        : e.ElementName;

                    // Check if child name starts with base name (e.g., "valueQuantity" starts with "value")
                    return childName.StartsWith(baseName, StringComparison.Ordinal) && childName.Length > baseName.Length;
                });
            if (choiceElement != null)
            {
                childDef = choiceElement;
            }
        }

        // If still no match, try qualified choice type (e.g., "Observation.value[x]" for "valueQuantity")
        if (childDef == null)
        {
            var typeName = cachedStructureDef.TypeName;
            var qualifiedChoiceElement = cachedStructureDef.GetElements()
                .FirstOrDefault(e =>
                {
                    // Extract base name from qualified choice element (e.g., "Observation.value[x]" → "value")
                    var elementName = e.ElementName;
                    if (elementName.EndsWith("[x]", StringComparison.Ordinal) && elementName.Contains('.', StringComparison.Ordinal))
                    {
                        var parts = elementName.Split('.');
                        if (parts.Length == 2)
                        {
                            var baseName = parts[1].TrimEnd("[x]".ToCharArray());
                            return childName.StartsWith(baseName, StringComparison.Ordinal);
                        }
                    }
                    return false;
                });
            if (qualifiedChoiceElement != null)
            {
                childDef = qualifiedChoiceElement;
            }
        }

        // Cache the result (including null) - ConcurrentDictionary makes this thread-safe
        cache.TryAdd(childName, childDef);
        return childDef;
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
