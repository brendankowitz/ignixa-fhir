# Investigation: Instance Creation Delegate

**Feature**: fhirpath
**Status**: In Progress
**Created**: 2026-06-16

## Problem

The instance selector (`Type { element: value, ... }`, spec:
<https://build.fhir.org/ig/HL7/FHIRPath/en/index.html#instance-selector>) is
currently evaluated by having the engine construct a bespoke
`FhirPathEvaluator.ComplexElement : IElement` in-memory tree
(`src/Core/Ignixa.FhirPath/Evaluation/FhirPathEvaluator.cs:1832`,
`:1949`). This puts FHIR type-system materialization inside the FHIRPath
engine — the wrong layer — and produces a second-class node that diverges from
the canonical `SchemaAwareElement` the engine navigates everywhere else.

This investigation evaluates pivoting to the approach Firely's .NET SDK
proposed: carry an **instance-creation delegate** on the evaluation context and
hand object construction off to the host's model/type system.

## Approach

Add an optional factory seam to `EvaluationContext`, **replacing** the
`ISchema? Schema` property (the only engine consumer of `Schema` is
`VisitInstanceSelector` at `FhirPathEvaluator.cs:1857`, so the factory subsumes
it — see Evidence). The host-side factory holds whatever schema/model it needs
internally; the engine's context no longer references `ISchema` at all:

```csharp
public interface IInstanceFactory
{
    // Returns a first-class IElement (same kind the engine already navigates),
    // or null if the host cannot construct the type.
    IElement? Create(string typeName, string? namespacePrefix,
        IReadOnlyList<(string name, IReadOnlyList<IElement> values)> elements);
}

public record EvaluationContext
{
    // Replaces `ISchema? Schema` / `WithSchema(...)`.
    public IInstanceFactory? InstanceFactory { get; init; }
    public EvaluationContext WithInstanceFactory(IInstanceFactory factory) => ...;
}
```

`VisitInstanceSelector` becomes: enforce the singleton-input rule, evaluate each
element's value expression (dropping empty-valued elements per spec), then:

1. If `InstanceFactory` is set → delegate construction; the host returns a
   real node (JSON/source-node-backed `SchemaAwareElement`, or a Firely
   POCO-backed node via the `Ignixa.Extensions.FirelySdk6` adapter).
2. If no factory is set → fall back to `ComplexElement`, documented as a
   **transient, navigation-only, no-round-trip** node.

The host (Validation / DataLayer / Api / mapping) wires the factory. The engine
stays model-agnostic.

## Tradeoffs

| Pros | Cons |
|------|------|
| Object construction lives in the host/model layer, not the Core engine (correct layering) | Requires host wiring; an unwired host silently gets the degraded fallback |
| Factory returns the SAME node kind the engine already navigates → no fidelity divergence | Ignixa's canonical node is read-only source-node-backed; host must build a `MutableNode`/`JsonObject` and wrap it (real work, but in the right place) |
| Round-trip/serialization comes for free from the returned node | Two construction paths exist during migration (factory + fallback) until callers adopt |
| Replaces the `Schema` property added for this feature — net-neutral context surface, no redundant model coupling | Fallback semantics must be specified and tested, or behavior is ambiguous |
| Matches Firely prior art; eases future Firely-backed scenarios | Marginally more context surface (`EvaluationContext` already a `record`, low cost) |

## Surviving spec gaps (fold-in, no separate issue)

Verified these are **instance-selector-local**, not engine-wide (type
namespaces are already handled in `CollectionFunctions.cs` `type()`; the
analyzer overrides every other node type). They are tracked here rather than in
a standalone issue, and the pivot should address them:

1. **Special `value` element + primitive conversion.** Spec: *"the engine is
   responsible for performing any type conversions from fhirpath primitives to
   the target object/type system… particularly for primitive types using the
   special `value` element name."* Current code treats `value` as an ordinary
   child. Under the delegate, conversion becomes the factory's responsibility —
   the contract must pass primitives in a convertible form.
2. **Namespace prefix dropped at eval.** `InstanceSelectorExpression.NamespacePrefix`
   is parsed (`FHIR.Identifier`) then ignored — `VisitInstanceSelector` uses
   `expression.TypeName` only. The factory contract above passes
   `namespacePrefix` through so the host can disambiguate `System.` vs `FHIR.`.
3. **Analyzer type inference.** `FhirPathAnalyzer` never overrode
   `VisitInstanceSelector`, so it inherits `DefaultFhirPathExpressionVisitor`'s
   `default!` → created instances have no static type. The analyzer should infer
   the declared `typeName` as the result type regardless of the eval pivot.

## Alignment

- [x] Follows architectural layering rules — moves type materialization out of the Core engine into the host
- [x] Developer Experience (works with minimal setup) — degraded `ComplexElement` fallback keeps schema-less/test usage working with zero wiring
- [x] Specification compliance — enables `value`/conversion and namespace handling the bespoke node can't
- [x] Consistent with existing patterns — `with`-based optional dependency on `EvaluationContext` (same shape as `TraceHandler`/`NodeEvaluationHandler`)

## Evidence

- **Current implementation**: `FhirPathEvaluator.VisitInstanceSelector`
  (`:1832`) builds `ComplexElement` (`:1949`). `ComplexElement` is a private,
  impoverished `IElement`: `Location => ""`, `Meta<T>() => null`, `Type` only if
  a `Schema.GetTypeDefinition` lookup succeeded, flat name/element child list, no
  `value[x]` choice-type naming, no primitive+shadow extension model.
- **Canonical node**: `Ignixa.Serialization/SourceNodes/SchemaAwareElement.cs`
  wraps an `ISourceNavigator` + `ISchema` (schema-driven child resolution,
  instance-type derivation, choice types). This is what the engine navigates
  everywhere else — the divergence the pivot removes.
- **No round-trip today**: nothing converts `ComplexElement` back to JSON/POCO
  (`grep` for `ToJson|ToResource|ToPoco|MutableNode` in `Ignixa.FhirPath`
  returns only `Expression.ToFhirPath`). Created instances are navigation-only.
- **`Schema` exists only for this feature**: the sole engine consumer of
  `EvaluationContext.Schema` is `VisitInstanceSelector` (`FhirPathEvaluator.cs:1857`,
  `context.Schema?.GetTypeDefinition(typeName)`). It was added by the
  "Ability to attach schema" commit purely to feed instance-selector
  construction. The factory subsumes it, so the pivot **removes** `Schema` /
  `WithSchema` from `EvaluationContext` rather than adding alongside it —
  effectively reverting that part of the commit.
- **Firely adapter as a factory home**: `Ignixa.Extensions.FirelySdk6`
  (`TypedElementAdapter`, `IgnixaElementAdapter`) is the natural place for a
  POCO-backed `IInstanceFactory` implementation.
- **Spec semantics already correct** and should be preserved: singleton-input
  (empty→empty, >1→error), drop-empty-element, `{:}` empty object.
- **Prior art**: Firely .NET SDK's instance-selector proposal — add a creation
  delegate to the context and delegate to the model provider rather than the
  engine owning construction.

## Open decisions

1. **Fallback when no factory is wired**: degrade to `ComplexElement`
   (recommended — preserves test/schema-less usage) vs. throw
   "instance creation not supported in this context".
2. **Factory ownership**: a new `IInstanceFactory` (engine context drops
   `ISchema` entirely) vs. extending `ISchema` with a `Create(...)` method
   (context keeps an `ISchema` reference, used for construction not metadata
   attachment). Leaning separate interface — construction and metadata lookup
   are distinct concerns, and it lets the context shed `ISchema` cleanly.
3. **Node backing**: ~~source-node-backed vs. Firely-POCO-backed~~ —
   **resolved (2026-06-16): source-node-backed.** `SourceNodeInstanceFactory`
   in `Ignixa.Serialization` builds the node natively (see below); no Firely
   dependency. Firely-POCO backing remains a future option for hosts already on
   the Firely model, but is not required.

## Alternatives considered

- **Status quo + scope-down**: keep `ComplexElement`, declare instance selectors
  transient-only, drop the schema-metadata attachment. Cheapest; punts if a
  round-trip consumer (mapping/StructureMap, value-producing invariants) appears.
- **First-class `ComplexElement` via source nodes**: build a `MutableNode`/
  `JsonObject` in the engine and wrap with `SchemaAwareElement`. Gets fidelity
  but keeps construction in the Core engine — wrong layer, more code.

## Spike findings (2026-06-16)

Prototyped the seam end-to-end (engine-side only, test-double factory):

- New `IInstanceFactory` + `InstanceElement` contract in
  `Ignixa.FhirPath.Evaluation`.
- `EvaluationContext`: `Schema`/`WithSchema` replaced by
  `InstanceFactory`/`WithInstanceFactory` (net context surface unchanged).
- `VisitInstanceSelector` delegates when a factory is present, else builds the
  `ComplexElement` fallback. Singleton-input rule and empty-element drop
  preserved; `{:}`/`{}` collapse to zero elements (no special branch).
- Tests: replaced the schema-attachment region with 4 seam tests
  (delegation + inputs, null→empty, namespace prefix flows through, no-factory
  fallback). Full FhirPath suite green (3990 passed).

What the spike confirmed:

1. **The seam is clean and small.** Engine delegates with zero model coupling;
   fallback preserves existing behavior. Reversible.
2. **Namespace prefix now reaches the factory for free** — closes gap #2; the
   host decides `System.` vs `FHIR.`.
3. **The cost is the node backing, not the seam.** `JsonSourceNodeFactory` is
   resource-centric (`ResourceJsonNode` requires a `resourceType`); there is no
   existing path to build a first-class source-node-backed *datatype* node
   (`Coding`, `Identifier`). A production factory therefore needs either a
   datatype-capable source-node/`MutableNode` builder, or a Firely-POCO-backed
   implementation in `Ignixa.Extensions.FirelySdk6`. This sharpens open
   decision #3 — and is exactly the work that belongs in the host, not the
   engine, which is the point of the pivot.

### Production factory (2026-06-16)

Built the native backing and proved it:

- Contract `IInstanceFactory` + `InstanceElement` moved to **`Ignixa.Abstractions`**
  (sits with `ISchema`/`IElement`; both `Ignixa.FhirPath` and
  `Ignixa.Serialization` reference Abstractions, so no new cross-references).
- **`SourceNodeInstanceFactory`** (`Ignixa.Serialization.SourceNodes`): builds a
  `JsonObject` from the evaluated elements, wraps via `JsonNodeSourceNode.Create`,
  returns a `SchemaAwareElement` with explicit type definition — the same node
  kind the engine navigates elsewhere. Conversion: source-node-backed values
  clone their JSON via `Meta<JsonNode>()`; primitive literals fall back to a
  scalar `JsonValue`. Declines (`null`) for schema-unknown types and the
  `System.` namespace.
- The resource-centric `JsonSourceNodeFactory` wall was sidestepped via the
  lower-level `JsonNodeSourceNode.Create` (no `resourceType` required).
- Tests: `SourceNodeInstanceFactoryTests` (5) — navigable typed node, JSON
  round-trip, empty object, unknown-type→null, System-namespace→null. Green.

Not yet done:

- **Wiring**: no `EvaluationContext` construction site sets `InstanceFactory`
  yet — instance selectors still hit the `ComplexElement` fallback in production.
- **Analyzer type inference** (gap #3): `FhirPathAnalyzer.VisitInstanceSelector`
  still inherits the `default!` no-op.
- **Special `value` element** (gap #1): primitive *target* types using the
  `value` element name aren't special-cased in the factory yet.

## Verdict

**Seam validated and production backing proven.** The delegate approach
(option A) works with a small, reversible engine change plus a native
`SourceNodeInstanceFactory` that returns first-class, round-trippable nodes — no
Firely dependency. The earlier "node backing" risk is retired. Remaining work is
integration, not feasibility: wire the factory into `EvaluationContext`
construction, add analyzer type inference, and handle the special `value`
element. The "round-trip consumer?" question now only affects *when* to wire the
factory in by default (vs. leaving the fallback), not whether the approach is
viable.
