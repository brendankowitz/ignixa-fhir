# Legacy Type Migration Plan

## Executive Summary

This document outlines the systematic removal of legacy interfaces from `Ignixa.Abstractions/Legacy` folder. The modern replacements (`IElement`, `IType`, `ISchema`) are already implemented and partially adopted.

## Current State Analysis

### Legacy Types (13 total)

| Type | Modern Replacement | Status | Removal Strategy |
|------|-------------------|--------|------------------|
| `ISourceNode` | `IElement` | High usage (979 refs) | Migrate validation layer |
| `ITypedElement` | `IElement` | High usage (248 refs) | Already bridged in `SchemaAwareElement` |
| `IAnnotated` | `IElement.Meta<T>()` | Low usage (19 refs) | Replace with Meta<T> pattern |
| `IBaseElementNavigator` | N/A (internal) | Very low (7 refs) | Remove with ITypedElement |
| `IResourceTypeSupplier` | `IElement.InstanceType` | Low (10 refs) | Inline resourceType detection |
| `XmlRepresentation` | **KEEP** | Very high (81K - generated) | Move out of Legacy |
| `IElementDefinitionSummary` | `IType` | High (7K - generated) | Update generators |
| `ITypeSerializationInfo` | N/A (marker) | Moderate (1K - generated) | Remove with generators |
| `IStructureDefinitionSummary` | `ISchema` return | Moderate (118 refs) | Already migrating |
| `IStructureDefinitionReference` | N/A (internal) | Low (21 refs) | Remove with generators |
| `IStructureDefinitionSummaryProvider` | `ISchema` | Moderate (53 refs) | Already migrating |
| `TypeSerializationInfoExtensions` | N/A | Very low (1 ref) | Remove with generators |
| `AnnotatedExtensions` | N/A | Very low (7 refs) | Inline or remove |

### Modern Interfaces (Already Implemented)

1. **`IElement`** - Replaces `ITypedElement` and `ISourceNode`
   - `SchemaAwareElement` already implements both!
   - Uses `IReadOnlyList<IElement>` instead of `IEnumerable<ITypedElement>`
   - Has `Meta<T>()` replacing `IAnnotated.Annotations()`

2. **`IType`** - Replaces `IElementDefinitionSummary`
   - `ElementTypeAdapter` bridges old to new
   - Generated `*CoreSchemaProvider` classes implement `IType`

3. **`ISchema`** - Replaces `IStructureDefinitionSummaryProvider`
   - Generated `R4CoreSchemaProvider`, etc. implement `ISchema`
   - Provides FHIR version awareness

## Migration Phases

### Phase 1: Move XmlRepresentation Out of Legacy (KEEP)

`XmlRepresentation` is used by `IType.Representation` and is fundamental for XML serialization. It should NOT be removed.

**Action**: Move from `/Legacy/XmlRepresentation.cs` to parent `/XmlRepresentation.cs`

### Phase 2: Migrate Validation Layer (ISourceNode -> IElement)

The validation layer is the heaviest user of `ISourceNode`. Key changes:

1. `IValidationCheck.Validate(ISourceNode node, ...)` -> `IValidationCheck.Validate(IElement element, ...)`
2. Update all 13+ check implementations
3. Replace `node.Children(name)` with `element.Children(name)` (same signature!)
4. Replace `node.Text` with `element.Value?.ToString()` or proper Value handling
5. Replace `node.Location` with `element.Location` (same!)

**Key insight**: `ISourceNode.Text` -> `IElement.Value` (object, with type conversion)

### Phase 3: Migrate Search Layer (ITypedElement -> IElement)

The search indexer already casts `ITypedElement` to `IElement`:
```csharp
var element = resource as IElement ?? throw new InvalidOperationException("...");
```

1. Change `ISearchIndexer.Extract(ITypedElement)` -> `ISearchIndexer.Extract(IElement)`
2. Update all converters (`ITypedElementToSearchValueConverter` -> `IElementToSearchValueConverter`)
3. Update resolver interface

### Phase 4: Remove IAnnotated/AnnotatedExtensions

Replace usages with `IElement.Meta<T>()`:
- `annotated.Annotation<JsonNode>()` -> `element.Meta<JsonNode>()`

### Phase 5: Update Code Generators

The generators produce:
1. `*StructureDefinitionSummaryProvider` (uses `IElementDefinitionSummary`, `ITypeSerializationInfo`)
2. `*CoreSchemaProvider` (uses `IType`, `ISchema`)

**Action**: Stop generating old providers OR phase them out once all consumers migrate.

### Phase 6: Final Cleanup

Delete from `/Legacy/`:
- `ISourceNode.cs`
- `ITypedElement.cs`
- `IBaseElementNavigator.cs`
- `IAnnotated.cs`
- `AnnotatedExtensions.cs`
- `IResourceTypeSupplier.cs`
- `IElementDefinitionSummary.cs`
- `ITypeSerializationInfo.cs`
- `IStructureDefinitionSummary.cs`
- `IStructureDefinitionReference.cs`
- `IStructureDefinitionSummaryProvider.cs`
- `TypeSerializationInfoExtensions.cs`

KEEP (moved to parent):
- `XmlRepresentation.cs`

## Implementation Order

1. **XmlRepresentation** - Move out of Legacy (non-breaking)
2. **Validation Layer** - Most impactful, unlocks ISourceNode removal
3. **Search Layer** - Medium impact
4. **Serialization Layer** - Already dual-implements
5. **Code Generators** - Can run in parallel with above
6. **Final Deletion** - Once all references removed

## Breaking Changes

| Change | Impact | Mitigation |
|--------|--------|------------|
| `IValidationCheck.Validate` signature | Internal API | No external consumers |
| `ISearchIndexer.Extract` signature | Internal API | No external consumers |
| Converter interfaces renamed | Internal API | No external consumers |
| Legacy types removed | Breaking if external use | Mark obsolete first (optional) |

## Testing Strategy

1. Build after each phase
2. Run full test suite
3. Verify generated code still works
4. Integration tests for validation + search

## Timeline Estimate

| Phase | Effort |
|-------|--------|
| Phase 1 (XmlRepresentation) | 5 minutes |
| Phase 2 (Validation) | 2-3 hours |
| Phase 3 (Search) | 1-2 hours |
| Phase 4 (IAnnotated) | 30 minutes |
| Phase 5 (Generators) | 1-2 hours |
| Phase 6 (Cleanup) | 30 minutes |
| **Total** | **~6-8 hours** |
