# Investigation: Differential → Snapshot Generation (own ElementMerger)

**Feature**: validation
**Status**: Planned (decision locked — build our own `ElementMerger`)
**Created**: 2026-07-06

## Problem Statement

Ignixa cannot validate against profiles that ship **differential-only** StructureDefinitions. The
adapter that turns a raw StructureDefinition into the validator's `IType` is snapshot-only:

- `StructureDefinitionTypeAdapter` (`src/Core/Ignixa.PackageManagement/Infrastructure/`) requires
  `snapshot.element`; its own doc-comment says *"Differential resolution is not performed —
  StructureDefinitions without a snapshot return null. (Snapshot generation is a separate concern.)"*
- `ProfileLayeredSchemaProvider` consequently **drops** any *"differential-only definition with no
  snapshot"*.
- Profile composition in `ProfileAwareValidationSchemaResolver.ResolveForElement` concatenates base +
  profile schemas; it never merges a differential onto a base.

Consequence: any IG or custom profile authored without a pre-built snapshot validates as base-spec
only (constraints silently not enforced). The conformance suite's `profile`/package cases — currently
excluded from the clean-base slice — cannot be run at all.

## Decision (locked)

Build our **own `ElementMerger`** (not Firely's `SnapshotGenerator`). Rationale: no `Hl7.Fhir.*`
coupling in the Core/package layers, full control, idiomatic to the existing snapshot-element model,
and directly informed by the Rust `rh-foundation` merger we studied. Firely's generator remains
available in the **test layer only** as a differential oracle to grade our output against.

## Key Insight — the seam is already isolated

`StructureDefinitionTypeAdapter` already consumes a flat `snapshot.element` list. We do **not** need to
touch the schema builder, the checks, or the resolver. We insert a generation step *upstream* of the
adapter: given a StructureDefinition with only `differential`, produce a `snapshot.element` list, then
hand it to the existing adapter. Everything downstream is unchanged.

```
raw SD (differential + baseDefinition)
      │
      ▼  NEW: SnapshotGenerator.Generate(sd)  ──resolves base, merges differential──►  snapshot.element
      │
      ▼  StructureDefinitionTypeAdapter.Adapt(snapshot)  (unchanged)
      │
      ▼  IType → StructureDefinitionSchemaBuilder → ValidationSchema  (unchanged)
```

## Reference design (Rust `rh-foundation`)

`C:\Src\rh\crates\rh-foundation\src\snapshot\{generator,merger}.rs`:

- `generate_snapshot_internal`: if the SD already carries a `snapshot`, use it as-is; else resolve
  `baseDefinition` recursively to a snapshot and call `ElementMerger::merge_elements(base, diff)`.
- Circular-dependency detection via a `visited` set.
- `merger.rs`: matches differential elements to base by path/id, overrides constrained facets,
  inserts new elements (extensions, slices).

## Plan

### Component

New: `src/Core/Ignixa.PackageManagement/Infrastructure/Snapshot/SnapshotGenerator.cs` +
`ElementMerger.cs` (one type per file). Operates on `System.Text.Json` `JsonElement`/`JsonNode` (same
representation the adapter already consumes) — no new object model, no Firely types.

Public surface:
```
JsonNode? GenerateSnapshotElements(JsonElement structureDefinition);  // returns snapshot.element array, or null
```

Base resolution uses the existing `IPackageResourceProvider` / embedded core definitions to fetch the
`baseDefinition` SD; core R4 types resolve to their shipped snapshots.

### Milestones (each measured by the conformance runner)

**M1 — Base merge, constraint tightening (no slicing).** Resolve `baseDefinition` → base snapshot;
walk it; for each `differential.element` matched by `path`, override the constrainable facets:
`min`, `max`, `type`, `binding`, `fixed[x]`, `pattern[x]`, `short`, `definition`, `mustSupport`,
`constraint`. Preserve base elements not in the differential. Recurse `baseDefinition` chain with
cycle detection. This alone handles the bulk of US Core / AU Core constraint profiles.

**M2 — Slicing + extension element insertion.** Insert differential elements that don't exist in the
base: named slices (`elementId` with `:sliceName`), sliced extensions (`extension:foo`), and the
`slicing` discriminator metadata on the sliced element. Feeds the in-progress
[slicing-discriminators](slicing-discriminators.md) work (which needs the sliced elements present in
the snapshot to enforce per-slice cardinality).

**M3 — Type expansion + edge cases.** `contentReference`, expanding a complex datatype's children
when a profile constrains into it, choice-type `[x]` narrowing, re-slicing, and `baseDefinition`
pointing at another profile (profile-on-profile). Cycle/`visited` guard throughout.

### Wiring

1. `ProfileLayeredSchemaProvider` / `StructureDefinitionTypeAdapter` entry: when `snapshot` is absent
   but `differential` + `baseDefinition` are present, call `SnapshotGenerator.GenerateSnapshotElements`
   and adapt the result. When a snapshot is already present, use it as-is (parity with Rust; no
   regeneration).
2. Cache generated snapshots (keyed by canonical URL, version-stripped) — the resolver layer already
   caches schemas via `CachedValidationSchemaResolver`; add snapshot-level caching in the provider to
   avoid re-merging on every resolve.

### Guardrails

- **Compatibility depth** unchanged — snapshot generation only affects *which* elements exist; the
  depth gating in `ValidationSchema.Validate` is untouched.
- Generated snapshots must be **byte-comparable in intent** to package-shipped snapshots. Firely's
  `SnapshotGenerator` (test-only) is the oracle: for profiles that ship *both* differential and
  snapshot, generate from the differential and diff our element list against the shipped snapshot
  (and against Firely's regeneration) — any divergence is a merger bug.

## Verification

- **Unit**: per-facet merge tests (min tighten 0→1, type restriction, fixed/pattern injection, slice
  insertion), cycle detection, base-on-base recursion. AAA + Shouldly.
- **Oracle diff**: for US Core / AU Core / CARIN profiles that ship snapshots, assert our generated
  `snapshot.element` matches the shipped one (path set, cardinalities, bindings).
- **Conformance**: enable the currently-deferred `profile`/package slice in `ValidatorConformanceRunner`
  (extend `ConformanceCaseLoader` to load `packages`/`supporting`/`profile` cases). This is the real
  scoreboard — Phase 2's payoff shows up here, not on clean-base. Track over-strict/under-strict on the
  new slice.

## References

- [roadmap.md](../roadmap.md) — Phase 2.
- [slicing-discriminators.md](slicing-discriminators.md) — consumer of M2 (needs sliced elements).
- Rust: `C:\Src\rh\crates\rh-foundation\src\snapshot\{generator,merger}.rs`.
- FHIR spec: [Snapshot Generation](https://hl7.org/fhir/R4/profiling.html#snapshot).
