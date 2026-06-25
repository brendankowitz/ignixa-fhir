# Investigation: Slicing & Discriminator Validation

**Feature**: validation
**Status**: In Progress
**Created**: 2026-06-17

## Problem Statement

`SlicingMetadata` is captured from every `ElementDefinition.slicing` during code-gen
(`src/Core/Ignixa.Abstractions/Structure/SlicingMetadata.cs`) but the comment in that file is
explicit: *"Slicing support is not yet implemented but metadata is captured for future use."*
`StructureDefinitionSchemaBuilder` builds cardinality, type, shape, invariant, and binding checks
for every tier (Minimal / Spec / Full — `ValidationDepth.cs`) but produces no `SlicingCheck` in
`profileChecks` (`StructureDefinitionSchemaBuilder.cs:301-318`). The `ValidationSchema` doc-comment
at line 29 acknowledges this: *"Profile checks (Full depth) — Slicing, advanced terminology, etc."*

The practical consequence:

- `Extension.extension` is sliced by `url` in every R4 StructureDefinition (the canonical example in
  `reference-implementations.md`). Without slice assignment, ignixa cannot enforce that two
  extensions with the same URL do not exceed their per-slice `max`.
- Profile slicing (e.g., `us-core-patient.name` sliced into `official` / `nickname`) is completely
  invisible to the validator.
- Closed/`openAtEnd` slice rules — which must reject unmatched elements — are never applied.

## Key Insight

Slicing is not backward navigation. It is a **parent-altitude check**.

The element *owning* the sliced array — e.g., the `Patient` node owning `name[*]` — already has
all candidates available through `Children("name")`. The forward-only `IElement` model handles this
natively: there is no need for a parent pointer, a `ScopedNode` wrapper, or any upward traversal.
This is the same conclusion the [tree-context-scoping investigation](tree-context-scoping.md) reached
when it classified slicing as *"not backward navigation at all … a parent-altitude check over
`Children()`, which the forward-only model already supports"* (tree-context-scoping.md, Key Insight
section).

What slicing actually requires is a two-responsibility split:

1. **Imperative shell** — slice *assignment*, per-slice cardinality accounting, closed/open rule
   enforcement, and error reporting. This must be imperative code so diagnostics can say *"you have
   2 elements matching slice 'official', expected at most 1, at Patient.name[3]"* rather than a
   single boolean assertion.
2. **FHIRPath predicate engine** — evaluate the per-element discriminator condition. Only this piece
   delegates to the existing FhirPath engine. The discriminator `path` is itself restricted
   FHIRPath: simple property navigation plus `resolve()`, `extension(url)`, and `ofType()`.

This is the accurate reading of the phrase "HAPI compiles checks to FHIRPath" (confirmed in the
[reference-implementations](reference-implementations.md) SliceValidator section): the FHIRPath
engine evaluates the *discriminator predicate per candidate element*, not the whole-profile check.
Firely's `SliceValidator` (`reference-implementations.md:458-506`) does exactly this — `Condition`
per `SliceCase` is evaluated via `ValidateOne`, assignment is tracked imperatively in `buckets`,
then per-slice cardinality is enforced over each bucket.

### Discriminator Type Mapping

The FHIR spec defines five discriminator types. Each maps cleanly to existing engine primitives:

| Discriminator `type` | Discriminator `path` evaluates to | Engine primitive | Status |
|---|---|---|---|
| `value` | concrete scalar/code | FHIRPath equality: `path = 'literal'` | Fully implemented |
| `pattern` | element matching a Pattern | FHIRPath `~` (equivalence) or `subsetOf()` | Implemented (`subsetOf`, `supersetOf` in `CollectionFunctions.cs`) |
| `exists` | presence/absence | `path.exists()` | Implemented (`exists()` in `CollectionFunctions.cs`) |
| `type` | FHIR type of the element | `ofType(T)` / `is T` | Implemented (`ofType` in `CollectionFunctions.cs`; `is`/`as` in `TypeConversionFunctions.cs`) |
| `profile` | element conforms to a profile canonical | `conformsTo('url')` | **Stub** — throws `NotSupportedException` (`FhirSpecificFunctions.cs:404-411`); requires tree-context-scoping Solution 1 first |

The most common discriminators in base R4 profiles (`value` on `url` for extensions, `value` on
`system`/`code` for identifiers) require only equality — already working. `type` discriminators
require `ofType` — already working. `profile` discriminators are the only case blocked on a
prerequisite (see Dependency section below).

## Approach

A `SlicingCheck : IValidationCheck` at the **Full tier** (`ValidationDepth.Full`), registered in
`_profileChecks` of `ValidationSchema`.

The check owns the sliced element path (e.g., `"name"`), the `SlicingMetadata` captured by
code-gen, and a per-slice list of `(discriminatorPredicate: string, min: int, max: int?)` compiled
at schema-build time.

Pseudocode of the core algorithm:

```
SlicingCheck.Validate(element, depth, state):
    if depth < Full → return success  // guard: slicing is Full-tier only

    candidates = element.Children(slicedPath)   // O(n) — one forward pass
    if candidates.IsEmpty → check per-slice minimum cardinality for mandatory slices; return

    // Assignment pass — O(slices × candidates) but slices are typically 2-5
    buckets = Dictionary<sliceName, List<(IElement, int index)>>
    defaultBucket = List<(IElement, int index)>
    lastMatchedSliceIndex = -1

    for (i, candidate) in candidates.WithIndex():
        matched = false
        for (s, slice) in slices.WithIndex():
            predicateResult = fhirPathEngine.Evaluate(candidate, slice.DiscriminatorExpression, context)
            if predicateResult.IsTrue():
                if metadata.Ordered && s < lastMatchedSliceIndex:
                    report Issue(OUT_OF_ORDER, "name[{i}] matches slice '{slice.Name}' out of order")
                buckets[slice.Name].Add((candidate, i))
                lastMatchedSliceIndex = s
                matched = true
                break  // first match wins; FHIR slicing is deterministic

        if !matched:
            if metadata.Rules == "closed":
                report Issue(UNMATCHED_SLICE, "name[{i}] matches no slice in a closed slicing")
            elif metadata.Rules == "openAtEnd" && lastMatchedSliceIndex >= 0:
                report Issue(UNMATCHED_SLICE, "name[{i}] appears before end in an openAtEnd slicing")
            else:
                defaultBucket.Add((candidate, i))

    // Cardinality pass — O(slices)
    for slice in slices:
        count = buckets[slice.Name].Count
        if count < slice.Min:
            report Issue(CARDINALITY_TOO_FEW, "slice '{slice.Name}': {count} < min {slice.Min}")
        if slice.Max != null && count > slice.Max:
            report Issue(CARDINALITY_TOO_MANY, "slice '{slice.Name}': {count} > max {slice.Max}")
            for (elem, i) in buckets[slice.Name][slice.Max..]:
                annotate Issue with "at name[{i}]"  // precise per-element location
```

`StructureDefinitionSchemaBuilder.BuildSchema` plugs this in where slicing metadata is present on
an element. The builder already has access to `ITypeExtended`, which carries `SlicingMetadata` via
code-gen.

The `FhirEvaluationContext` passed to the discriminator evaluations must be built from
`ValidationState.Scope` (the resource-scope populated by Solution 1 of the
tree-context-scoping investigation) so that `%resource`, `resolve()`, and — once unblocked —
`conformsTo()` are available.

## Dependency on Tree-Context Scoping

This investigation builds directly on [tree-context-scoping](tree-context-scoping.md) Solutions 1
and 2:

- **Solution 1 (resource scope threading)** is required for `profile` and `type` discriminators
  whose `conformsTo()` predicate must be evaluated against the correctly-scoped `%resource`. Without
  Solution 1, `conformsTo` would evaluate against an unscoped element (wrong), even after its
  stub is replaced with a real implementation.
- **Solution 2 (scoped reference index)** is required for reference discriminators that navigate
  through `resolve()` to reach the target element. `resolve()` already returns empty when no
  resolver is configured (`FhirSpecificFunctions.cs:94-96`), so without Solution 2, any
  discriminator path containing `resolve()` silently produces no match and assigns all candidates
  to the default bucket.

For `value` and `exists` discriminators (the vast majority of base R4 usage), neither tree-context
dependency is needed — `SlicingCheck` can be delivered as a partial implementation before
tree-context-scoping lands, with `profile`-type discriminator support gated behind a capability
flag.

## Tradeoffs

| Pros | Cons |
|------|------|
| Fits the existing `IValidationCheck` / `_profileChecks` slot without any new abstractions | O(slices × candidates) assignment pass; for rare deeply-nested slicings with many slices this is not O(n) |
| Precise per-element diagnostics: reports `name[3]` not just "slicing failed" | Discriminator predicate evaluation re-walks the candidate for each slice until a match is found; early-exit on first match mitigates this significantly |
| Forward-only `IElement` model is sufficient — no `ScopedNode`, no parent pointer | Nested slicing (reslicing within a slice) requires a recursive `SlicingCheck` per sub-slice; compiler complexity grows with nesting depth |
| Delegate-injection pattern for `conformsTo` is already established (`ElementResolver` on `FhirEvaluationContext`) — clean seam, no new mechanism | `profile` discriminators blocked until tree-context-scoping Solution 1 lands and `conformsTo` stub is replaced |
| `SlicingMetadata` already captures discriminators, rules, and ordered flag — code-gen work already done | A single-pass assignment (produce O(1) FHIRPath expression collapsing all predicates) would be faster but loses per-element location in error messages |
| Aligns with Firely `SliceValidator` (bucket-based, condition per slice case) and HAPI's imperative assignment; no novel pattern to justify | `conformsTo()` injection adds a FHIRPath→Validation callback that must be wired carefully to avoid re-entrant validation cycles (same risk as the `ElementResolver` cycle, which is already managed) |
| Closed/openAtEnd enforcement is straightforward boolean state on the assignment loop | `ordered` slicing requires tracking `lastMatchedSliceIndex`, which means unordered slicings cannot short-circuit early within a candidate's slice loop — minor |

## Alignment

- [x] Follows architectural layering rules — `SlicingCheck` lives in `Ignixa.Validation.Checks`,
      consumes `IElement` (forward-only), delegates discriminator evaluation to `Ignixa.FhirPath`
      via the context boundary; no `Hl7.Fhir.*` dependency introduced
- [x] Developer Experience — plugs into the existing `_profileChecks` list; schema-builder already
      has the slicing metadata; consumers see the same `IValidationIssue` shape as today
- [x] Specification compliance — implements FHIR R4 `ElementDefinition.slicing` rules: ordered,
      closed/open/openAtEnd, per-slice min/max cardinality, five discriminator types
- [x] Consistent with existing patterns — `IValidationCheck`, `ValidationDepth.Full` gate,
      `FhirEvaluationContext` from `ValidationState.Scope`, delegate-injection for `conformsTo`
      mirroring the `ElementResolver` seam

## Weakest-Link Analysis

1. **`conformsTo()` re-entrancy.** Implementing `conformsTo` as a `Func<IElement, string, bool>`
   delegate on `FhirEvaluationContext` (mirroring `ElementResolver`) means the FHIRPath engine
   calls back into the validation pipeline for each `profile` discriminator. If a discriminator
   predicate triggers further slicing which triggers further `conformsTo` calls, a cycle is
   possible. Mitigation: the same per-resource cycle guard already used by
   `ContainedResourceCheck` can be reused; `conformsTo` should not itself run Full-depth
   validation (a Spec-depth structural check is sufficient for slice assignment).

2. **Silent no-match on missing resolver.** A `profile` discriminator before tree-context-scoping
   lands will cause `conformsTo` to throw `NotSupportedException`
   (`FhirSpecificFunctions.cs:410`). The check must catch this and either skip the discriminator
   (treating assignment as indeterminate — unsafe for closed slicings) or report a warning and
   skip. The safer choice is to gate `profile`-discriminator slices with an explicit capability
   flag rather than silently assigning all candidates to the default bucket.

3. **`SlicingMetadata.Discriminators` is `string[]` without type annotation.** The current model
   stores raw strings (`src/Core/Ignixa.Abstractions/Structure/SlicingMetadata.cs:22`) with no
   separate `type` field — the discriminator type (value/pattern/exists/type/profile) is not
   captured. Code-gen must be extended to emit a structured discriminator record (type + path)
   before `SlicingCheck` can construct the correct FHIRPath predicate per discriminator. This is a
   prerequisite schema-builder change, not a runtime change.

4. **O(slices × candidates) for large arrays.** Patient bundles with hundreds of entries each
   containing many sliced identifiers could make this quadratic in practice. Mitigation: a
   single-pass trie-like pre-index over the discriminator values (for pure `value` discriminators)
   can reduce this to O(candidates + slices); implement as an optimization after correctness is
   established.

## Evidence

- **Metadata captured, logic absent.** `SlicingMetadata` constructor confirms discriminators,
  rules, and ordered are all captured (`SlicingMetadata.cs:14-18`). The file-level comment *"not
  yet implemented"* is unambiguous.
- **Correct plug-in point.** `ValidationSchema._profileChecks` is explicitly documented as
  *"Slicing, advanced terminology, etc."* (`ValidationSchema.cs:29`). `StructureDefinitionSchemaBuilder`
  builds `profileChecks` (line 301) and currently populates it only with `invariantChecks` (line 308).
  Adding `SlicingCheck` here follows the exact same pattern.
- **FHIRPath primitives are ready.** `ofType` exists in `CollectionFunctions.cs:575`; `exists` at
  line 27; `subsetOf`/`supersetOf` for pattern matching present. `is`/`as` type-testing operators
  are in `TypeConversionFunctions.cs`. The only missing primitive is `conformsTo`
  (`FhirSpecificFunctions.cs:397-411`), which throws `NotSupportedException` and is explicitly
  flagged as needing *"profile validation infrastructure"*.
- **Reference prior art.** Firely's `SliceValidator` in `reference-implementations.md:458-506`
  uses bucket-based assignment with a `Condition.ValidateOne(candidate, vc, state)` discriminator
  call — directly analogous to the proposed approach. HAPI's StructureDefinition XML shows the
  canonical `Extension.extension` sliced-by-`url` example (`reference-implementations.md:579-598`),
  confirming `value` discriminator on `url` is the single most important case to get right.
- **GraphQL directive is not reusable.** `FhirSliceDirectiveType.cs` declares a `@slice(path:)`
  GraphQL directive for HotChocolate query slicing — a completely different concept
  (splitting a list result in a query response). It contains no slice-matching logic and shares
  only the name.
- **`ValidationDepth.Full` is the correct tier.** `ValidationDepth.cs:22` defines Full as
  *"structure + required and extensible bindings, display checks, invariants/slicing"* — the
  comment already names slicing explicitly.
- **ADR-2510** (`docs/adr/adr-2510-validation-architecture.md`) established the three-tier schema
  architecture that `SlicingCheck` slots into without revision.

## Open Questions

1. **`SlicingMetadata` schema gap.** The current `Discriminators: string[]` captures the
   discriminator `path` but not the `type` (value/pattern/exists/type/profile). Should code-gen
   emit a `DiscriminatorDefinition(string Type, string Path)` record, or should `SlicingCheck`
   parse the type from a convention in the path string? The former is cleaner; it requires a
   code-gen change and a new type in `Ignixa.Abstractions`.

2. **Partial delivery.** Is it acceptable to ship `SlicingCheck` with `value`, `exists`, and `type`
   discriminators working (no tree-context dependency) and `profile`/`resolve()` discriminators
   gated behind a capability flag, before tree-context-scoping lands? Or should slicing wait for
   the full foundation?

3. **Nested / resliced slices.** When a slice itself contains a further sliced element (reslicing),
   `SlicingCheck` must be applied recursively. Is the correct approach to emit a nested
   `SlicingCheck` inside a `NestedComplexTypeCheck`, or to flatten the slice hierarchy at
   schema-build time?

4. **`conformsTo` cycle guard depth.** The re-entrant validation triggered by `profile`
   discriminators should run at what depth — Minimal or Spec? Spec is sufficient to establish
   profile conformance for slice assignment and avoids triggering further Full-depth slicing checks
   recursively.

## Verdict

*Pending evaluation.* The hybrid approach — imperative assignment + FHIRPath discriminator
predicate — is the clearly right direction: it matches Firely's `SliceValidator`, HAPI's design,
and fits the existing `IValidationCheck` / `_profileChecks` slot without new abstractions.

The strongest constraint on sequencing is the `SlicingMetadata` schema gap (Open Question 1): the
current `string[]` for discriminators cannot distinguish discriminator `type` from `path`, so code-gen
must emit a structured record before `SlicingCheck` can correctly choose between `value =`,
`ofType()`, and `conformsTo()` predicates.

Recommended sequencing once that gap is resolved:
1. Extend code-gen to emit `DiscriminatorDefinition(Type, Path)` in `SlicingMetadata`.
2. Implement `SlicingCheck` with `value`, `exists`, and `type` discriminators (no tree-context
   dependency). Gate `profile`-discriminator paths with a capability flag that degrades to
   open-slicing semantics.
3. Land [tree-context-scoping](tree-context-scoping.md) Solutions 1 and 2.
4. Implement `conformsTo()` injection via `FhirEvaluationContext` delegate; wire into
   `SlicingCheck` for `profile` discriminators; remove the capability flag.
