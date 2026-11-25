# Feature: Fabric Silver Writer

**Status**: Partial Implementation (Phases 1–2 implemented; ADR 2611 Proposed, pending /accept-adr)
**Created**: 2026-11-24

## Problem Statement

Issue #550 asks for a native (non-Spark) writer that produces Fabric-compatible Silver Delta tables for
FHIR R4 `Patient` and `Observation`, matching the table layout and row semantics of the Healthcare data
solutions (HDS) Silver layer. Scope is Phase 1 (native writer), then a local Phase 2; it does not expand
beyond these two resource types.

## Constraints

- Must reproduce the Silver column layout, `id`/`idOrig`, identifier/reference normalization, `*Orig`
  columns, `_string` companions, lineage columns and upsert semantics documented in the investigation.
- Must not copy HDS code, schemas or [viewdefinition-depot](https://github.com/brendankowitz/viewdefinition-depot)
  contracts into this repository.
- No HDS-captured row oracle exists. Only **schema parity** (names, order, types, nullability, protocol,
  table properties) is checkable today; a schema match is **not** row parity.

## Decisions Status

| # | Decision | Status | Current direction |
|---|----------|--------|-------------------|
| 1 | Native column derivation | Proposed (awaiting owner ratification) | Option B: bypass ViewDefinitions and do a target-schema-driven walk of the parsed `ResourceJsonNode`, with the R4 `ISchema` deciding reference type validity. Silver leaves follow Spark `from_json` token semantics (source number text, compact JSON text for `extension`/`contained`/string slots, wire names), which the element tree normalizes away. Contained and reversible: the walker is internal and the public input is `ResourceJsonNode` |
| 2 | Delta library | Proposed ([ADR 2611](adr-2611-fabric-silver-delta-engine.md)) | Hybrid: Ignixa writes version 0; DeltaLake.Net owns data writes, MERGE, CDF and later commits, isolated in the alpha `Ignixa.SqlOnFhir.Fabric.Delta` package |
| 3 | Batch atomicity | Proposed ([ADR 2611](adr-2611-fabric-silver-delta-engine.md)) | Not atomic across tables; project and validate everything, create missing version 0s, then merge table by table; a typed error lists committed tables and replay repairs the rest |
| 4 | V-Order | Proposed | Set the table property; do not claim file-layout parity |
| 5a | Provenance: no redistribution | Proposed | The contract is loaded at runtime from a licensed local package at a caller-supplied path; no HDS schema text, data or code is committed |
| 5b | Redistribution clearance | Open | Whether HDS-derived material may be redistributed is not cleared |
| 6 | CDF property value (B1) | Proposed ([ADR 2611](adr-2611-fabric-silver-delta-engine.md)) | Ignixa-created tables write lowercase `"true"` (deviation from HDS `"True"`); existing tables with any other value fail closed |
| 7 | Engine licence and footprint (B2) | Proposed ([ADR 2611](adr-2611-fabric-silver-delta-engine.md)) | Treat DeltaLake.Net as Apache-2.0; accept 113–142 MiB of natives per RID, contained by a repository isolation guard |
| 8 | MERGE scale (B3) | Proposed ([ADR 2611](adr-2611-fabric-silver-delta-engine.md)) | Fail-closed ceilings (50,000 target rows, 100 files, 50,000 source resources per table); sustained use is gated on a tested compaction policy |

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [hds-silver-normalization](investigations/hds-silver-normalization.md) | Complete | Row semantics of HDS Silver for Patient/Observation, derived from source, with a cross-check of the viewdefinition-depot's claims |
| [delta-writer-options](investigations/delta-writer-options.md) | Complete | Delta engine options, the B1–B4 blockers and their local evidence; synthesized into ADR 2611 |

## Known Gaps

- No HDS-captured oracle; residual questions that only a real HDS run can settle are listed in section J
  of the normalization investigation.
- Licensing of HDS-derived artifacts is unresolved; the writer avoids redistribution by loading contracts at runtime.
- The Fabric runtime has not been exercised (B4: Spark datetime rebase default). The Delta test suite passed on
  linux-x64 (glibc) in a local container; linux-arm64, macOS and musl natives have not been run, and no CI run
  exists.

## Decision

[ADR 2611: Delta Engine for the Native Fabric Silver Writer](adr-2611-fabric-silver-delta-engine.md) (Proposed).
