# ADR-2530: ISourceNode and IElement Interface Consolidation

## Status

**Proposed** | Date: 2025-11-27

## Context

The codebase has two navigation interfaces for FHIR data:

1. **ISourceNode** (Legacy) - Schema-less parsing interface
2. **IElement** (Modern) - Schema-aware navigation interface

The current architecture requires a two-step conversion:
```
JSON → ISourceNode (parsing) → IElement (via SchemaAwareElement with schema)
```

This investigation evaluates whether these interfaces should remain separate, be consolidated, or use a hybrid inheritance approach.

## Interface Comparison

### ISourceNavigator (renamed from ISourceNode)

```csharp
public interface ISourceNavigator
{
    string Name { get; }                              // Element name (e.g., "valueQuantity")
    string Text { get; }                              // Primitive value as STRING
    string Location { get; }                          // Dotted path for errors
    string ResourceType { get; }                      // From root "resourceType" property
    IEnumerable<ISourceNavigator> Children(string? name);  // Lazy enumeration
    IEnumerable<object> Annotations(Type type);       // Generic metadata
}
```

**Use cases**: JSON parsing, raw data access, validation before schema is known.

### IElement (652 usages across 115 files)

```csharp
public interface IElement
{
    string Name { get; }                              // Element name
    object? Value { get; }                            // Typed primitive (bool, int, decimal, string)
    string InstanceType { get; }                      // Runtime FHIR type (e.g., "Reference", "code")
    string Location { get; }                          // Dotted path for errors
    IType? Type { get; }                              // Schema metadata (nullable)
    IReadOnlyList<IElement> Children(string? name);   // Eager list
    T? Meta<T>() where T : class;                     // Typed metadata access
}
```

**Use cases**: FHIRPath evaluation, search indexing, validation, serialization.

### Key Differences

| Aspect | ISourceNavigator | IElement |
|--------|-------------|----------|
| Primitive values | `string Text` | `object? Value` (typed) |
| Type awareness | None | `InstanceType`, `Type` |
| Children return | `IEnumerable<T>` (lazy) | `IReadOnlyList<T>` (eager) |
| Schema required | No | Optional (null Type) |
| Metadata access | `Annotations(Type)` | `Meta<T>()` |

## Current Implementations

### 1. JsonNodeSourceNode (ISourceNavigator only)

```csharp
public class JsonNodeSourceNode : ISourceNavigator
{
    public string Name { get; }
    public string Text => /* JSON primitive as string */;
    public string Location { get; }
    public string ResourceType => /* from root "resourceType" property */;
    public IEnumerable<ISourceNavigator> Children(string? name);
    public IEnumerable<object> Annotations(Type type);
}
```

**Purpose**: Raw JSON structure navigation. No type awareness, no schema dependency. Handles FHIR-specific concerns like shadow properties (`_birthDate`) and choice type suffixes.

### 2. SchemaAwareElement (Wraps ISourceNavigator → IElement)

```csharp
internal class SchemaAwareElement : IElement
{
    private readonly ISourceNavigator _source;
    private readonly ISchema _schema;

    public string InstanceType => DeriveInstanceType(_source, _definition);
    public IType? Type => _definition;
    public object? Value => ConvertPrimitive(_source.Text, InstanceType);
}
```

This wrapper enriches an ISourceNavigator with schema information to provide proper `InstanceType` and `Type`.

## Options Evaluated

### Option 1: IElement Inherits from ISourceNavigator

```csharp
public interface ISourceNavigator
{
    string Name { get; }
    string? Text { get; }
    string Location { get; }
    IEnumerable<ISourceNavigator> Children(string? name = null);
}

public interface IElement : ISourceNavigator
{
    object? Value { get; }
    string InstanceType { get; }
    IType? Type { get; }
    new IReadOnlyList<IElement> Children(string? name = null);  // Covariant override
    T? Meta<T>() where T : class;
}
```

**Pros**:
- Single hierarchy - IElement can be used wherever ISourceNode is expected
- Backward compatible for ISourceNode consumers

**Cons**:
- Two `Children()` signatures cause ambiguity
- `Text` is redundant when `Value` exists
- Covariant return types can be confusing
- Forces IElement to always have Text (even when not meaningful)

**Verdict**: Not recommended due to interface pollution and ambiguity.

### Option 2: Common Base Interface (IFhirNode)

```csharp
public interface IFhirNode
{
    string Name { get; }
    string Location { get; }
}

public interface ISourceNode : IFhirNode
{
    string Text { get; }
    IEnumerable<ISourceNode> Children(string? name = null);
}

public interface IElement : IFhirNode
{
    object? Value { get; }
    string InstanceType { get; }
    IType? Type { get; }
    IReadOnlyList<IElement> Children(string? name = null);
}
```

**Pros**:
- Clean separation of concerns
- Common utilities can work with IFhirNode

**Cons**:
- Still requires conversion between hierarchies
- More complex type system
- Minimal benefit over current approach

**Verdict**: Over-engineered for our use case.

### Option 3: Keep Separate (Current Approach)

```
JSON → ISourceNode (parsing) → .ToElement(schema) → IElement (navigation)
```

**Pros**:
- Already implemented and working
- Clear lifecycle: parse → enrich → navigate
- Schema optional: can work with ISourceNode alone

**Cons**:
- Must call `.ToElement(schema)` for typed navigation
- Two parallel interface trees to maintain
- 91 ISourceNode usages to eventually migrate

**Verdict**: Acceptable but not optimal.

### Option 4: Unified IElement with Optional Schema (RECOMMENDED)

```csharp
public interface IElement
{
    string Name { get; }
    object? Value { get; }                            // Typed if known, raw string if not
    string InstanceType { get; }                      // Empty string if no schema
    string Location { get; }
    IType? Type { get; }                              // Null if no schema
    IReadOnlyList<IElement> Children(string? name = null);
    T? Meta<T>() where T : class;
}

// Extension for backward compatibility
public static class ElementExtensions
{
    public static string? Text(this IElement element) => element.Value?.ToString();
}
```

**Implementation pattern** (already exists!):
- `JsonNodeSourceNode` → Schema-less IElement (`Type=null`, limited `InstanceType`)
- `SchemaAwareElement` → Schema-aware IElement (`Type=IType`, proper `InstanceType`)

**Pros**:
- Single interface for all navigation
- Schema is optional (null Type, empty InstanceType is valid)
- JsonNodeSourceNode already implements this pattern
- `Text` becomes a computed extension method
- Eliminates interface duplication

**Cons**:
- Schema-less elements have degraded capabilities (empty InstanceType, null Type)
- Consumers must handle null Type gracefully

**Verdict**: Best balance of simplicity and functionality.

## Recommended Approach

**Option 4: Unified IElement with Optional Schema**

### Migration Path

1. **Add Text extension method to IElement**
   ```csharp
   public static string? Text(this IElement element) => element.Value?.ToString();
   ```

2. **Mark ISourceNode as obsolete**
   ```csharp
   [Obsolete("Use IElement instead. Schema-less navigation is supported via JsonNodeSourceNode.")]
   public interface ISourceNode { ... }
   ```

3. **Update consumers incrementally**
   - FHIRPath evaluation: Already uses IElement
   - Search indexing: Already uses IElement
   - Validation: Update to use IElement (replace `.Text` with `.Value?.ToString()`)
   - Bundle parsing: Use schema-less IElement

4. **Remove ISourceNode when all consumers migrated**

### Implementation Details

#### Schema-less IElement (JsonNodeSourceNode)

```csharp
public class JsonNodeSourceNode : IElement
{
    public string Name => _name;
    public object? Value => GetTypedValue();           // Native types from JSON
    public string InstanceType => ResourceType ?? "";  // Limited without schema
    public string Location => _location;
    public IType? Type => null;                        // No schema
    public IReadOnlyList<IElement> Children(string? name) => ...;
    public T? Meta<T>() where T : class => ...;
}
```

#### Schema-aware IElement (SchemaAwareElement)

```csharp
internal class SchemaAwareElement : IElement
{
    private readonly ISourceNode _source;  // Or IElement after migration
    private readonly ISchema _schema;

    public string InstanceType => DeriveInstanceType(_source, _definition);
    public IType? Type => _definition;     // Schema metadata
    // ... rest same
}
```

### Consumer Guidelines

| Scenario | Approach |
|----------|----------|
| Parse JSON only | Use `JsonNodeSourceNode` directly (schema-less IElement) |
| Need FHIRPath evaluation | Use `.ToElement(schema)` for proper type checking |
| Need validation | Use `.ToElement(schema)` for InstanceType-aware validation |
| Need search indexing | Use `.ToElement(schema)` for type converters |
| Raw string value needed | Use `element.Value?.ToString()` or `element.Text()` extension |

## Decision

**Adopt Option 3**: Keep ISourceNavigator and IElement separate with clear responsibilities.

The interfaces represent fundamentally different concerns:

1. **ISourceNavigator** - JSON/XML structure navigation (parsing layer)
   - Deals with raw FHIR wire format
   - String values (`Text`)
   - No type awareness
   - Implementation: `JsonNodeSourceNode`

2. **IElement** - Type-enriched navigation (semantic layer)
   - Deals with FHIR semantics
   - Typed values (`Value`)
   - Schema metadata (`Type`, `InstanceType`)
   - Implementation: `SchemaAwareElement`

The conversion pattern is intentional:
```
JSON → JsonNodeSourceNode (ISourceNavigator) → .ToElement(schema) → SchemaAwareElement (IElement)
```

This separation ensures:
- Parsing logic stays in parsing classes
- Type logic stays in type-aware classes
- Clear lifecycle: parse → enrich → navigate

## Consequences

### Positive
- Clear separation of concerns
- Parsing and type-checking logic are isolated
- Each interface has single responsibility
- Schema enrichment is explicit (`.ToElement(schema)`)

### Negative
- Two interface trees to maintain
- Must call `.ToElement(schema)` for typed navigation
- Some code duplication between implementations

### Neutral
- Migration of ISourceNode usages still needed (for unification on IElement in consumer code)
- SchemaAwareElement continues to wrap ISourceNavigator

## Implementation Status

**COMPLETED** (2025-11-27): Renamed `ISourceNode` to `ISourceNavigator` for .NET naming consistency.

| Phase | Status | Description |
|-------|--------|-------------|
| Rename ISourceNode → ISourceNavigator | ✅ Complete | Interface renamed with backward-compat alias |
| Update implementations | ✅ Complete | JsonNodeSourceNode, BaseSourceNode, SchemaAwareElement |
| Update all usages | ✅ Complete | ~185 files updated across solution |
| Build verification | ✅ Complete | 0 warnings, 0 errors |

The backward-compatibility alias `ISourceNode : ISourceNavigator` is marked `[Obsolete]` and will be removed in a future version.

## Related Documents

- [Legacy Type Migration Plan](./legacy-type-migration-plan.md) - Overall migration strategy
- [ADR-2523: Multi-tenancy](./ADR-2523-multi-tenancy.md) - Architecture context

## Appendix: Usage Statistics

```
ISourceNavigator (formerly ISourceNode): ~185 files updated
IElement: 652 occurrences across 115 files
```

IElement is the dominant interface (7:1 ratio). The ISourceNavigator rename aligns with .NET naming conventions (similar to `XPathNavigator`).
