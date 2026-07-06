# Investigation: Terminology Completeness

**Feature**: validation
**Status**: Planned (decision locked — local valuesets + severity semantics + remote TX API)
**Created**: 2026-07-06

## Problem Statement

Terminology is the single largest conformance gap on the R4 clean-base slice — it dominates both
error directions:

- **Over-strict (~9 cases):** we emit `IssueSeverity.Error` for codes whose system/valueset we cannot
  resolve locally (unknown SNOMED/LOINC/example codes). The Java reference **warns** ("cannot verify")
  rather than errors. We are wrong to fail these.
- **Under-strict (~16 cases, `tx`/`tx-advanced`):** bindings we cannot verify at all because the
  valueset isn't available locally and there is no terminology service.

Current state:

- `InMemoryTerminologyService` (`src/Core/Ignixa.Validation/Services/`) is **membership-only**;
  `$lookup`/`$expand`/`$translate`/`$subsumes` are stubs.
- `BindingCheck` depth-gates (Spec: required bindings only; Full: + extensible) and calls the async
  service via blocking `.GetAwaiter().GetResult()`.
- `ITerminologyService` already declares the full surface (`ValidateCode`, `ValidateBinding`,
  `Lookup`, `Expand`, `Translate`, `Subsumes`).

## Decision (locked)

Three complementary pieces, in priority order:

1. **Error-vs-warn severity semantics** — never error on a binding we cannot *verify*; only error when
   we can positively determine a code is **not** in a **required** valueset.
2. **Expanded local valuesets** — ship/load more valueset expansions so more bindings resolve locally
   (offline, deterministic — matches the project's determinism goal and the Rust "local-first" model).
3. **Remote terminology server API** — an optional `ITerminologyService` backed by a FHIR TX endpoint,
   used as a fallback for what local data cannot decide.

This mirrors the Rust `rh-validator` terminology model: **local-first, remote as fallback**, cached.

## Plan

### T1 — Severity semantics (immediate; no new dependencies)

The over-strict fix, and a prerequisite for the other two so they don't regress.

Binding outcome must distinguish three states, not two:

| Local knowledge | Required binding | Extensible binding |
|---|---|---|
| Code **verified in** valueset | pass | pass |
| Code **verified not in** valueset | **error** | warning |
| Valueset/system **unresolvable** (can't verify) | **warning** (not error) | info/none |

- Extend the binding path (`BindingCheck` + `InMemoryTerminologyService.ValidateBindingAsync` +
  `BindingValidationResult`) to return `Unverifiable` distinctly from `NotInValueSet`.
- `TerminologyFailureMode` (already on `ValidationSettings`) governs whether `Unverifiable` on a
  required binding is Warning (default) or Error.
- **Guardrail:** `ValidationDepth.Compatibility` keeps current leniency — no new errors at that depth.
- Verify: the ~9 over-strict terminology cases flip to pass; no new over-strict. Add `BindingCheck`
  tests for the three states.

### T2 — Expanded local valuesets

Close under-strict bindings without a network dependency.

- Load valueset **expansions** and **code systems** from the FHIR core package + configured IG
  packages via the existing `IValueSetProvider` / package layer (the code-system data is already
  partly present for required bindings).
- Precedence: IG valuesets override core (the provider already supports layering).
- Cache membership by `(valueSet, system, code)` — this was the single biggest perf win in the Rust
  validator (≈−99%); adopt the same LRU key.
- Handle `compose.include` with enumerated concepts and simple system-wide includes locally; mark
  `filter`-based includes as *unverifiable* (→ T1 warning, or T3 remote), matching Rust's
  "not locally decidable" behavior.
- Verify: `tx` under-strict cases with locally-available valuesets flip to caught; the split moves
  from under-strict toward matched.

### T3 — Remote terminology server API

Fallback for what local data cannot decide (SNOMED/LOINC/large valuesets, `filter` includes).

- New `HttpTerminologyService : ITerminologyService` (`src/Core/Ignixa.Validation/Services/`) calling
  a FHIR TX endpoint: `$validate-code` (CodeSystem + ValueSet), `$expand`, `$lookup`, `$subsumes`,
  `$translate`. `HttpClient` + `CancellationToken` (name it `cancellationToken`), typed Parameters
  in/out.
- `CachedTerminologyService` decorator: in-memory LRU + optional disk persistence (mirror Rust's
  `CachedTerminologyService`), so repeated codes don't re-hit the server.
- **Local-first composition:** a `LayeredTerminologyService` tries local (T2) first, remote only when
  local is `Unverifiable`. Required-binding remote failures surface per `TerminologyFailureMode`;
  remote unavailability degrades to Warning, never a hard error (server outage ≠ invalid resource).
- Configuration: `TerminologyConfig` (endpoint URL, cache dir, on/off) wired through
  `ValidationServicesRegistration`. Off by default — deterministic offline validation stays the
  baseline; remote is opt-in.
- Verify: conformance `tx`/`tx-advanced` cases against a configured `tx.fhir.org/r4` (in a
  network-gated, non-CI test category — the offline suite must not depend on it).

## Sequencing

T1 first (fixes over-strict now, unblocks the rest), then T2 (offline coverage), then T3 (remote
fallback). T1 and T2 move the offline conformance number; T3 is measured separately behind a network
gate so the default suite stays deterministic.

## Guardrails

- **Compatibility depth** semantics unchanged throughout (MS FHIR Server / legacy Firely parity).
- Offline determinism preserved — remote TX is opt-in; the default validator never requires network.
- No `Hl7.Fhir.*` in Core — the TX client speaks FHIR JSON over HTTP directly.

## Verification

- Unit: three-state binding severity (T1); local membership incl. `compose.include` handling (T2);
  HTTP client request/response shaping with a mock handler + cache decorator (T3).
- Conformance: offline `tx` cases via `ValidatorConformanceRunner` (T1/T2); network-gated `tx` +
  `tx-advanced` category (T3).

## References

- [roadmap.md](../roadmap.md) — Phase 3.
- Rust: `C:\Src\rh\crates\rh-validator\src\terminology.rs` (local-first, cached, HTTP fallback).
- Current: `InMemoryTerminologyService`, `BindingCheck`, `ITerminologyService`, `ValidationSettings.TerminologyFailureMode`.
