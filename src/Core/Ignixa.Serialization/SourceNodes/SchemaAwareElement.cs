// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using System.Runtime.CompilerServices;
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

    // OPTIMIZATION: Memoise the parsed Value without a Lazy<T>, which cost a Lazy, its LazyHelper lock
    // object and a closure delegate on every element constructed — allocation the navigation and
    // search-indexing paths pay whether or not Value is ever read.
    // ComputeValue() can legitimately return null, so null cannot mark "not computed": a sentinel does.
    // The field is volatile so the reference publishes with release semantics. ComputeValue() is pure,
    // so a race can only duplicate the work, never publish a half-built value.
    //
    // CORRECTNESS PRECONDITION: an element is a snapshot of its source node, not a live view of the
    // document. Memoising is only sound because ISourceNavigator.Text is contractually stable for the
    // lifetime of a navigator instance (see ISourceNavigator.Text) — so this caches a value that could
    // not have changed anyway, rather than freezing one that could. JsonNodeSourceNode upholds it by
    // capturing the backing JsonValue by reference at construction: System.Text.Json edits replace a
    // node in its parent instead of mutating it, so the captured instance never changes, and the node
    // already snapshots its children into _cachedNodes on first navigation for the same reason.
    // The way to observe an edit is therefore to re-derive the tree — ResourceJsonNode.InvalidateCaches()
    // followed by ToElement() — never to re-read an element captured before the edit. Callers that hold
    // an element tree across a mutation of the same document already read pre-mutation values without
    // this memo; removing it would not make them correct.
    private static readonly object ValueNotComputed = new();

    private volatile object? _cachedValue = ValueNotComputed;

    // OPTIMIZATION: Memoise the schema type lookup the same way, for the same reason. The former
    // Lazy<IType?> captured `this`, so every element paid for a Lazy, its LazyHelper lock object and
    // a bound delegate whether or not the type definition was ever needed.
    // GetTypeDefinition() returns null for unknown types, so null cannot mark "not computed" either.
    // One volatile reference field means one atomic write, with no flag-versus-value ordering to get
    // wrong. Every ISchema implementation resolves types from an interned table, so a race can only
    // repeat the lookup and always republishes the identical reference.
    private static readonly object TypeDefinitionNotComputed = new();

    private volatile object? _cachedTypeDefinition = TypeDefinitionNotComputed;

    // OPTIMIZATION: Child resolution is cached per (parent type, child name) for the whole process
    // rather than per element instance.
    //
    // Resolving a child answers two questions - which IType defines it, and whether it is a
    // BackboneElement whose qualified name becomes the child's InstanceType - and both answers depend
    // only on the parent's IType and the child's name. Neither varies by element instance, yet the
    // cache used to live on the instance, where it was useless: a navigation hop builds a wrapper,
    // reads one definition through it and drops it, so the dictionary was allocated (1,016 B on a
    // 16-core host, since the parameterless ctor sizes its lock array by ProcessorCount) and thrown
    // away having served a single lookup. Worse, the miss path ran twice per child - once here and
    // once inside the definition lookup - each time building a "Parent.child" string and probing a
    // schema that answers null for every leaf property, and the generated providers spend a Split +
    // per-segment Substring + Join on that miss before returning null.
    //
    // The cache is partitioned by ISchema, not merely by IType. Resolution asks the schema to resolve a
    // qualified "Parent.child" name, and two schemas can hand out the same base IType instances while
    // answering that question differently - a decorating or profile-aware schema over a core provider
    // does exactly that. Keying on the type alone would let one schema's answer be served to another.
    // Within a partition the type instance is still part of the key, which keeps STU3-through-R6
    // entries apart even though every version calls its type "Patient", and is safe because every
    // ISchema resolves types from an interned table.
    //
    // ConditionalWeakTable anchors the entry's lifetime to the schema: tenant providers are loaded and
    // dropped at runtime, and a static dictionary would root every one of them forever.
    private static readonly ConditionalWeakTable<ISchema, ConcurrentDictionary<ChildResolutionKey, ChildResolution>> SharedChildResolutions = new();

    /// <summary>
    /// Identifies a child resolution within one schema's partition. Reference equality on the parent
    /// type is deliberate and spelled out rather than inherited: <see cref="IType"/> is an interface, so
    /// an implementation is free to define value equality, and a type that compared equal by name would
    /// silently merge entries this key exists to keep apart.
    /// </summary>
    private readonly struct ChildResolutionKey : IEquatable<ChildResolutionKey>
    {
        private readonly IType _parentType;
        private readonly string _childName;

        public ChildResolutionKey(IType parentType, string childName)
        {
            _parentType = parentType;
            _childName = childName;
        }

        public bool Equals(ChildResolutionKey other) =>
            ReferenceEquals(_parentType, other._parentType)
            && string.Equals(_childName, other._childName, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is ChildResolutionKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            RuntimeHelpers.GetHashCode(_parentType),
            StringComparer.Ordinal.GetHashCode(_childName));
    }

    /// <summary>
    /// What resolving a child name against a parent type yields: the child's definition, and the
    /// qualified instance type when the child is a BackboneElement (null when it is not, leaving the
    /// instance type to be derived from the child's own source node).
    /// </summary>
    private readonly record struct ChildResolution(IType? Definition, string? QualifiedInstanceType);

    // OPTIMIZATION: The wrapper elements themselves are cached against the source node that backs them.
    //
    // Resolution caching above removed the schema work from a navigation hop but not the wrappers: every
    // Children() call still built a fresh List and a fresh SchemaAwareElement per child, so walking
    // "component.valueQuantity.value" allocated a new object at each level on every evaluation, and an
    // indexing pass that runs a hundred expressions over one resource rebuilt the same tree a hundred
    // times. Anchoring the wrappers to the source node instead makes the second and later walks free.
    //
    // The source layer already works this way - JsonNodeSourceNode snapshots its children into
    // _cachedNodes on first navigation and hands back the same navigator instances forever - so this
    // extends an existing lifetime rather than inventing one, and the elements it caches are documented
    // immutable snapshots of exactly those nodes (see the class remarks).
    //
    // Mutation stays correct without an explicit invalidation hook: ResourceJsonNode.InvalidateCaches()
    // drops _cachedSourceNode, so a patched document is re-derived from new navigators, which key a new
    // and empty entry here. The stale wrappers become unreachable with the navigators they wrapped.
    //
    // The key carries the schema and the parent's instance type as well as the child name: one source
    // node can legitimately be wrapped by more than one element - under a different schema, or typed
    // differently through a choice element - and those wrappings have different children.
    private static readonly ConditionalWeakTable<ISourceNavigator, ConcurrentDictionary<ChildListKey, IReadOnlyList<IElement>>> SharedChildElements = new();

    /// <summary>
    /// Identifies one materialized child list for a given source node: which schema typed it, what the
    /// parent's instance type resolved to, and which child name was asked for (null meaning "all").
    /// </summary>
    private readonly struct ChildListKey : IEquatable<ChildListKey>
    {
        private readonly ISchema _schema;
        private readonly string? _parentInstanceType;
        private readonly string? _name;

        public ChildListKey(ISchema schema, string? parentInstanceType, string? name)
        {
            _schema = schema;
            _parentInstanceType = parentInstanceType;
            _name = name;
        }

        public bool Equals(ChildListKey other) =>
            ReferenceEquals(_schema, other._schema)
            && string.Equals(_parentInstanceType, other._parentInstanceType, StringComparison.Ordinal)
            && string.Equals(_name, other._name, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is ChildListKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            RuntimeHelpers.GetHashCode(_schema),
            _parentInstanceType is null ? 0 : StringComparer.Ordinal.GetHashCode(_parentInstanceType),
            _name is null ? 0 : StringComparer.Ordinal.GetHashCode(_name));
    }

    // OPTIMIZATION: FHIR primitive type mapping (static to avoid repeated allocations)
    // Reference: http://hl7.org/fhir/datatypes.html
    // Most FHIR primitive types use lowercase names, but a few require special casing preservation.
    // We split these into two collections for efficiency:
    // 1. SpecialCasedPrimitives: Dictionary for types with non-lowercase casing (5 entries)
    // 2. LowercasePrimitives: FrozenSet for lowercase types (15 entries) - faster lookups in .NET 9+

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
            if (typeName != null && typeName.Contains('.', StringComparison.Ordinal))
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
            var cached = _cachedValue;
            if (ReferenceEquals(cached, ValueNotComputed))
            {
                cached = ComputeValue();
                _cachedValue = cached;
            }

            return cached;
        }
    }

    private object? ComputeValue()
    {
        var text = _source.Text;
        if (text == null) return null;

        // Convert primitive FHIR types to their native C# types for proper FHIRPath evaluation.
        // The FHIR wire format is always invariant: '.' is the decimal separator and group separators
        // are never present. Parsing under CurrentCulture silently corrupts data — on de-DE, '.' is the
        // group separator, so the default NumberStyles.Number would read "1.5" as 15. NumberStyles.Float
        // is used rather than Number because it excludes AllowThousands and includes AllowExponent,
        // which FHIR decimals permit (e.g. "1.2e3").
        return InstanceType switch
        {
            "boolean" => bool.TryParse(text, out var b) ? b : text,
            "integer" or "unsignedInt" or "positiveInt" =>
                int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : text,
            "decimal" =>
                decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : text,
            "date" => FhirTemporal.TryParse(text, FhirPrimitive.Date, out var td) ? td : text,
            "dateTime" => FhirTemporal.TryParse(text, FhirPrimitive.DateTime, out var tdt) ? tdt : text,
            "instant" => FhirTemporal.TryParse(text, FhirPrimitive.Instant, out var ti) ? ti : text,
            "time" => FhirTemporal.TryParse(text, FhirPrimitive.Time, out var tt) ? tt : text,
            // FHIRPath engine handles type checking via InstanceType, no prefix needed here
            _ => text
        };
    }

    public string Location => _source.Location;

    public IType? Type => _definition ?? TypeDefinition;

    private IType? TypeDefinition
    {
        get
        {
            var cached = _cachedTypeDefinition;
            if (ReferenceEquals(cached, TypeDefinitionNotComputed))
            {
                cached = string.IsNullOrEmpty(_instanceType) ? null : _schema.GetTypeDefinition(_instanceType);
                _cachedTypeDefinition = cached;
            }

            return (IType?)cached;
        }
    }

    /// <summary>
    /// Resolves a child name against a parent type, memoised for the lifetime of that type.
    /// </summary>
    /// <remarks>
    /// The schema is both an argument and the cache partition, so a resolution computed under one
    /// schema can never be served under another.
    /// </remarks>
    private static ChildResolution ResolveChild(IType parentTypeDef, string childName, ISchema schema)
    {
        var perSchema = SharedChildResolutions.GetValue(
            schema,
            static _ => new ConcurrentDictionary<ChildResolutionKey, ChildResolution>());

        var key = new ChildResolutionKey(parentTypeDef, childName);

        if (perSchema.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var resolved = ComputeChildResolution(parentTypeDef, childName, schema);

        // A losing race recomputes an identical value, so TryAdd's discard costs nothing.
        perSchema.TryAdd(key, resolved);
        return resolved;
    }

    public IReadOnlyList<IElement> Children(string? name)
    {
        var childCache = SharedChildElements.GetValue(
            _source,
            static _ => new ConcurrentDictionary<ChildListKey, IReadOnlyList<IElement>>());

        var cacheKey = new ChildListKey(_schema, _instanceType, name);

        if (childCache.TryGetValue(cacheKey, out var cachedChildren))
        {
            return cachedChildren;
        }

        var materialized = MaterializeChildren(name);

        // A losing race produces an equivalent list; discarding it costs one wasted materialization and
        // keeps every caller on a single instance, which matters because these are handed out as shared
        // immutable snapshots.
        return childCache.GetOrAdd(cacheKey, materialized);
    }

    private IReadOnlyList<IElement> MaterializeChildren(string? name)
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
                var cachedTypeDef = TypeDefinition;
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
            var cachedTypeDef = TypeDefinition;
            IType? childDef = null;
            string? childInstanceType = null;

            if (cachedTypeDef != null)
            {
                // One memoised lookup answers both questions. These used to be computed separately,
                // each re-deriving the same "Parent.child" probe on every navigation.
                var resolution = ResolveChild(cachedTypeDef, child.Name, _schema);
                childInstanceType = resolution.QualifiedInstanceType;
                childDef = resolution.Definition;
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

        // Frozen to an array before being cached. The list is now handed to every caller that navigates
        // this node, and an IReadOnlyList<T> that is really a List<T> can be cast back to a mutable one.
        // An empty result is the common case for a name a document does not carry, so it costs nothing.
        return result.Count == 0 ? Array.Empty<IElement>() : result.ToArray();
    }

    /// <summary>
    /// Resolves a child name against its parent type from scratch. Pure, and the only caller is
    /// <see cref="ResolveChild"/>, which memoises the answer - so the cost here is paid once per
    /// (schema, parent type, child name) rather than once per navigation.
    /// </summary>
    /// <returns>
    /// A resolution whose <c>Definition</c> is null when the schema describes no such child, which is
    /// legitimate - not every element in a document has a definition.
    /// </returns>
    private static ChildResolution ComputeChildResolution(IType cachedTypeDef, string childName, ISchema schema)
    {
        // For BackboneElements, try to get the qualified type definition directly
        // (e.g., schema.GetTypeDefinition("QuestionnaireResponse.item"))
        var qualifiedName = $"{cachedTypeDef.Info.Name}.{childName}";
        var qualifiedTypeDef = schema.GetTypeDefinition(qualifiedName);

        // A BackboneElement's qualified name is also its instance type.
        string? qualifiedInstanceType = null;
        if (qualifiedTypeDef != null)
        {
            qualifiedInstanceType = qualifiedTypeDef.Info.Name;
        }
        else
        {
            // A recursive or forward-referencing BackboneElement (e.g. QuestionnaireResponse.item.item,
            // or ExplanationOfBenefit.item.detail.subDetail.adjudication referring back up to
            // ExplanationOfBenefit.item.adjudication) is not detectable by name: the target is not
            // always the immediate parent, and can be an ancestor several levels up or even a sibling
            // (ValueSet.compose.exclude -> #ValueSet.compose.include). The schema's own
            // ContentReference is the authoritative - and only reliable - marker, so key off it
            // directly rather than approximating it with a name comparison.
            var childElementDef = cachedTypeDef.Children.FirstOrDefault(e => e.Info.Name == childName);
            if (childElementDef is ITypeExtended { ContentReference: { } contentReference })
            {
                var targetTypeName = contentReference.TrimStart('#');
                var targetTypeDef = schema.GetTypeDefinition(targetTypeName);

                // Every ContentReference in the generated schemas resolves (verified across all five
                // FHIR versions), but a miss is handled deliberately rather than assumed away: leaving
                // qualifiedInstanceType null here falls through to the normal DeriveInstanceType path
                // below instead of fabricating a type name for a target the schema cannot locate.
                if (targetTypeDef != null)
                {
                    qualifiedInstanceType = targetTypeDef.Info.Name;
                }
            }
        }

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

        return new ChildResolution(childDef, qualifiedInstanceType);
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

    /// <summary>
    /// Indicates whether this element has an actual primitive value (not just extensions).
    /// </summary>
    public bool HasPrimitiveValue => _source.HasPrimitiveValue;
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
