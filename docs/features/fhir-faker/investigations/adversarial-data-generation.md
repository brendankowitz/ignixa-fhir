# Investigation: Edge-Case / Adversarial Data Generation

**Feature**: fhir-faker
**Status**: In Progress
**Created**: 2026-06-16
**Revised**: 2026-06-16 (reimagined around an extensible edge-case catalog + decorator pipeline)

## Summary

Add **edge-case data** as a first-class, *requestable* kind of synthetic data — alongside the
existing realistic generators — built from an **extensible catalog of named edge-case strategies**
applied as a seeded, post-generation **decorator pipeline**. The output is deliberately
*valid-but-hostile* FHIR: data that exercises the assumptions downstream parsers, validators,
storage, and search make, rather than the well-behaved data the current layers emit.

The categories are open and customizable: built-in families ship by default, and consumers can
register their own strategies. Each applied mutation is recorded (category + JSON path + before/after)
so the corpus is self-describing, reproducible, and measurable.

## Why this shape (and not "a mode on the builders")

The naive framing — "add an `--edge-cases` mode to the existing generators" — fails three of the
four thinking principles:

- **Separation of concerns.** Making data hostile is a different concern from generating realistic
  data. Folding it into the core means threading a flag through ~20 private `Generate*` methods
  (`SchemaBasedFhirResourceFaker.cs:540-1012`). The realistic generator should stay a pure functional
  core; adversarial-ness belongs in a decorator layer.
- **Reversibility.** A flag baked through the core is near-impossible to remove. A separate
  `EdgeCases/` namespace is deletable in an afternoon.
- **Weakest link.** The original plan asserts "determinism is in place." It is not (see Evidence).
  An un-replayable fuzzer is a random crash generator, not a test tool. Determinism is a
  *prerequisite*, and a decorator layer can own its own seeded RNG independently of the core's
  determinism debt.

So: **mutation/decorator is the mechanism; an extensible category catalog is the product.**

## Proposed design

```
Core generator (functional core, unchanged except one new axis)
  + GenerationDensity { Minimal | Realistic | Maximize }   ← single contained core change
        │
        ▼  realistic (or maximize) resource
Edge-case decorator pipeline            ← new: Core/Ignixa.FhirFakes/EdgeCases/
  • IEdgeCaseStrategy registry (built-in + user-registered)
  • seeded selection + application (own Randomizer)
  • schema-aware element targeting
  • each application tagged: category + JSON path + before/after
        │
        ▼  perturbed resource + mutation manifest
Validate + round-trip harness           ← existing --validate, plus byte-stable round-trip
  bucket → valid-hostile | invalid | round-trip-broken
        │
        ▼  coverage report: per-family fired / validity buckets / round-trip pass-fail
```

### Core abstraction

```csharp
public interface IEdgeCaseStrategy
{
    string Category { get; }            // hierarchical: "unicode.rtl", "temporal.leap-year"
    EdgeCaseFamily Family { get; }      // Unicode | Temporal | StringBoundary | Cardinality | Structural
    ValidityIntent Intent { get; }      // PreservesValidity | MayViolate | AlwaysInvalid

    bool CanApply(MutationTarget target);                 // schema-aware: type, binding, cardinality
    MutationResult Apply(MutationTarget target, Randomizer rng);  // returns what changed
}
```

- **Most strategies are field-level mutators** that walk the resource tree and perturb matching
  elements (unicode, temporal, string-boundary, optional-stripping).
- **A few are resource-level** (maximal cardinality, deep structural nesting) and lean on the core's
  `GenerationDensity.Maximize` rather than mutating.
- `MutationTarget` carries the schema type for the element, so a strategy targets *string* fields for
  unicode and *date* fields for temporal — it never drops a CJK string into a required-bound `code`.

### Extensibility (the part you want)

The catalog is a **registry**, not a fixed enum:

```csharp
// Built-ins registered by default; add your own:
EdgeCaseCatalog.Register(new MyHospitalRtlNameStrategy());

// CLI selection — coarse or fine:
ignixa-fakes resource Patient --out . --edge-cases                 // all built-ins
ignixa-fakes resource Patient --out . --edge-cases unicode,temporal // by family
ignixa-fakes resource Patient --out . --edge-cases unicode.rtl      // by category
ignixa-fakes resource Patient --out . --edge-cases --seed 12345     // reproducible

// Library:
builder.WithEdgeCases();                                   // all
builder.WithEdgeCases(EdgeCaseFamily.Unicode | EdgeCaseFamily.Temporal);
builder.WithEdgeCases(c => c.Only("unicode.rtl", "temporal.leap-year"));
```

### Built-in catalog (initial families → categories)

| Family | Categories (examples) | Mechanism | Default intent |
|--------|----------------------|-----------|----------------|
| `unicode` | `cjk`, `rtl`, `combining`, `emoji`, `zero-width`, `multi-script-long` | mutate string fields | PreservesValidity |
| `temporal` | `leap-year`, `year-boundary`, `far-past`, `far-future`, `partial-precision`, `tz-extreme` | mutate date/dateTime fields | PreservesValidity |
| `string` | `max-length`, `empty-present`, `whitespace-only`, `control-chars`, `injection-like` | mutate free-text fields | PreservesValidity* |
| `cardinality` | `all-optional-omitted`, `every-optional-present` | omit-strip / `Maximize` density | PreservesValidity |
| `structural` | `deep-nesting`, `contained-resources`, `forEach-heavy` | synthesize | MayViolate |

\* `string.injection-like` is **robustness testing** (does the pipeline mangle a value that *looks*
like SQL/HTML?), explicitly *not* a security/pentest feature. Scope it that way in docs to avoid
misreading.

## Determinism

- The pipeline owns a dedicated `Randomizer(seed)`; mutation selection and application are fully
  reproducible from `(seed, resourceIndex)` **today**, regardless of core-gen seeding.
- The mutation manifest records the seed, so any single resource is replayable in isolation.
- **Pre-existing debt (flagged, not blocking):** the core itself is not deterministic —
  `SchemaBasedFhirResourceFaker` uses unseeded `new Faker()`/`new Random()`
  (`SchemaBasedFhirResourceFaker.cs:71-72`) and scenario states spray unseeded `new Faker()` inline
  (`ObservationState.cs:292-362`). Full end-to-end reproducibility needs a `WithSeed` on the core and
  removal of the inline `new Faker()` instances. Worth a separate task.

## Validity boundary — measured, not guaranteed

You cannot *guarantee* a generator stays spec-valid across STU3→R6 without running the validator on
its output. So don't try:

- Strategies declare an **intent**; default mode emits only `PreservesValidity` strategies (the
  valid-but-hostile default). `--include-invalid` opens up `MayViolate` / `AlwaysInvalid` for
  negative testing.
- Intent is a **claim, verified**: every resource runs through the existing `--validate` path and is
  bucketed `valid-hostile | invalid | round-trip-broken`. A `PreservesValidity` strategy that
  produces an invalid resource is a finding — strategy bug or platform bug, both useful.

## Coverage & manifest

Each generated resource gets a sibling manifest (and an optional `meta.tag` per category — the tag
plumbing already exists, `SchemaBasedFhirResourceFaker.cs:1021-1038`):

```json
{
  "resourceId": "…",
  "seed": 12345,
  "mutations": [
    { "category": "unicode.rtl",       "path": "name[0].family", "before": "Smith",      "after": "…" },
    { "category": "temporal.leap-year","path": "birthDate",       "before": "1990-03-15", "after": "1992-02-29" }
  ]
}
```

Roll-up report answers "what did this corpus actually exercise?": per-family fired counts, validity
buckets, round-trip pass/fail. Without this signal, "edge-case interestingness" is unbounded noise.

## Consumers

1. **Demo / interactive** — `ignixa-fakes ... --edge-cases --validate`: generate hostile, validate
   inline, show what the pipeline does. Strong DevDays moment.
2. **CI round-trip gate (the real payoff)** — feed the corpus through ingest → store → retrieve →
   search and assert byte-stability and search correctness. This catches the #280/#281 *class*
   repeatably, not once on stage. Lives in the platform E2E test project, **not** in the faker core.

## Layering

- `Core/Ignixa.FhirFakes/EdgeCases/` — interface, built-in strategies, pipeline, registry. Pure,
  reusable, packageable. Correct Core placement.
- The validate + round-trip harness consuming it lives in the CLI (demo) and the E2E project (CI),
  never inside the faker.

## Tradeoffs

| Pros | Cons |
|------|------|
| Edge-case data becomes a first-class, requestable, **customizable** data type | Schema-aware targeting is real work — getting "valid-but-hostile" to stay valid is the hard part |
| Decorator layer keeps the realistic core pure and the whole feature deletable | Validate-per-resource + tree-walking is slower; fine for demos/CI, not for millions of resources |
| Validity boundary is *measured* (bucketed), not an unsolvable a-priori guarantee | Two families (`every-optional-present`, `structural`) need a core `GenerationDensity` change |
| Self-describing manifest → reproducible + coverage-measurable | "adversarial" / "injection-like" can be misread as a security feature — scope as robustness |
| Pairs with the existing `--validate` path; CI round-trip gate catches the real bug class | Determinism needs the pipeline RNG now and core seeding later (pre-existing debt) |

## Alignment

- [x] Follows layering — new `EdgeCases/` in Core; harness in CLI/E2E, not the faker
- [x] Developer experience — one flag / one builder call, with coarse and fine category selection
- [x] Extensible/customizable — open registry, hierarchical category names, user-registered strategies
- [ ] Determinism — pipeline RNG is self-contained now; **core seeding is a prerequisite for full
      reproducibility** (separate task)
- [~] Specification compliance — valid-but-hostile by default, `--include-invalid` opt-in, validity
      **measured and bucketed** rather than assumed
- [x] Consistent with existing patterns — schema-driven, version-aware, builder-based, `meta.tag` reuse

## Evidence

- **No edge-case capability exists today.** Generators aim at realistic data and never populate
  optional fields (`SchemaBasedFhirResourceFaker.cs:219`, `:448`). Net-new.
- **Determinism is *not* currently in place** (correcting the original claim). Core faker is unseeded
  (`SchemaBasedFhirResourceFaker.cs:71-72`); scenario states use inline unseeded `new Faker()`. The
  decorator can be deterministic regardless, but end-to-end replay needs core seeding work.
- **#281 (fixed-birthday) directly motivates the `temporal` family** — exactly the failure a
  date-boundary mutator surfaces.
- **#280 (UTF-8) motivates `unicode` — with a qualifier.** #280 was a *console-output* encoding bug
  (OEM codepage on stdout). The CLI writes resources via `File.WriteAllTextAsync` (UTF-8), which never
  touches that path. Unicode edge data only catches #280's *class* when the data flows through
  **stdout** or another encoder — which is an argument for the round-trip gate, not just file output.
- **The validation path already exists.** `--validate` validates inline; `ignixa-validator` supports
  `--package hl7.fhir.us.core@x`. "Generate hostile → validate → bucket" is a closed loop.
- **Schema is already threaded everywhere** (`IFhirSchemaProvider` on the faker, states, builders), so
  schema-aware element targeting needs no new plumbing.
- **`meta.tag` plumbing exists** (`SchemaBasedFhirResourceFaker.cs:1021-1038`) for per-category tagging.

## Phased plan

**MVP (demoable, proves the concept):**
1. `IEdgeCaseStrategy` + `EdgeCaseCatalog` registry + seeded pipeline (own `Randomizer`)
2. Two families fully built — `unicode` and `temporal` (map to the two real bugs)
3. `--edge-cases [families]` + `--seed` on the `resource` command, wired to existing `--validate`
4. Mutation manifest output + simple per-family report

**Phase 2:**
5. `string` and `cardinality` families
6. `GenerationDensity { Minimal | Realistic | Maximize }` axis in the core (enables `every-optional-present`)
7. Public custom-strategy registration API
8. CI round-trip gate in the E2E project (ingest → store → retrieve → search, byte-stable)
9. `structural` family (synthesis-heavy; hardest; gate behind `--include-invalid` where it `MayViolate`)

**Separate prerequisite task:** core-generator seeding (`WithSeed`, remove inline `new Faker()`).

## Open decisions

1. **API surface naming** — recommend `--edge-cases` / `WithEdgeCases` / `EdgeCases` namespace for the
   public surface, reserving "adversarial" for prose (avoids the security-feature misread).
2. **Primary first consumer** — recommend demo first (MVP), CI round-trip gate in phase 2.
3. **Core seeding now vs later** — recommend the decorator ships with its own RNG immediately; core
   seeding is a tracked follow-up, not a blocker.

## Verdict

Viable, and stronger than the original framing. The reimagining keeps the capability you want —
edge-case/fuzz data as a customizable, expandable data type — while fixing the concern-mixing,
determinism, and validity-boundary problems by moving it to a seeded decorator layer with a measured
(bucketed) validity signal. Build the catalog + pipeline with two families as the MVP; the schema-aware
targeting is the part to get right.

## Alternatives considered (and why not)

- **Mode baked into the builders** — rejected: concern mixing, flag-coloring through ~20 methods,
  hard to reverse. (This was the original framing.)
- **Static "known-tricky" fixture corpus** — simpler, but not expandable/customizable and goes stale;
  useful as a *seed set* of regression fixtures derived from manifests, not as the mechanism.
- **Pure property-based fuzz harness with no catalog** — maximal coverage but no curation, no
  human-meaningful categories, and nothing demoable. The catalog + manifest gives the curation;
  the round-trip gate gives the property checking. Adopt the property-check *as a consumer*, not as the
  generator.
