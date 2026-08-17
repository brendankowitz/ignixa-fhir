# ADR 2607: Forward-Only Nodes with Descending Context Scopes

## Status

Accepted

> Implemented via PR #286 (tree-context scoping) and the element-scoped invariant altitude fix on
> `feat/validation-implementation`. Validated against the official HL7 FHIR validator conformance
> suite (see [validation roadmap](../features/validation/roadmap.md)).

## Context

A recurring claim — heard again at FHIR DevDays 2026 — is that FHIR resource validation *requires
bidirectional tree navigation*: the parsed data nodes must carry parent pointers so validation can
walk upward. Firely's SDK embodies this with `ScopedNode`, a parent-aware wrapper over
`ITypedElement`.

The claim is motivated by real needs that appear to require reaching "up" from a deep element:

- Invariants that reference `%resource` / `%rootResource` (e.g. `dom-*`, `bdl-*`).
- `resolve()` — following a `Reference` to a contained resource or another Bundle entry.
- Slicing discriminators that inspect sibling elements.
- Error-path reporting (`Patient.contact[2].name`) which names ancestors.

Ignixa's `IElement` model is deliberately **forward-only**: immutable, no parent pointer, navigation
only downward via `Children()`. That model is cheaper, thread-safe, and avoids the lifecycle and
allocation cost of parent-linked nodes — but it appears to conflict with the needs above.

## Decision

**We reject bidirectional node navigation. Nodes stay forward-only; the information that would
otherwise be reached by walking up is instead *carried down* with the traversal in a context scope.**

Navigation and information flow are separate axes. Navigation is always forward. What flows "up" is a
*bounded, enumerable* set of context, threaded through descent and pushed at the boundaries where it
is known:

- `ValidationState` carries the descending context: a `Global`/`Instance`/`Location` chain plus a
  `Scope` (`ResourceScope`).
- `ResourceScope` carries `%resource`, `%rootResource`, and a `resolve()` delegate (backed by
  `ReferenceIndex`: contained `#id` + intra-Bundle `fullUrl` / `Type/id`).
  - **Update (2026-08):** since GitHub issue #400/#401, the FhirPath `resolve()` function tries
    in-instance resolution first (contained resources, sibling Bundle/Parameters entries, and
    container-scoped bare `#`) and only calls this `ResourceScope` delegate as a fallback when
    nothing in-instance matches. This delegate is no longer *the* resolution mechanism, just the
    external one. See `docs/site/docs/core-sdk/fhirpath.md` and `ResolveFunctionTests.cs` for the
    current contract.
- Scope is forked at the two resource boundaries — `EnterRootResource` (handler entry) and
  `EnterContainedResource` (contained recursion). `FhirPathInvariantCheck.BuildEvaluationContext`
  materializes a `FhirEvaluationContext { Resource, RootResource, ElementResolver }` from the scope;
  when scope is unseeded it falls back to context-free evaluation.
- Element-scoped constraints (e.g. `pat-1` on `Patient.contact`) are evaluated at the altitude of
  their owning element — injected into that element's nested schema and run per-occurrence by
  `NestedComplexTypeCheck` — never hoisted to the resource root.

The load-bearing reason this is sufficient: **standard FHIRPath has no upward operator.** There is no
`parent()` and no `..`. Every expression navigates forward from a defined context, and the only
external references it can make are the finite set above. Because the set is known in advance,
threading it exactly is complete — a parent pointer's value is answering questions you did not know
you would ask, and validation is a known algorithm that does not ask them.

## Rationale

- **FHIRPath is downward-only.** The context needed (`%resource`, `%rootResource`, `resolve()`,
  current node, and the location path for error reporting) is bounded and can be carried.
- **The reference implementations agree.** The HL7 Java reference validator threads a `NodeStack`
  through its walk (carried context, not parent-pointed data nodes). The Rust `rh-validator` we
  benchmark against evaluates invariants over untyped `serde_json::Value` with an explicit
  `EvaluationContext(root, current)`. Only Firely's `ScopedNode` is the parent-pointer camp — a lazy
  reconstruction convenience, not a fundamental requirement.
- **Forward-only nodes are cheaper and safer:** immutable, thread-safe, no parent-link lifecycle, no
  wrapper allocation per node.
- **YAGNI.** Parent pointers buy ad-hoc upward queries that validation never issues.

## Consequences

**Positive**

- No parent pointers on `IElement`; the model stays immutable and thread-safe.
- The two "hard" cases that supposedly need backward navigation — `resolve()`/`%resource` invariants
  and slicing — are handled as carried context and parent-altitude forward checks respectively.
- Conformance evidence: across the invariant fixes, over-strict cases (valid resources we wrongly
  reject — a validator's worst failure mode) fell from **54 to 19** on the R4 clean-base slice, with
  no case requiring a back-pointer.

**Negative — the cost, and what reviewers must check**

Correctness moves from "guaranteed by the data structure" to "guaranteed by discipline." Two failure
modes replace the ones parent pointers would prevent:

1. **Missed boundary push.** `%resource` is only correct if a scope was forked at *every* resource
   root — the outer resource, each `contained`, each Bundle entry. Miss one `EnterContainedResource`
   and `%resource` is silently wrong, with no node to walk up from to self-correct.
2. **Wrong constraint altitude.** A constraint must run at the element it is defined on. Hoisting a
   nested-element constraint to the root (the original `pat-1` bug) evaluates it against the wrong
   node. Reviewers of new checks must confirm element-scoped constraints descend to their owner.

Any code that seeds validation (handlers, the conformance runner, tests) must seed the scope the same
way (`new ValidationState().EnterRootResource(element)`) or the context features silently no-op.

## Alternatives considered

- **Parent pointers on `IElement` (Firely `ScopedNode` style).** Rejected: mutable/heavier nodes,
  thread-safety hazards, and generality the algorithm never uses.
- **Lazy parent reconstruction (walk up on demand).** Rejected: same node-model downsides; only
  advantage is tolerating missed boundary pushes, which we address by convention + tests instead.

## References

- PR #286 — tree-context scoping (`ValidationState`, `ResourceScope`, `ReferenceIndex`,
  `ReferenceResolutionCheck`).
- [tree-context-scoping investigation](../features/validation/investigations/tree-context-scoping.md)
- [slicing-discriminators investigation](../features/validation/investigations/slicing-discriminators.md)
  — slicing as a parent-altitude forward check.
- [Validation roadmap](../features/validation/roadmap.md) — conformance measurement.
- [ADR 2510: Three-Tier Validation Architecture](adr-2510-validation-architecture.md)
