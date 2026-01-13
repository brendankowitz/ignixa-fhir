// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Collections.Frozen;
using Ignixa.Abstractions;

#pragma warning disable CS0618 // Type or member is obsolete

namespace Ignixa.Serialization.SourceNodes;

/// <summary>
/// Wraps an ISourceNode and adds schema-based type information from a schema provider.
/// Includes caching for performance optimization: type definitions and typed children are cached
/// to eliminate O(n) property lookups and redundant schema queries.
/// Implements IElement interface for modern FHIR navigation.
/// </summary>
internal class SchemaAwareElement : IElement
{
    private readonly ISourceNavigator _source;
    private readonly ISchema _schema;
    private readonly IType? _definition;
    private readonly string? _instanceType;

    // OPTIMIZATION: Cache type definition (immutable, safe to cache per-instance)
    private readonly Lazy<IType?> _typeDefinition;

    // OPTIMIZATION: Cache for child element definitions (avoid repeated lookups)
    // Key: element name, Value: IType (can be null)
    // Using ConcurrentDictionary for thread-safe concurrent access
    private readonly Lazy<ConcurrentDictionary<string, IType?>> _childDefinitionCache =
        new(() => new ConcurrentDictionary<string, IType?>());

    // OPTIMIZATION: FHIR primitive type mapping (static to avoid repeated allocations)
    // Reference: http://hl7.org/fhir/datatypes.html
    // Most FHIR primitive types use lowercase names, but a few require special casing preservation.
    // We split these into two collections for efficiency:
    // 1. SpecialCasedPrimitives: Dictionary for types with non-lowercase casing (5 entries)
    // 2. LowercasePrimitives: FrozenSet for lowercase types (14 entries) - faster lookups in .NET 9+

    // Primitive types with special (non-lowercase) casing that must be preserved
    private static readonly Dictionary<string, string> SpecialCasedPrimitives = new(StringComparer.OrdinalIgnoreCase)
    {
        { "dateTime", "dateTime" },
        { "base64Binary", "base64Binary" },
        { "unsignedInt", "unsignedInt" },
        { "positiveInt", "positiveInt" },
        { "integer64", "integer64" }
    };

    // All lowercase primitive types (for validation and normalization)
    private static readonly FrozenSet<string> LowercasePrimitives = FrozenSet.ToFrozenSet(
    [
        "string", "integer", "boolean", "decimal", "date", "time",
        "code", "uri", "url", "canonical", "uuid", "oid", "id",
        "markdown", "instant"
    ], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Public constructor for root elements (resources)
    /// </summary>
    public SchemaAwareElement(ISourceNavigator source, ISchema schema, IType? definition = null, string? instanceType = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _definition = definition;
        _instanceType = instanceType ?? DeriveInstanceType(source, definition);

        // Lazy initialization - fetch type definition when needed
        _typeDefinition = new Lazy<IType?>(() =>
        {
            // Use the derived instance type to get type definition
            if (!string.IsNullOrEmpty(_instanceType))
            {
                return _schema.GetTypeDefinition(_instanceType);
            }

            return null;
        });
    }

    /// <summary>
    /// Derives the instance type for an element based on its source node and definition.
    /// KEY INSIGHT: For BackboneElements, the Info.Name IS the qualified type name (e.g., "QuestionnaireResponse.item").
    /// For elements with ITypeExtended definition, use DefaultTypeName or Types[0] for the actual FHIR type.
    /// </summary>
    private static string? DeriveInstanceType(ISourceNavigator source, IType? definition)
    {
        // For resources, check for resourceType property first (exposed via ISourceNavigator.ResourceType)
        var resourceTypeIndicator = source.ResourceType;

        if (definition != null && definition.Info.IsResource)
        {
            return resourceTypeIndicator;
        }

        // For choice elements (value[x]), extract type from property name suffix
        if (definition != null && definition.Info.IsChoiceElement)
        {
            var elementBaseName = definition.Info.Name.EndsWith("[x]", StringComparison.Ordinal)
                ? definition.Info.Name.TrimEnd("[x]".ToCharArray())
                : definition.Info.Name;

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

        // For elements with an ITypeExtended definition, use DefaultTypeName or Types[0]
        // This provides the actual FHIR type (e.g., "code", "CodeableConcept")
        // as opposed to Info.Name which is the element name (e.g., "status", "code")
        if (definition is ITypeExtended extendedDef)
        {
            // Use DefaultTypeName if available
            if (!string.IsNullOrEmpty(extendedDef.DefaultTypeName))
            {
                return extendedDef.DefaultTypeName;
            }

            // Use first type from Types array if available
            if (extendedDef.Types.Count > 0)
            {
                return extendedDef.Types[0].Code;
            }
        }

        // For elements with a type definition, use the type name
        // For BackboneElements, the Info.Name is already the qualified name we want
        if (definition != null)
        {
            var typeName = definition.Info.Name;

            // BackboneElements have qualified Info.Name like "QuestionnaireResponse.item"
            // Primitive and complex types have simple names like "string", "HumanName"
            // If the name contains a dot, it's likely a BackboneElement - use it as-is
            if (typeName.Contains('.', StringComparison.Ordinal))
            {
                return typeName;
            }

            // For simple types without ITypeExtended, return the type name from Info
            // This path is mainly for backward compatibility
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

    public string InstanceType => _instanceType ?? string.Empty;

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
                // FHIRPath engine handles type checking via InstanceType, no prefix needed here
                _ => text
            };
        }
    }

    public string Location => _source.Location;

    public IType? Type => _definition;

    public IReadOnlyList<IElement> Children(string? name)
    {
        // Handle polymorphic properties (value[x] in FHIR spec)
        // According to FHIRPath N1 spec section 3.2, accessing "value" should match
        // "valueCode", "valueString", "valueQuantity", etc.
        IEnumerable<ISourceNavigator> sourceChildren;

        if (name != null && !name.EndsWith("[x]", StringComparison.Ordinal))
        {
            // Try exact match first
            sourceChildren = _source.Children(name);

            // If no exact match and we have a definition, check for polymorphic (choice) properties
            if (!sourceChildren.Any())
            {
                var cachedTypeDef = _typeDefinition.Value;
                if (cachedTypeDef != null)
                {
                    // Check if this is a choice element (IsChoiceElement == true)
                    // OR if there's an element with [x] suffix
                    var choiceElement = cachedTypeDef.Children
                        .FirstOrDefault(e => (e.Info.Name == name && e.Info.IsChoiceElement) ||
                                              e.Info.Name == name + "[x]");

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

        // Wrap source children in IElement
        var result = new List<IElement>();
        foreach (var child in sourceChildren)
        {
            // OPTIMIZATION: Use cached type definition lookup (immutable per instance)
            var cachedTypeDef = _typeDefinition.Value;
            IType? childDef = null;
            string? childInstanceType = null;

            if (cachedTypeDef != null)
            {
                // For BackboneElements, check if a qualified type exists (e.g., "QuestionnaireResponse.item")
                var qualifiedName = $"{cachedTypeDef.Info.Name}.{child.Name}";
                var qualifiedTypeDef = _schema.GetTypeDefinition(qualifiedName);

                if (qualifiedTypeDef != null)
                {
                    // This child is a BackboneElement with its own type definition
                    // Use the qualified typename directly as the instance type
                    childInstanceType = qualifiedTypeDef.Info.Name;
                }
                else
                {
                    // Check for recursive BackboneElements (e.g., QuestionnaireResponse.item.item)
                    // The parent InstanceType might already be the qualified name we need
                    // Extract last segment of parent's TypeName and compare with child name
                    var parentTypeName = cachedTypeDef.Info.Name;
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
                childDef = GetCachedChildDefinition(child.Name, cachedTypeDef);
            }

            // If we didn't already determine the instance type (not a BackboneElement),
            // derive it using the standard method
            if (childInstanceType == null)
            {
                childInstanceType = DeriveInstanceType(child, childDef);
            }

            // Create child node with explicit instance type
            result.Add(new SchemaAwareElement(child, _schema, childDef, childInstanceType));
        }

        return result;
    }

    /// <summary>
    /// Gets or creates a cache of child element definitions.
    /// Avoids repeated lookups of the same child name across multiple navigations.
    /// Returns null if no definition found (valid - not all elements have definitions).
    /// Thread-safe: uses ConcurrentDictionary for atomic get-or-add semantics.
    /// </summary>
    private IType? GetCachedChildDefinition(string childName, IType? cachedTypeDef)
    {
        // No type definition? Can't cache anything
        if (cachedTypeDef == null)
            return null;

        var cache = _childDefinitionCache.Value;

        // Return from cache if found (even if value is null, which is valid)
        if (cache.TryGetValue(childName, out var cachedDef))
            return cachedDef;

        // Cache miss: Look up definition
        // For BackboneElements, try to get the qualified type definition directly
        // (e.g., schema.GetTypeDefinition("QuestionnaireResponse.item"))
        var qualifiedName = $"{cachedTypeDef.Info.Name}.{childName}";
        var qualifiedTypeDef = _schema.GetTypeDefinition(qualifiedName);

        // If we found a qualified type definition for this child (it's a BackboneElement),
        // use it as the definition
        IType? childDef = null;
        if (qualifiedTypeDef != null)
        {
            childDef = qualifiedTypeDef;
        }

        // If no qualified type def, try exact match from parent's children (for primitives/simple types)
        if (childDef == null)
        {
            childDef = cachedTypeDef.Children.FirstOrDefault(e => e.Info.Name == childName);
        }

        // If still no match, check if this is a choice type variant (e.g., valueString for value[x])
        if (childDef == null)
        {
            var choiceElement = cachedTypeDef.Children
                .FirstOrDefault(e =>
                {
                    // Check if it's a choice element by flag OR by [x] suffix
                    if (!e.Info.IsChoiceElement && !e.Info.Name.EndsWith("[x]", StringComparison.Ordinal))
                        return false;

                    // Extract base name: "value[x]" → "value" or just use "value" if IsChoiceElement
                    var baseName = e.Info.Name.EndsWith("[x]", StringComparison.Ordinal)
                        ? e.Info.Name.TrimEnd("[x]".ToCharArray())
                        : e.Info.Name;

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
            var typeName = cachedTypeDef.Info.Name;
            var qualifiedChoiceElement = cachedTypeDef.Children
                .FirstOrDefault(e =>
                {
                    // Extract base name from qualified choice element (e.g., "Observation.value[x]" → "value")
                    var elementName = e.Info.Name;
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
    /// Normalizes FHIR type names extracted from choice elements to match FHIR/FHIRPath conventions.
    /// Primitive type names use FHIR's exact casing (e.g., "String" → "string", "DateTime" → "dateTime"),
    /// while complex types remain capitalized (e.g., "Quantity", "CodeableConcept").
    /// </summary>
    /// <param name="typeName">The type name extracted from the choice element suffix (e.g., "String", "Quantity").</param>
    /// <returns>The normalized type name per FHIR conventions.</returns>
#pragma warning disable CA1308 // FHIR spec requires lowercase primitive type names
    private static string NormalizeFhirPathTypeName(string typeName)
    {
        // Check special-cased types first (dateTime, base64Binary, unsignedInt, positiveInt, integer64)
        if (SpecialCasedPrimitives.TryGetValue(typeName, out var canonicalName))
            return canonicalName;

        // Check lowercase primitives (string, integer, boolean, etc.)
        if (LowercasePrimitives.Contains(typeName))
            return typeName.ToLowerInvariant();

        // Not a primitive type - keep complex types as-is (Quantity, CodeableConcept, etc.)
        return typeName;
    }
#pragma warning restore CA1308

    /// <summary>
    /// Retrieves metadata of the specified type (IElement interface).
    /// </summary>
    public T? Meta<T>() where T : class
    {
        return _source.Meta<T>();
    }
}

/// <summary>
/// Extension methods for converting ISourceNavigator to schema-aware elements.
/// </summary>
public static class SchemaAwareElementExtensions
{
    /// <summary>
    /// Converts an ISourceNavigator to an IElement using schema metadata.
    /// </summary>
    /// <param name="source">The source node to wrap.</param>
    /// <param name="schema">The schema provider for type information.</param>
    /// <returns>An IElement with type information from the schema.</returns>
    public static IElement ToElement(this ISourceNavigator source, ISchema schema)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(schema);

        return new SchemaAwareElement(source, schema);
    }
}
