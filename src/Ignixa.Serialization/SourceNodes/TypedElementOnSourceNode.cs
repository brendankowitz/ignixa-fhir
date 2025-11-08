// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Ignixa.Abstractions;

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
    private readonly string? _instanceType;

    // OPTIMIZATION: Cache structure definition (immutable, safe to cache per-instance)
    private readonly Lazy<IStructureDefinitionSummary?> _structureDefinition;

    // OPTIMIZATION: Cache for child element definitions (avoid repeated lookups)
    // Key: element name, Value: IElementDefinitionSummary (can be null)
    // Using ConcurrentDictionary for thread-safe concurrent access
    private readonly Lazy<ConcurrentDictionary<string, IElementDefinitionSummary?>> _childDefinitionCache =
        new(() => new ConcurrentDictionary<string, IElementDefinitionSummary?>());

    /// <summary>
    /// Public constructor for root elements (resources)
    /// </summary>
    public TypedElementOnSourceNode(ISourceNode source, IStructureDefinitionSummaryProvider provider, IElementDefinitionSummary? definition = null, string? instanceType = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _definition = definition;
        _instanceType = instanceType ?? DeriveInstanceType(source, definition);

        // Lazy initialization - fetch structure definition when needed
        _structureDefinition = new Lazy<IStructureDefinitionSummary?>(() =>
        {
            // Use the derived instance type to get structure definition
            if (!string.IsNullOrEmpty(_instanceType))
            {
                return _provider.Provide(_instanceType);
            }

            return null;
        });
    }

    /// <summary>
    /// Derives the instance type for an element based on its source node and definition.
    /// KEY INSIGHT: For BackboneElements, the ElementName IS the qualified type name (e.g., "QuestionnaireResponse.item").
    /// </summary>
    private static string? DeriveInstanceType(ISourceNode source, IElementDefinitionSummary? definition)
    {
        // For resources, check for resourceType element first
        var resourceTypeIndicator = source.Children("resourceType").FirstOrDefault()?.Text;

        if (definition != null && definition.IsResource)
        {
            return resourceTypeIndicator;
        }

        // For choice elements (value[x]), extract type from property name suffix
        if (definition != null && (definition.IsChoiceElement || definition.ElementName?.EndsWith("[x]", StringComparison.Ordinal) == true))
        {
            var elementBaseName = definition.ElementName?.TrimEnd("[x]".ToCharArray());
            if (!string.IsNullOrEmpty(elementBaseName) && source.Name.StartsWith(elementBaseName, StringComparison.Ordinal))
            {
                var suffix = source.Name.Substring(elementBaseName.Length);
                if (!string.IsNullOrEmpty(suffix))
                {
                    var normalized = NormalizeFhirPathTypeName(suffix);
                    return normalized;
                }
            }
        }

        // For elements with a single type, check if it's a BackboneElement
        if (definition?.Type?.Length == 1)
        {
            var typeName = definition.Type[0].GetTypeName();

            // BackboneElements have qualified ElementName like "QuestionnaireResponse.item"
            // This IS the InstanceType we want (not generic "BackboneElement")
            if (typeName == "BackboneElement" || typeName == "Element")
            {
                // Use the ElementName as the InstanceType (it's qualified for BackboneElements)
                if (!string.IsNullOrEmpty(definition.ElementName))
                {
                    return definition.ElementName;
                }
            }

            return typeName;
        }

        // Fallback for resources without definition
        if (!string.IsNullOrEmpty(resourceTypeIndicator))
        {
            return resourceTypeIndicator;
        }

        // Fallback to element name if uppercase (likely a resource or complex type)
        if (!string.IsNullOrEmpty(source.Name) && char.IsUpper(source.Name[0]))
        {
            return source.Name;
        }

        return null;
    }

    public string Name => _source.Name;

    public string? InstanceType => _instanceType; // Just return the stored field

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
            string? childInstanceType = null;

            if (cachedStructureDef != null)
            {
                // For BackboneElements, check if a qualified structure exists (e.g., "QuestionnaireResponse.item")
                var qualifiedName = $"{cachedStructureDef.TypeName}.{child.Name}";
                var qualifiedStructDef = _provider.Provide(qualifiedName);

                if (qualifiedStructDef != null)
                {
                    // This child is a BackboneElement with its own structure definition
                    // Use the qualified typename directly as the instance type
                    childInstanceType = qualifiedStructDef.TypeName;
                }
                else
                {
                    // Check for recursive BackboneElements (e.g., QuestionnaireResponse.item.item)
                    // The parent InstanceType might already be the qualified name we need
                    // Extract last segment of parent's TypeName and compare with child name
                    var parentTypeName = cachedStructureDef.TypeName;
                    var lastSegment = parentTypeName.Contains('.', StringComparison.Ordinal)
                        ? parentTypeName.Substring(parentTypeName.LastIndexOf('.') + 1)
                        : parentTypeName;

                    // Case-insensitive comparison for recursion check
                    if (child.Name.Equals(lastSegment, StringComparison.OrdinalIgnoreCase))
                    {
                        // This is a recursive element - use the parent's qualified type
                        childInstanceType = parentTypeName;
                    }
                }

                // Use child definition cache to avoid repeated lookups
                childDef = GetCachedChildDefinition(child.Name, cachedStructureDef);
            }

            // If we didn't already determine the instance type (not a BackboneElement),
            // derive it using the standard method
            if (childInstanceType == null)
            {
                childInstanceType = DeriveInstanceType(child, childDef);
            }

            // Create child node with explicit instance type
            yield return new TypedElementOnSourceNode(child, _provider, childDef, childInstanceType);
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
        // For BackboneElements, try to get the qualified structure definition directly
        // (e.g., provider.Provide("QuestionnaireResponse.item"))
        var qualifiedName = $"{cachedStructureDef.TypeName}.{childName}";
        var qualifiedStructDef = _provider.Provide(qualifiedName);

        // If we found a qualified structure definition for this child (it's a BackboneElement),
        // get its root element as the definition
        IElementDefinitionSummary? childDef = null;
        if (qualifiedStructDef != null)
        {
            // Use the root element of the qualified structure
            childDef = qualifiedStructDef.GetElements().FirstOrDefault();
        }

        // If no qualified structure def, try exact match from parent's elements (for primitives/simple types)
        if (childDef == null)
        {
            childDef = cachedStructureDef.GetElements().FirstOrDefault(e => e.ElementName == childName);
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

    /// <summary>
    /// Normalizes FHIR type names extracted from choice elements to match FHIRPath conventions.
    /// Primitive type names are lowercase (e.g., "String" → "string", "Integer" → "integer"),
    /// while complex types remain capitalized (e.g., "Quantity", "CodeableConcept").
    /// </summary>
    /// <param name="typeName">The type name extracted from the choice element suffix (e.g., "String", "Quantity").</param>
    /// <returns>The normalized type name per FHIRPath conventions.</returns>
    private static string NormalizeFhirPathTypeName(string typeName)
    {
        // FHIR primitive types that should be lowercase in FHIRPath
        // Reference: http://hl7.org/fhir/datatypes.html and FHIRPath specification
        var primitiveLowercase = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "string", "integer", "boolean", "decimal",
            "date", "dateTime", "time", "code",
            "uri", "url", "canonical", "uuid", "oid", "id",
            "markdown", "base64Binary", "instant",
            "unsignedInt", "positiveInt", "integer64"
        };

        // If it's a primitive type, return lowercase version
        // FHIRPath type names for primitives are lowercase per specification, ToLowerInvariant is intentional
#pragma warning disable CA1308 // Normalize strings to uppercase
        return primitiveLowercase.Contains(typeName)
            ? typeName.ToLowerInvariant()
            : typeName; // Keep complex types as-is (Quantity, CodeableConcept, etc.)
#pragma warning restore CA1308 // Normalize strings to uppercase
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
