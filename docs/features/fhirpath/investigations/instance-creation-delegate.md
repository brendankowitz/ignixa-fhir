# Investigation: Instance Creation Delegate

**Feature**: fhirpath
**Status**: Implemented
**Created**: 2026-06-16

## Problem

The instance selector (`Type { element: value, ... }`, spec:
<https://build.fhir.org/ig/HL7/FHIRPath/en/index.html#instance-selector>) was
originally evaluated by having the engine construct a bespoke
`FhirPathEvaluator.ComplexElement : IElement` in-memory tree. That put FHIR
type-system materialization inside the FHIRPath engine — the wrong layer — and
produced a second-class node that diverged from the canonical
`SchemaAwareElement` the engine navigates everywhere else. That fallback has
since been removed; construction is host-delegated only (see Decisions, #2).

This investigation evaluates pivoting to the approach Firely's .NET SDK
proposed: carry an **instance-creation delegate** on the evaluation context and
hand object construction off to the host's model/type system.

## Approach

Add an optional creation delegate to `EvaluationContext`, **replacing** the
`ISchema? Schema` property (the only engine consumer of `Schema` is
`VisitInstanceSelector`, so the delegate subsumes
it — see Evidence). The host-side implementation holds whatever schema/model it
needs internally; the engine's context no longer references `ISchema` at all.
The shape mirrors the existing `resolve()` hook
(`FhirEvaluationContext.ElementResolver`) — a delegate on the context, not an
interface, so a host can wire a lambda or a method group:

```csharp
public sealed record InstanceCreationRequest(
    string TypeName,
    string? NamespacePrefix,
    IReadOnlyList<InstanceElement> Elements);

public record EvaluationContext
{
    // Replaces `ISchema? Schema` / `WithSchema(...)`.
    // Returns a first-class IElement (same kind the engine already navigates),
    // or null if the host cannot construct the type.
    public Func<InstanceCreationRequest, IElement?>? InstanceCreator { get; init; }
    public EvaluationContext WithInstanceCreator(Func<InstanceCreationRequest, IElement?> creator) => ...;
}
```

`VisitInstanceSelector` becomes: enforce the singleton-input rule, evaluate each
element's value expression (dropping empty-valued elements per spec), then:

1. If `InstanceCreator` is set → delegate construction; the host returns a
   real node (JSON/source-node-backed `SchemaAwareElement`, or a Firely
   POCO-backed node via the `Ignixa.Extensions.FirelySdk6` adapter).
2. If no creator is set → throw. The engine has no object model of its own, so
   there is nothing honest to return (see Decisions, #2).

The host (Validation / DataLayer / Api / mapping) wires the delegate. The engine
stays model-agnostic.

## Tradeoffs

| Pros | Cons |
|------|------|
| Object construction lives in the host/model layer, not the Core engine (correct layering) | Requires host wiring; an unwired host gets a hard failure at the first instance selector |
| Factory returns the SAME node kind the engine already navigates → no fidelity divergence | Ignixa's canonical node is read-only source-node-backed; host must build a `MutableNode`/`JsonObject` and wrap it (real work, but in the right place) |
| Serialization comes from the returned node rather than a parallel model | Only one construction path exists, so every host that wants instance selectors must wire one |
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
- [x] Developer Experience (works with minimal setup) — a host that has not wired a creator gets an error naming `WithInstanceCreator` and the concrete factory to use, instead of a node that silently behaves differently from a parsed one
- [x] Specification compliance — enables `value`/conversion and namespace handling the bespoke node can't
- [x] Consistent with existing patterns — `with`-based optional dependency on `EvaluationContext` (same shape as `TraceHandler`/`NodeEvaluationHandler`)

## Evidence

- **Implementation at the time of this investigation**:
  `FhirPathEvaluator.VisitInstanceSelector` built `ComplexElement`, a private,
  impoverished `IElement`: `Location => ""`, `Meta<T>() => null`, `Type` only if
  a `Schema.GetTypeDefinition` lookup succeeded, flat name/element child list, no
  `value[x]` choice-type naming, no primitive+shadow extension model.
- **Canonical node**: `Ignixa.Serialization/SourceNodes/SchemaAwareElement.cs`
  wraps an `ISourceNavigator` + `ISchema` (schema-driven child resolution,
  instance-type derivation, choice types). This is what the engine navigates
  everywhere else — the divergence the pivot removes.
- **No round-trip at the time**: nothing converted `ComplexElement` back to
  JSON/POCO (`grep` for `ToJson|ToResource|ToPoco|MutableNode` in
  `Ignixa.FhirPath` returned only `Expression.ToFhirPath`). Created instances
  were navigation-only.
- **`Schema` existed only for this feature**: the sole engine consumer of
  `EvaluationContext.Schema` was `VisitInstanceSelector`
  (`context.Schema?.GetTypeDefinition(typeName)`). It was added by the
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

1. **Fallback when no factory is wired**: ~~degrade to `ComplexElement` vs. throw~~
   — **resolved (2026-07-29): throw.** See Decisions, #2.
2. **Creation-hook ownership**: ~~a new `IInstanceFactory` (engine context drops
   `ISchema` entirely) vs. extending `ISchema` with a `Create(...)` method~~ —
   **resolved (2026-07-28): a delegate on the context.** Construction and
   metadata lookup are distinct concerns, and the delegate mirrors the existing
   `resolve()` hook (`ElementResolver`), so hosts wire a lambda or method group
   with no interface to implement. The context sheds `ISchema` cleanly.
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

### Integration (2026-06-16)

Wiring strategy: **explicit per-call injection** at schema-bearing sites.

- **Validation invariants wired**: `FhirPathInvariantCheck` builds an
  `EvaluationContext().WithInstanceCreator(new SourceNodeInstanceFactory(_schema).Create)`
  and passes it to `Evaluate`. Validation suite green.
- **Analyzer type inference** (gap #3): `FhirPathAnalyzer.VisitInstanceSelector`
  now infers the declared type (resolves via schema; falls back to the type name
  for unknowns) and still visits child value expressions. Analyzer tests green.
- **Special `value` element** (gap #1): `SourceNodeInstanceFactory` now produces
  a primitive node (not a complex object) when the target type is primitive and
  the sole element is `value` — e.g. `code { value: 'final' }` → primitive `code`.

Still deferred:

- **Mapping engine wiring**: `FhirMappingLanguage.FhirPathIntegration` has no
  `ISchema` in scope (ctor takes only a cache flag); wiring it means threading a
  schema through the mapping engine. The FHIR Mapping Language has its own
  `create` mechanism, so this is lower priority — deferred.
- **Namespace `System.` construction**: factory declines it; no consumer needs
  System-type construction yet.

### Delegate pivot (2026-07-28)

Collapsed the `IInstanceFactory` interface into a delegate on the context, so the
creation hook reads the same as `resolve()`:

- `Ignixa.Abstractions`: `IInstanceFactory` deleted; `InstanceElement` and the new
  `InstanceCreationRequest` record each live in their own file.
- `EvaluationContext`: `InstanceFactory`/`WithInstanceFactory(IInstanceFactory)` →
  `InstanceCreator`/`WithInstanceCreator(Func<InstanceCreationRequest, IElement?>)`,
  mirroring `FhirEvaluationContext.ElementResolver`/`WithElementResolver`.
- `SourceNodeInstanceFactory` keeps its name and behavior but no longer implements
  an interface; `Create(InstanceCreationRequest)` is wired as a method group
  (`.WithInstanceCreator(new SourceNodeInstanceFactory(schema).Create)`).
- Tests wire lambdas/method groups instead of declaring test-double classes.

Why: one fewer public type in Abstractions, hosts wire a lambda without declaring
a class, and the two host hooks on the context (`resolve()` and instance creation)
are now shaped identically. Reversible — an interface can be reintroduced as a
wrapper without touching the engine.

## Decisions (2026-07-29)

### What the spec actually settles

Before deciding anything, the source was checked directly
([HL7/FHIRPath@`c95ad83`](https://github.com/HL7/FHIRPath/blob/c95ad83b35babc67a383369c96535c39e9487fd3/input/pages/index.md)):

- **The Object Creation section is marked STU**, not normative, even though the
  page as a whole is normative.
- **No conformance tests exist.** `FHIR/fhir-test-cases` `tests-fhir-r5.xml`
  self-identifies as FHIRPath 2.0.0 and contains no object-construction cases.
- **HAPI does not implement the syntax at all** — its `ExpressionNode.Kind` enum
  has only `Name, Function, Constant, Group, Unary`.
- **Firely's implementation is real but unmerged** (branch
  `feature/BP-fhirpath-long-and-instanceselector`): a `ModelObjectFactory`
  delegate on `FhirEvaluationContext` that throws when unset and treats a null
  return as empty.
- Grepping every packaged specification for instance-selector syntax inside
  invariant `expression` fields returns **zero matches** — no shipped FHIR
  constraint uses this today.

The spec **is** explicit about: the singleton-input rule, dropping
empty-valued elements, `{:}`, populating repeating elements from a multi-item
expression, that "the engine **MAY** throw an error" when it cannot represent the
requested multiplicity, the special `value` element, and that the engine is
responsible for converting FHIRPath primitives into the target type system.

The spec is **silent** about: unknown type names, duplicate element keys,
singleton-vs-array representation, choice-element naming, whether the result must
be a valid standalone instance, and `System.*` construction.

Note that `is`/`as` *do* specify throwing on unresolved type names, but that
language is deliberately not carried into the instance-selector section — it was
not assumed to apply here.

So the four decisions below are **Ignixa implementation choices in spec-silent
territory**, not compliance requirements. They are recorded here so the reasoning
survives, and because a future normative revision may contradict them.

### 1. Factory contract — emit parser-friendly FHIR JSON (with known gaps)

`SourceNodeInstanceFactory` emits the discriminators a FHIR parser needs, but
does not yet produce fully canonical FHIR JSON:

- `resourceType` is written when the target type is a resource. Without it the
  backing JSON has no type discriminator and cannot be read back. It is written
  after the element assignments so an assignment cannot forge it.
- A choice element assigned by its base name is stored under the type-suffixed
  name: `Observation { value: Quantity{...} }` → `valueQuantity`. The suffix is
  only applied when the assigned value's type matches one of the element's
  declared choice types; an unmatched type keeps the base name rather than
  inventing a property. Names that already carry a suffix are left alone.
  FHIRPath navigation by the base name still works, because
  `SchemaAwareElement` already resolves choice variants.

**Deliberately not done:** a single value assigned to a repeating element is
still emitted as a JSON scalar, not a one-item array. Fixing that means
consulting cardinality on every write and changes the shape of existing output;
it is a separate change with its own blast radius. Primitive shadow content
(`_value` extensions/id) is likewise not emitted.

**Consequence worth knowing:** because created nodes are read back through the
schema exactly like parsed ones, values are typed by the schema, not by the
FHIRPath literal. `Quantity { value: 42 }` yields a `decimal`, not an `integer` —
which is precisely the conversion the spec asks for. Conversely, an element the
schema does not know has no type to convert against, so its value surfaces as
text.

### 2. No creator wired — throw

The engine owns no object model, so with no creator there is nothing truthful to
return. The previous `ComplexElement` fallback produced a node that navigated
like a real one but had no schema metadata and could not be serialized, so
misconfiguration surfaced later as confusing downstream behaviour rather than at
the point of failure. Matches Firely. The exception names both
`WithInstanceCreator` and the concrete factory to wire.

**Known gap:** `FhirPathInvariantCheck` is the only host wiring a creator.
`SqlOnFhirEvaluationVisitor` and the general-purpose `TypedElementExtensions`
helpers build bare contexts, and `FhirMappingLanguage.FhirPathIntegration` has no
`ISchema` in scope at all. Instance selectors used from those paths now fail
loudly. Wiring them means threading a schema through each host — a separate
change, and FML additionally has its own `create` mechanism.

### 3. Unknown type names — analyzer error, runtime empty

`FhirPathAnalyzer.VisitInstanceSelector` reports an error for a type the schema
cannot resolve. Runtime stays lenient (the creator declines, the expression
yields empty), matching Firely's null-means-empty contract. The analyzer still
contributes the unresolved name to the result type set so downstream navigation
does not cascade a second wave of "empty context" errors from the same root
cause.

### 4. Duplicate assignments — schema-driven

Assignments are grouped by element name instead of being written one at a time,
so `HumanName { given: 'John', given: 'Jacob' }` keeps both values rather than
the second silently overwriting the first. The same applies to a single
assignment whose expression yields several items. When the schema says the
element does not repeat, this throws — cover for which is the spec's explicit
"the engine MAY throw an error". Cardinality is resolved through the choice
base name *and* its type-suffixed forms, so `valueString` is enforced exactly as
`value` is. Element names absent from the schema are passed through unchanged
and aggregate freely: this factory constructs, it does not validate.

## Verdict

**Implemented.** The delegate approach works with a small, reversible engine
change plus a native `SourceNodeInstanceFactory` — no Firely dependency. The
earlier "node backing" risk is retired: created instances are the same
`SchemaAwareElement` kind the engine navigates everywhere else, backed by JSON
that carries `resourceType` and canonical choice-element names.

The claim in earlier revisions that created nodes are fully "round-trippable" was
too strong and has been corrected. Two known gaps remain in the backing JSON:

1. A single value assigned to a repeating element is emitted as a scalar rather
   than a one-item array (see Decisions, #1).
2. Primitive shadow content (`_value` extensions/id) is not emitted, because
   that requires writing a sibling `_{name}` property and `ElementJsonConverter`
   returns a single node. Documented on `ElementJsonConverter.ToJsonNode`.

Remaining integration work is host wiring for `SqlOnFhir` and the mapping engine
(see Decisions, #2), not feasibility.

