# Investigation: Tree-Context Scoping (%resource, %rootResource, resolve())

**Feature**: validation
**Status**: Viable
**Created**: 2026-06-17

## Problem Statement

ignixa's runtime element model (`IElement`) is **forward-only**: it exposes `Children()` and
nothing else. There is no `Parent`, no `Ancestors()`, no enclosing-resource pointer. This is a
deliberate contrast with the Firely SDK, whose `ScopedNode` wraps every `ITypedElement` with
`Parent` / `ParentResource` pointers and lazily computes `%resource`, `resolve()`, and `Location`
by walking *up* the tree (`firely-net-sdk/src/Hl7.Fhir.Base/ElementModel/ScopedNode.cs`).

Three FHIR validation behaviours appear to need that upward/sideways navigation:

1. **`%resource` / `%rootResource` in invariants** — e.g. `dom-1`, `bdl-*`. A constraint evaluated
   deep inside a resource needs `%resource` to point back at the enclosing resource.
2. **`resolve()` across Bundle entries and contained resources** — a `Reference` must find a
   *sibling* `entry.resource` (by `fullUrl`) or a `#id` contained resource.
3. **Slicing discriminators** — partition a sibling array into slices.

Today the gap is real and silent. `FhirPathInvariantCheck.Validate` calls
`_evaluator.Evaluate(element, expression)` with **no context**
(`src/Core/Ignixa.Validation/Checks/FhirPathInvariantCheck.cs:168`), so `%resource` auto-inits to
*the constrained element itself* (`TypedElementExtensions.Select` defaults `Resource`/`RootResource`
to the input). For a nested element `%resource` points at the wrong node, and `resolve()` returns
empty because no resolver is wired. The existing test suite acknowledges this:
*"Real dom-1 requires %resource variable ... not yet implemented."*

## Key Insight: FHIRPath has no `parent()` operator

The FHIRPath spec exposes exactly two "backward" capabilities — root-ward variables
(`%resource`/`%rootResource`) and reference resolution (`resolve()`). There is **no** standard way
for an expression to navigate to an arbitrary parent element. Firely's `ScopedNode.Parent` is
internal plumbing used to *compute* those two things plus `Location`; it is never surfaced as a
FHIRPath navigation step.

Therefore ignixa does **not** need a parent pointer on the node. Both backward needs are computable
**on the way down**: the traversal that descends the tree *is* the ancestor chain, so the walker's
call stack already holds everything a `Parent` field would store. Storing parent pointers on the
node would duplicate state the traversal already has — the wrong tool for this codebase.

Slicing is not backward navigation at all (see *Related future work*): it is a parent-altitude check
over `Children()`, which the forward-only model already supports.

## Approach

Two complementary changes, both staying on the current "dumb node + injected context" pathway. No
change to `IElement`.

### Solution 1 — Thread resource scope through `ValidationState`

`ValidationState` is already an immutable record threaded through the validation pipeline with three
levels (Global / Instance / Location) and a `with`-based fluent API
(`src/Core/Ignixa.Validation/ValidationState.cs`). Add a fourth concern — the resource scope — and
fork it at resource boundaries during descent.

```csharp
public record ValidationState
{
    public ResourceScope Scope { get; init; } = new();

    // A standalone resource, or an independent Bundle entry: %resource == %rootResource == itself.
    public ValidationState EnterRootResource(IElement resource) => this with
    {
        Scope = new ResourceScope
        {
            Resource     = resource,
            RootResource = resource,
            Resolver     = BuildResolver(resource, parent: null),  // see Solution 2
        }
    };

    // A contained resource C inside parent P: %resource = C, %rootResource = P (the container).
    public ValidationState EnterContainedResource(IElement contained) => this with
    {
        Scope = Scope with
        {
            Resource     = contained,
            RootResource = Scope.Resource,                          // the containing resource
            Resolver     = BuildResolver(contained, parent: Scope), // contained #ids chain to parent/bundle
        }
    };
}

public record ResourceScope
{
    public IElement? Resource { get; init; }       // %resource     = nearest containing resource
    public IElement? RootResource { get; init; }   // %rootResource = container resource (parent), else == Resource
    public Func<string, IElement?>? Resolver { get; init; }
}
```

The fix at the consumer — `FhirPathInvariantCheck.Validate` stops evaluating context-free:

```csharp
var context = new FhirEvaluationContext
{
    Resource       = state.Scope.Resource,
    RootResource   = state.Scope.RootResource,
    ElementResolver = state.Scope.Resolver,
};
var result = _evaluator.Value.Evaluate(element, expression, context);
```

**`%resource` / `%rootResource` semantics (FHIR rule, encoded above):**
- Standalone resource, or an independent **Bundle entry**: `%resource == %rootResource ==` that resource.
  A Bundle entry's resource is *not* contained in the Bundle in the FHIRPath sense, so neither variable
  points at the Bundle.
- **Contained** resource C inside parent P: `%resource = C`, `%rootResource = P` (the containing resource).
- The Bundle's own constraints (`bdl-*`): `%resource == %rootResource ==` the Bundle.

Firely reaches the same result by scanning up `ParentResource` and stopping before a Bundle
(`ScopedNode.ResourceContext` — "do not go past a root resource into a bundle"); we encode it directly
at the two seed points instead of scanning.

### Solution 2 — Scoped reference index injected as the `ElementResolver`

`resolve()` is already a delegated `Func<string, IElement?>` on `FhirEvaluationContext`
(`FhirEvaluationContext.cs:35`); the search indexer already wires one
(`ElementSearchIndexer.cs:65`). The gap is only that validation never builds one with knowledge of
the enclosing Bundle/contained set. Build that index — Firely's `ReferencedResourceCache`
equivalent — by the walker, and inject the closure. Do **not** attach it to the node.

```csharp
sealed class ReferenceIndex
{
    readonly Dictionary<string, IElement> _byFullUrl;     // urn:uuid:.., Type/id, Type/id/_history/v
    readonly Dictionary<string, IElement> _byContainedId; // "#p1"
    public IElement? Resolve(string r) =>
        r.StartsWith('#') ? _byContainedId.GetValueOrDefault(r[1..])
                          : _byFullUrl.GetValueOrDefault(r);
}
```

- Built **lazily and cached in `ValidationState.Global.Cache`**, keyed on the owner's
  `Meta<JsonNode>()` identity — O(entries) once per Bundle, not once per reference.
- The injected resolver chains **contained-of-current-`%resource` → bundle-of-root**, the correct
  FHIR resolution order.
- **Reference integrity** becomes a small new `IValidationCheck`: resolve each `Reference` through
  the index, report unresolved (respecting external/logical references that legitimately do not
  resolve).

## Tradeoffs

| Pros | Cons |
|------|------|
| No change to `IElement`; stays on the forward-only model | Each site where a resource becomes a validation root must seed scope (only 2 today) |
| Reuses existing primitives: `ValidationState` threading + `ElementResolver` delegate | Future nested-root sites (bundle-entry validation) must remember to seed |
| Functional-core / imperative-shell aligned: node stays pure, shell supplies context | `%resource`/`%rootResource` contained-vs-standalone rule must be encoded correctly |
| Unblocks `dom-1`, `bdl-*`, and all `%resource`-referencing invariants | Reference index adds O(entries) build cost per Bundle (mitigated by caching) |
| Cheaper than Firely on the hot path — no per-child `ScopedNode` allocation | Does not by itself solve evaluation of *context-free* deep nodes handed in via a public API (see Open Question) |
| Same delegate-injection pattern already proven by `resolve()` breaking the FhirPath→storage cycle | |

## Alignment

- [x] Follows architectural layering rules — `Ignixa.FhirPath` stays dependency-free; validation
      injects context at the boundary (same inversion as `ElementResolver`)
- [x] Developer Experience — no new public surface; checks read scope from the state they already receive
- [x] Specification compliance — implements FHIRPath `%resource`/`%rootResource` and `resolve()`
      semantics including the Bundle-boundary rule
- [x] Consistent with existing patterns — immutable `with`-based state, injected delegates, lazy caches

## Weakest-Link Analysis

1. **Scope must be seeded at every nested-root site, and only there.** There is no per-element
   descent in the engine — `ValidationSchema.Validate` runs all checks against one root `element`,
   so scope changes *only* when a resource becomes a new validation root (handler entry +
   `ContainedResourceCheck` today; bundle-entry validation in future). Two failure modes:
   (a) a new nested-root site forgets to call `Enter*Resource` → `%resource` stays stale/null
   **silently**; (b) if element-level invariants are later wired (evaluating a constraint against a
   sub-element like `Patient.contact`), a developer might "helpfully" re-point `%resource` per
   element — wrong: `%resource` must stay the enclosing *resource*. *Mitigation:* expose seeding only
   as `EnterRootResource` / `EnterContainedResource` on `ValidationState` (no per-element variant),
   so the invariant "scope forks at resource boundaries, nowhere else" is structurally enforced.
2. **Silent `resolve()`.** Today `resolve()` returns empty both when *no resolver is configured*
   (a wiring bug) and when *a reference legitimately does not resolve* (correct per spec)
   (`FhirSpecificFunctions.cs:94`). In the validation path the resolver should be non-null, so a
   missing resolver surfaces instead of vanishing. Only genuine misses return empty.
3. **Index cost.** Eager per-reference rebuild would be O(refs × entries). Lazy build + cache keyed
   on Bundle identity makes it O(entries) once.

## Evidence

- **Forward-only node, by design.** `IElement` has no parent
  (`src/Core/Ignixa.Abstractions/Structure/IElement.cs`). The Firely adapter
  (`Ignixa.Extensions.FirelySdk6/TypedElementAdapter.cs`) deliberately does not expose `Parent`. A
  parent link *does* exist at the substrate (`System.Text.Json` `JsonNode.Parent`) and is used as an
  escape hatch for mutation only (`Ignixa.DeId/Extensions/ElementMutationExtensions.cs`).
- **Context is already the carrier.** `EvaluationContext.Resource` / `RootResource` and
  `GetEnvironmentVariable` resolve `%resource`/`%rootResource` as plain lookups
  (`EvaluationContext.cs:122-127, 328-336`). `FhirEvaluationContext.ElementResolver` carries
  `resolve()` (`FhirEvaluationContext.cs:35`).
- **The seam is confirmed.** `FhirPathInvariantCheck.Validate` evaluates with no context
  (`FhirPathInvariantCheck.cs:168`); `ValidationState` already threads immutable per-level context
  (`ValidationState.cs`).
- **Prior art — Firely.** `ScopedNode.ResourceContext` walks up `ParentResource` until it hits a
  Bundle; `BundledResources()` / `ContainedResources()` build a `ReferencedResourceCache` of sibling
  entries. We replicate the *behaviour* (root scope + reference index) without the *mechanism*
  (per-node parent pointers). See [reference-implementations](reference-implementations.md).
- **Prior art — HAPI / org.hl7.fhir validator.** Keeps slice assignment + cardinality + reporting
  imperative for diagnostic granularity, and treats only the discriminator *predicate* as FHIRPath.
  Confirms scope/resolution belongs in the traversal, not the node.
- **Injection seam is pre-built.** `conformsTo`, `memberOf`, `validateVS` are registered FhirPath
  functions that currently `throw NotSupportedException("...requires profile validation
  infrastructure")` (`FhirSpecificFunctions.cs:397-431`) — the same deferred-delegate pattern as
  `ElementResolver`. `SlicingMetadata` already captures discriminators
  (`src/Core/Ignixa.Abstractions/Structure/SlicingMetadata.cs`).

## Related Future Work (out of scope for this investigation)

These build **on top of** Solutions 1 & 2 and are tracked separately:

- **Slicing as a parent-altitude check + FHIRPath discriminators.** Slice *assignment* and
  cardinality stay imperative at the element owning the sliced array (forward-only over
  `Children()`); the per-element discriminator predicate is evaluated via the existing FhirPath
  engine. `SlicingMetadata.Discriminators` already holds the discriminator paths. This is the
  accurate reading of the "HAPI compiles checks to FHIRPath" idea — predicate-as-FHIRPath, not
  whole-profile-as-FHIRPath.
- **`conformsTo()` / `memberOf()` injection.** Implement the stubbed functions by injecting
  delegates into `FhirEvaluationContext` (mirroring `ElementResolver`). `profile`/`type`
  discriminators need `conformsTo` evaluated against the correctly-scoped `%resource` (depends on
  Solution 1); reference discriminators need `resolve()` (depends on Solution 2). This is why 1 & 2
  are the foundation.

## Open Question

Does ignixa ever evaluate FHIRPath / validate a node handed in **without** a controlled traversal
establishing scope (e.g. a public `element.Select("%resource.id")` on a deep node, or
`IElement.Validate()` on an arbitrary sub-element)? If **no** (current state — all evaluation flows
through indexing or the validation walker), Solutions 1 & 2 are sufficient. If **yes**, that single
entry point — and only that one — would justify a lazy `ScopedElement : IElement` built from the
`Meta<JsonNode>()` parent chain. Defer until such an API actually exists.

## Implementation Seam (the three edits)

There is no descent refactor — `ValidationSchema.Validate` already runs against a single root
`element`, and scope only changes where a resource *becomes* a validation root. That is exactly two
sites today, plus the consumer:

1. **Seed at the entry point.** `ValidateResourceHandler.cs:196` currently does
   `var state = new ValidationState();` → change to `new ValidationState().EnterRootResource(element)`.
   This alone fixes `%resource` for every root-level invariant (`dom-*`, `bdl-*`) — the headline gap.
2. **Re-scope on contained recursion.** `ContainedResourceCheck.cs:102` currently does
   `state.WithLocation(containedPath)` → add `.EnterContainedResource(containedElement)`.
3. **Consume at the check.** `FhirPathInvariantCheck.cs:168` evaluates context-free → build a
   `FhirEvaluationContext` from `state.Scope` (snippet above) and pass it to `Evaluate`.

Solution 1 is edits 1 + 3 (and the `EnterContainedResource` half of 2). Solution 2 is filling the
`Resolver` field that those `Enter*` methods already set — `BuildResolver` returns the
`ReferenceIndex` closure instead of null — plus the reference-integrity check. Same machinery; 2 is
an increment on 1, not a second mechanism.

## Verdict

**Recommended — adopt Solution 1 with the `Resolver` field in place from the start (Solution 2
follows as an increment).** Solution 1 alone fixes `%resource`-referencing invariants; leaving the
`Resolver` field present-but-null means `resolve()`-dependent invariants degrade gracefully (empty)
until Solution 2 populates it — no API churn between the two.

Sequencing:
1. Add `ResourceScope` + `EnterRootResource` / `EnterContainedResource` to `ValidationState`.
2. Seed at `ValidateResourceHandler` entry; re-scope in `ContainedResourceCheck`.
3. Build `FhirEvaluationContext` from scope in `FhirPathInvariantCheck`. **← Solution 1 done; `dom-*`/`bdl-*` pass.**
4. Implement `BuildResolver` → `ReferenceIndex` (lazy, cached on `Global.Cache`); add reference-integrity check. **← Solution 2 done.**

Slicing (parent-altitude check + FHIRPath discriminators) and `conformsTo`/`memberOf` injection
follow as separate investigations, built on this foundation.
