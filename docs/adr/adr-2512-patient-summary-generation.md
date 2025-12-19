# ADR-2512: Patient Summary Generation

**Status**: Proposed
**Date**: 2025-12-19
**Feature**: fhir-operations

## Context

FHIR Implementation Guides define patient summaries (IPS, AU PS, US IPS, EU PS) using the `$summary` operation. These summaries aggregate clinical data into standardized documents for cross-border care and care transitions.

Current challenges:
1. **No IPS support** - Server lacks patient summary generation capability
2. **Hard-coded strategies** - Section definitions manually coded per jurisdiction
3. **Maintenance burden** - Each IG update requires code changes
4. **Limited extensibility** - Cannot support new jurisdictions without new strategy classes

The opportunity: FHIR IGs already define summary sections via StructureDefinition slicing in Composition profiles. This metadata contains LOINC codes, cardinality, entry profiles, and resource types—everything needed for generation.

## Related Investigations

- [IPS Generator](../features/fhir-operations/investigations/ips-generator.md) - Detailed implementation design for IPS generation, including architecture, performance optimization, and HAPI FHIR analysis
- [StructureDefinition-Driven Summaries](../features/fhir-operations/investigations/structuredefinition-driven-summaries.md) - Metadata-driven strategy architecture, parsing logic, and package-based registration

## Options Considered

1. **Hard-coded per-jurisdiction strategies** *(rejected: maintenance burden)*
   - Create `DefaultIpsGenerationStrategy`, `AuPsGenerationStrategy`, etc.
   - Manually code all section definitions
   - **Issue**: Every IG update requires code changes, cannot support custom summaries

2. **StructureDefinition-driven generation** *(viable)*
   - Parse Composition profiles from installed packages
   - Extract section metadata from StructureDefinition snapshots
   - Auto-register strategies when packages installed
   - **Benefit**: Zero-code jurisdiction support, IG updates = package updates

3. **Template-based generation** *(rejected: incomplete)*
   - Use external templates (Liquid, Mustache) to define sections
   - **Issue**: Still requires manual template creation, doesn't leverage FHIR metadata

## Decision

Implement **StructureDefinition-driven patient summary generation** combining:

1. **Metadata-driven strategy creation**
   - Parse Composition profile section slices from StructureDefinition snapshots
   - Extract LOINC codes, cardinality (Required/Recommended), entry profiles, resource types
   - Auto-create `IIpsGenerationStrategy` instances from metadata

2. **Package-based registration**
   - Install `hl7.fhir.uv.ips` → IPS strategy auto-registered
   - Install `hl7.fhir.au.ps` → AU PS strategy auto-registered
   - Custom Composition profiles → custom strategies auto-registered

3. **Single-query optimization**
   - UNION all IPS resource types in one compartment query (vs HAPI's 20-25 sequential queries)
   - Stream results with type-based post-filtering
   - 6x faster than HAPI's sequential approach

4. **Strategy registry**
   - `IIpsGenerationStrategyRegistry` manages multiple strategies
   - Select by `?profile=` parameter or CapabilityStatement default
   - Fallback to hard-coded IPS if no packages installed

### Key Components

**Core Interfaces**:
- `IIpsGeneratorService` - Orchestrates IPS generation workflow
- `IIpsGenerationStrategy` - Pluggable jurisdiction-specific strategy
- `IStructureDefinitionStrategyFactory` - Parses StructureDefinition → strategy
- `IIpsGenerationStrategyRegistry` - Manages available strategies

**Metadata Parsing**:
- `SectionMetadataParser` - Extracts section metadata from StructureDefinition snapshots
- `StructureDefinitionBasedStrategy` - Strategy built from parsed metadata
- `PackageInstalledStrategyRegistrationHandler` - Auto-registers on package install

**API Endpoints**:
```
GET  /Patient/{id}/$summary
GET  /Patient/$summary?identifier={system}|{value}
POST /Patient/{id}/$summary
POST /Patient/$summary (with identifier in Parameters)
```

**Profile Selection Priority**:
1. Explicit `?profile=` parameter
2. First profile from CapabilityStatement
3. Global default (IPS)

## Consequences

### Positive

- **Zero-code jurisdiction support**: Install package → summary automatically available
- **IG updates simplified**: Update package → sections refresh automatically
- **Custom summaries enabled**: Upload custom Composition profile → instant generation
- **Performance optimized**: Single query vs 20+ sequential (6x faster)
- **Standards-driven**: Leverages existing FHIR metadata, no duplication
- **Maintenance reduced**: ~300 LOC saved per jurisdiction

### Negative

- **Complexity added**: StructureDefinition parsing logic (~200 LOC)
- **Snapshot dependency**: Packages must contain snapshots (or generate on-the-fly)
- **Parse cost**: One-time ~50ms per StructureDefinition on package install
- **Testing requirements**: Need test fixtures for various IG patterns

### Follow-up Work

1. **Narrative generation** - Implement Scriban template engine for section narratives
2. **Caching strategy** - Tiered caching (Bundle-level 5min TTL, section-level 15min TTL)
3. **Async support** - DurableTask orchestration for large patient records (>500 resources)
4. **Profile validation** - Optional validation against IPS profiles
5. **Custom filtering** - FHIRPath-based entry filtering via StructureDefinition extensions
