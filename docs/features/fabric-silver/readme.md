# Feature: Fabric Silver Writer

**Status**: Exploring
**Created**: 2026-11-24

## Problem Statement

Issue #550 asks for a native (non-Spark) writer that produces Fabric-compatible Silver Delta tables for
FHIR R4 `Patient` and `Observation`, matching the table layout and row semantics of the Healthcare data
solutions (HDS) Silver layer. Scope is Phase 1 (native writer), then a local Phase 2; it does not expand
beyond these two resource types.

## Constraints

- Must reproduce the Silver column layout, `id`/`idOrig`, identifier/reference normalization, `*Orig`
  columns, `_string` companions, lineage columns and upsert semantics documented in the investigation.
- Must not copy HDS code, schemas or depot contracts into this repository.
- No HDS-captured row oracle exists. Only **schema parity** (names, order, types, nullability, protocol,
  table properties) is checkable today; a schema match is **not** row parity.

## Decisions Status

| # | Decision | Status | Current direction |
|---|----------|--------|-------------------|
| 1 | Native column derivation | Proposed | Option B: bypass ViewDefinitions and project columns from the element tree using `ISchema` and the target Delta schema |
| 2 | Delta library | Exploring | Spike DeltaLake.Net; fallback is a minimal managed log writer over Parquet.Net |
| 3 | Batch atomicity | Proposed | Not atomic across tables; validate and stage all files first, replay repairs partial batches |
| 4 | V-Order | Proposed | Set the table property; do not claim file-layout parity |
| 5 | Provenance and licensing | Open | HDS-derived material is not redistributable until cleared |

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [hds-silver-normalization](investigations/hds-silver-normalization.md) | Complete | Row semantics of HDS Silver for Patient/Observation, derived from source, with a cross-check of the depot session's claims |
| [delta-writer-options](investigations/delta-writer-options.md) | In Progress | Delta writer library options (written separately) |

## Known Gaps

- No HDS-captured oracle; residual questions that only a real HDS run can settle are listed in section J
  of the normalization investigation.
- Licensing of HDS-derived artifacts is unresolved.

## Decision

*No ADR yet - investigations in progress*
