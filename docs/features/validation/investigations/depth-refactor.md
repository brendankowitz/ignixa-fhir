# Investigation: Validation Depth Refactor

**Feature**: validation
**Status**: In Progress
**Created**: 2025-12-18
**Original ADR**: N/A

## Goal
Replace the dual-validation controls (`ValidationTier`, `ValidationMode`) with one unified `ValidationDepth` enum (Minimal, Spec, Full) that drives both structural and terminology validation consistently across tenant config, Prefer parsing, commands, handlers, settings, and tests.

## Target Semantics
- `ValidationDepth.Minimal`: structure-only (basic shape). No invariants/slicing, no terminology.
- `ValidationDepth.Spec`: structure (spec-level: cardinality, types, FHIRPath invariants as configured) + terminology required bindings only.
- `ValidationDepth.Full`: structure (full: invariants/slicing) + terminology required + extensible + display checks.
- Tenant default sets the baseline; Prefer header overrides.

## Steps
1) **Introduce enum**
   - Add `Ignixa.Domain.Models.ValidationDepth { Minimal, Spec, Full }`.
   - Remove/alias `ValidationTier` and `ValidationMode` (delete or mark obsolete with mapping to ValidationDepth).

2) **Config & DTOs**
   - Update tenant config model `TenantConfiguration.ValidationTier` → `ValidationDepth` (string binding accepts Minimal/Spec/Full).
   - Update any DTOs/Tools (e.g., `ListTenantsInfoTool`, `TenantInfoDto`) to surface `ValidationDepth`.

3) **Prefer header parsing**
   - Update `PreferHeaderParser` to parse `validation=none|minimal` → Minimal, `validation=spec` → Spec, `validation=full` → Full. Drop old aliases or map them.
   - Update `ToPreferenceAppliedHeader` to emit the new values.
   - Adjust OperationEndpoints parsing to return `ValidationDepth` instead of `ValidationMode`.

4) **Commands/handlers/pipeline**
   - `ValidateResourceCommand`: replace `ValidationMode` with `ValidationDepth`.
   - `ValidationSettings`: replace `Tier`/`ValidationMode` with `Depth` only.
   - `ValidationBehavior` and `ValidateResourceHandler`: use `ValidationDepth` for structural choices and to decide if/what terminology to run (Minimal = none, Spec = required-only, Full = required+extensible+display).
   - Ensure schema resolver loads the right structural checks for Spec/Full (invariants/slicing) and Minimal skips them.
   - Binder/behavior: Prefer override vs tenant default both use `ValidationDepth`.

5) **Checks**
   - `BindingCheck`: consume `ValidationDepth` (Minimal skip; Spec = required only; Full = required+extensible+display). Ensure cancellation tokens flow through to `ValidateBindingAsync`.
   - Any other checks keyed off `ValidationTier` adjust to `ValidationDepth`.

6) **Hybrid/terminology services**
   - Keep as-is; just ensure callers pass the new enum and propagate cancellation tokens.

7) **Tests**
   - Update all tests referencing `ValidationTier`/`ValidationMode` to `ValidationDepth` and expected behaviors. This spans:
     - `test/Ignixa.Api.Tests` (Prefer parser, endpoints)
     - `test/Ignixa.Application.Tests` (ValidateResourceHandler)
     - `test/Ignixa.Validation.Tests` (binding/invariant/structure checks)
     - Any other validation-related suites.

8) **Docs**
   - Update any docs mentioning tier/mode to the new naming (`validation=spec|full|minimal`).

9) **Cleanup**
   - Remove dead enums/classes (`ValidationMode`, `ValidationTier`) once all references are migrated.
   - Run `dotnet build` and `dotnet test` across affected projects.

## Notes/Risks
- This is a breaking change; be thorough in grepping for both enums.
- Prefer header contract changes: clients should use `validation=spec|full|minimal`. Consider keeping lenient parsing for old values temporarily if needed.
- Structural vs terminology: Spec/Full both use richer structure; terminology depth is tied to the same enum (no separate override unless explicitly reintroduced).
