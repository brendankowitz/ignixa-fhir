# ADR 2607: Validation Oracle-Conformance Scope & Declaration

## Status

Accepted

> Declares "oracle-compliant for supported scope" against the official HL7 `fhir-test-cases`
> validator suite (R4), graded vs the Java reference validator's recorded outcomes. Freezes the
> exclusion list and the tracked-gap register that back the claim.

## Context

Ignixa validation is measured continuously against the official HL7 validator conformance suite via
`ValidatorConformanceRunner` (see [roadmap](../features/validation/roadmap.md)). The grading oracle is
the Java reference validator's recorded `OperationOutcome` per case. Not every case is achievable for
an offline, deterministic validator, and chasing 100% on a suite that includes cryptographic signature
verification or live-terminology-server behaviour is chasing a number, not a product.

The **supported-scope pass rate** — total minus an explicit, frozen exclusion list — is the honest
metric, per the Well-Architected reliability lens (declare what you don't do). This ADR freezes that
list so the conformance claim is defensible and auditable.

## Metric (R4 clean-base slice, 193 scored, 7 vendoring-gap skips)

| Measure | Value |
|---|---|
| **Over-strict (we reject, reference accepts)** | **0** — the worst failure mode, fully eliminated |
| Raw pass rate | 163/193 = 84.5% |
| **Supported-scope pass rate** (excl. the 13 out-of-scope-by-design) | **163/180 = 90.6%** |

Journey: over-strict **54 → 0**; raw pass rate **63.6% → 84.5%**. Every over-strict was retired by a
root-caused fix, never a suppression (see the burndown in the roadmap progression).

## Decision

Declare **oracle-compliant for supported scope**, on these three conditions (all met):

1. **Zero unexplained over-stricts.** Over-strict is 0. There are no cases where we are stricter than
   the reference without a documented, deliberate justification.
2. **The exclusion list is frozen here** with per-item rationale (below).
3. **Every non-excluded under-strict case is categorized** — either tracked feature gaps or
   offline-resolution-blocked cases whose capability exists but whose data the offline benchmark does
   not vendor.

### Exclusion list — out-of-scope by design (13 cases)

| Category | Cases | Rationale |
|---|---|---|
| **Remote terminology** | obs-temp-code2, vs-bad-code | Membership/validity of codes in external systems (SNOMED/LOINC) is undecidable offline. Requires a terminology server (the deferred "T3"). Explicitly ruled out for this effort. |
| **SNOMED-ECL / ValueSet-expression parsing** | vs-bad-ecl, vs-bad-ecl-us, vs-params-2/3/4 | Parsing/validating SNOMED ECL and `text/fhirpath` VS filter expressions — niche subsystems, terminology-server territory. |
| **SearchParameter static analysis** | capstmt, sp-composite, sp-diff-base, sp-diff-type | FHIRPath static type-inference over the SearchParameter AST is a subsystem, not a check (4 cases for weeks of work). Product-motivated backlog (protects the indexer), not built for the benchmark. |
| **Digital signature verification** | signatures-example-2 | Cryptographic signature verification is out of a structural validator's remit. |
| **IG-publishing mode** | cs-narrative-status-pub | `for-publication` mode (HL7-publishing workgroup rules) — not a resource-validity concern. |

### Offline-resolution-blocked (10 cases — capability exists, data not vendored)

Full profile validation **works** — `PackageBackedValidator` loads packages and resolves profiles,
extensions, and CodeSystems (proven by the offline bp-profile e2e and 33 US Core scenario tests). The
offline conformance runner loads only `hl7.fhir.r4.core`, so these cases stay under-strict purely
because the reference had IG/terminology packages loaded that the benchmark does not vendor offline:

- **Extension-definition (7):** ips-htmlrefs-backwards, maiden-name-extension, pat-dob-ext,
  nested-questionnaire-nested-valueset, target-ref-profile-empty, obs-vs-1, patient-with-turvakielto —
  reference vendor IG extensions (validitron, hl7.fi, fkcfhir, cardx-htn, sdc) not loaded offline.
- **Display validation (3):** cvx, ips-nz-pj, uk-msg — CVX/LOINC CodeSystems are not in the core
  package and SNOMED ships as a fragment (not `content: complete`), so display can't be verified.

These are closable by loading the relevant packages; not pursued in the offline benchmark because
forcing resolution (or erroring on every unresolvable extension) would spray over-strict and break the
zero-over-strict milestone for cases that are data-blocked, not capability-blocked.

### Tracked feature gaps (7 cases — real, deferred with a category)

| Case | Missing feature |
|---|---|
| obs-mz | StructureDefinition differential-path validation (snapshot-authoring diagnostics) |
| contract-binding-test | CodeSystem-membership validation (distinct from value-set binding) |
| mr-covid-bnd1 | `Coding.system`-absolute check behind Bundle-entry + CodeableConcept recursion |
| bundle-with-contained | Narrative hyperlink (`div/p/a` → contained) resolution |
| obs-temp-bad | Profile-specific magic-code (needs profile + terminology) |
| qr-bad-ref2 | Canonical-URL resolution for QuestionnaireResponse |
| allergy | Required-binding-on-empty for primitive-shadow (`_category`) elements |

## Consequences

- The conformance claim is **"oracle-compliant for supported scope (90.6%), zero over-strict"** — a
  defensible, auditable statement, not a raw percentage.
- Over-strict stays gated at 0 in CI-adjacent runs; any regression is a merge blocker (the discipline
  that got us here).
- The offline-resolution-blocked set is the natural next lever when IG/terminology packages are
  vendored into the benchmark — the validator capability is already in place.
- The tracked gaps are the backlog; each is a categorized issue, not a silent miss.

## References

- [Validation roadmap](../features/validation/roadmap.md) — progression + supported-scope framing.
- [ADR 2607: Forward-Only Nodes with Descending Context Scopes](adr-2607-forward-only-validation-context.md).
- Terminology follow-up (T3): [terminology-completeness](../features/validation/investigations/terminology-completeness.md).
