# Investigation: Theme-Consistent (Semantically Coherent) Generation

**Feature**: fhir-faker
**Status**: In Progress
**Created**: 2026-07-04

## Summary

At `GenerationDensity.Maximum`, `SchemaBasedFhirResourceFaker` populates every optional coded
element on a resource, but each element is picked **independently and uniformly at random** from
its own value-set pool. The result is schema-valid but clinically nonsensical: a single
`Procedure` can carry `category = Chiropractic manipulation`, `code = Arthroscopy of knee`,
`bodySite = Head of phalanx of great toe`, `performer.function = Laboratory hematologist`, and
`complication = [Diabetes mellitus type 2, Asthma]` — five unrelated clinical facts glued onto one
resource. Nothing in the generator ties sibling code picks to a single clinical story.

Proposed fix: introduce a **`Theme`** (a `ClinicalDomain`, e.g. `Cardiology`, `Endocrinology`,
`OrthopedicSurgery`) chosen once per `Generate()` call, and tag the existing curated `FhirCode`
pools (`Procedures`, `Conditions`, `Medications`, `Observations`) with the domain they already
belong to (today only expressed as **comments**). Binding-aware and heuristic code selection
filter by the active theme first, falling back to today's full-pool random pick whenever no
themed match exists. This stays inside Layer 1, is additive/opt-in at the API surface, and reuses
two idioms already proven elsewhere in this codebase.

## Why this shape (and not "just wire it through Scenarios")

- **Separation of concerns.** The reported symptom happens on a single, standalone
  `Generate(resourceType)` call — the exact path exercised by
  `ignixa-fakes resource <Type> --density maximum` (see Evidence). Routing that through
  `ScenarioBuilder`/`Predefined` scenarios to borrow their coherence would pull Patient/Encounter/
  Timeline machinery into what is supposed to be a single-resource call, and blur the Layer 1 vs.
  Layer 2 boundary that `layered-architecture.md` treats as a deliberate, valuable separation.
- **Weakest link.** The actual weak link is data, not code: the curated pools already group codes
  by clinical specialty, but only in **comments** (`Procedures.cs`, `FhirCode.cs`). The fix is to
  make that latent structure real and queryable, not to add a new generation pipeline.
- **Reversibility.** A `Theme` property that defaults to "unset" and a `BindingCodeMapper` overload
  that falls back to the existing full-pool pick when no theme match exists is deletable by
  reverting the tag data and the new overload — nothing about `Generate()`'s control flow changes
  shape.
- **Explicit over hidden.** Mirror the existing `Density` property exactly: a visible, settable
  instance property with a safe default, not an implicit side effect of some other flag.

## Proposed design

```
SchemaBasedFhirResourceFaker
  Theme: ClinicalDomain?  ← new settable property, mirrors Density; null = today's behavior
        │
        │  resolved once per Generate() call (explicit value, or lazily
        │  PickRandom<ClinicalDomain>() on first theme-eligible pick)
        ▼
  TryGenerateFromBinding / GenerateCodeableConcept / GenerateCodeableReference / GenerateCoding
        │
        ▼
  BindingCodeMapper.TryGetCodesForValueSet(uri, valueSetProvider, theme, out codes)
        │  filter curated pool by FhirCode.Domain == theme
        │  ├─ match found        → themed subset
        │  └─ no match / theme null → today's full pool (unchanged behavior)
        ▼
  TryGetCodeFromHeuristic (unbound fields)
        │  today: fixed constants (Hypertension / Ibuprofen 400mg / BodyWeight)
        ▼  becomes: small per-domain arrays, same constants as the "no theme" default
```

### Data model

```csharp
// FhirCode.cs — additive positional parameter, default null, existing call sites unaffected
public sealed record FhirCode(string System, string Code, string Display, ClinicalDomain? Domain = null);

// New, small, flat enum — mirrors names already in Scenarios/Codes/Specialties.cs
public enum ClinicalDomain
{
    FamilyMedicine, InternalMedicine, Cardiology, Endocrinology, GeneralSurgery,
    OrthopedicSurgery, Gastroenterology, ObstetricsGynecology, Urology, Radiology,
    // ... remaining entries mirror Specialties.cs
}
```

```csharp
// BindingCodeMapper.cs — new overload, existing 3-arg signature untouched
public static bool TryGetCodesForValueSet(
    string? valueSetUri, IValueSetProvider? valueSetProvider,
    ClinicalDomain? theme, out FhirCode[] codes)
{
    if (!TryGetCodesForValueSet(valueSetUri, valueSetProvider, out var all))
    {
        codes = [];
        return false;
    }

    var themed = theme is { } t ? all.Where(c => c.Domain == t).ToArray() : [];
    codes = themed.Length > 0 ? themed : all;
    return true;
}
```

A coarse, flat enum (no hierarchical `"cardiology.arrhythmia"`-style sub-category) is a deliberate
simplification versus the `EdgeCaseFamily`/`Category` precedent: clinical specialty doesn't need a
second axis for this problem, and adding one would be complexity the current task doesn't need.

### Usage (mirrors `Density`)

```csharp
var faker = new SchemaBasedFhirResourceFaker(schemaProvider, seed)
{
    Density = GenerationDensity.Maximum,
    Theme = ClinicalDomain.OrthopedicSurgery   // optional — omit to let the faker pick one
};
```

```
ignixa-fakes resource Procedure --density maximum --theme orthopedic-surgery --out .
ignixa-fakes resource Procedure --density maximum --out .   // theme chosen at random, still coherent
```

## Tradeoffs

| Pros | Cons |
|------|------|
| Directly fixes the reported symptom: sibling coded fields on one resource become clinically plausible | Requires manually tagging existing `FhirCode` constants with a `Domain` — one-time curation cost, no shortcut |
| Also fixes a second, independent bug: `TryGetCodeFromHeuristic`'s fixed constants make every resource's condition/medication/observation identical today, not just incoherent | Value sets that fall through to `IValueSetProvider` (spec-wide codes, not curated pools) get no benefit until/unless those are tagged too |
| Additive shape: new `BindingCodeMapper` overload, existing 3-arg signature and its tests untouched | A `PickRandom<ClinicalDomain>()` draw shifts the Faker/Random sequence for seeded callers — reproducibility holds within a run, not across turning theming on for a previously-pinned seed |
| Reuses two idioms already proven in this codebase (`Specialties.cs` vocabulary; `EdgeCaseFamily`-style coarse grouping) | Doesn't touch free-text generation (`Lorem.Sentence(3)` in `GenerateCodeableConcept`'s `text`) — prose stays unrelated to the theme |
| Stays entirely inside Layer 1 — no coupling to `ScenarioBuilder`/`ScenarioContext` | Doesn't by itself reach Layer 2/3 cross-resource coherence — needs the `ScenarioContext` follow-up (Phase 2) to go further |

## Alignment

- [x] Separation of concerns — tagging lives on existing `Codes/` data plus a filter in `BindingCodeMapper`; no Layer 1/Layer 2 blending
- [x] Reversibility — additive overload + optional property; revert the tag data and the overload to remove it entirely
- [~] Weakest link — curation debt (tagging every pool) is the remaining weak link; MVP scoped to the four pools the user's own example exercised
- [x] Explicit over hidden — `Theme` is a visible, settable property mirroring `Density`; no implicit behavior change when unset
- [ ] Determinism — reuses the existing seeded `Faker`/`Random`; adds one new draw to the sequence when a theme is auto-selected (see Cons), consistent with pre-existing seeding, not a new source of nondeterminism
- [x] Consistent with existing patterns — mirrors `Density` (settable property, safe default) and `EdgeCaseFamily` (coarse enum grouping)

## Evidence

**Root cause — every coded element is generated independently, no shared state between siblings:**
- `SchemaBasedFhirResourceFaker.cs:218-250` — the main `Generate()` loop calls `GenerateElementValue`
  once per child element with nothing carried between siblings
- `SchemaBasedFhirResourceFaker.cs:488-538` (`TryGenerateFromBinding`), pick at `:511` —
  `_faker.PickRandom(codes)` against the **full** pool for that value-set URI, independently per call
- `SchemaBasedFhirResourceFaker.cs:775-793` (`GenerateCodeableConcept`), `:799-845`
  (`GenerateCodeableReference`), `:851-871` (`GenerateCoding`) — three near-identical, independent
  binding-aware pick sites, each unaware of what any other element on the resource already picked
- `SchemaBasedFhirResourceFaker.cs:1048` (`ShouldPopulate`) —
  `child.IsRequired || Density == GenerationDensity.Maximum` confirms why the symptom is specific to
  Maximum density: that's what makes all these independent picks fire together on one resource

**A second, compounding bug — some unbound fields aren't randomly incoherent, they're identically wrong every time:**
- `SchemaBasedFhirResourceFaker.cs:877-936` (`TryGetCodeFromHeuristic`) — "condition"/"diagnosis"
  always resolves to `FhirCode.Conditions.Hypertension` (`:885`), "medication" always to
  `Ibuprofen400mg` (`:891`), "observation"/"loinc" always to `FhirCode.Observations.BodyWeight`
  (`:898`) — fixed constants, not pool picks. "procedure" (`:902-911`), "allergy" (`:913-921`), and
  "vaccine"/"immunization" (`:923-932`) do pick randomly from unrelated pools, which is where the
  user's cross-field incoherence is most visible.

**Confirmed real-world trigger, matching the user's report exactly:**
- `tools/Ignixa.FhirFakes.Cli/Commands/ResourceCommand.cs:264-267` (`HandleGenericDensity`) — the
  `ignixa-fakes resource <Type> --density maximum --out <folder>` command builds a bare
  `SchemaBasedFhirResourceFaker { Density = Maximum }` and calls `Generate(resourceType)` with no
  theme, scenario, or context input of any kind.
- `src/Core/Ignixa.TestScript.FhirFakes/FhirFakesFixtureProvider.cs:21-22` — the only other library
  consumer found in-repo uses **default (Minimal)** density, confirming the TestScript fixture path
  is not where the user's example originated and is unaffected by this change either way.

**Existing, unexploited taxonomy this design reuses directly:**
- `Scenarios/Codes/Procedures.cs` — ~30 codes already grouped by **comments only** into clinical
  specialties (General Surgery, Cardiology & Vascular, Orthopedic Surgery, Gastroenterology,
  OB/GYN, Urology, Imaging, Other)
- `Scenarios/Codes/FhirCode.cs` — `Conditions` (14), `Observations` (12), `Medications` (~40),
  `EncounterTypes` (4) pools, same comment-only grouping gap
- `Scenarios/Codes/Specialties.cs` — a fully queryable, 24-entry SNOMED CT specialty vocabulary
  (`FamilyMedicine`, `Cardiology`, `OrthopedicSurgery`, `Endocrinology`, `Gastroenterology`,
  `ObstetricsGynecology`, `Urology`, `Radiology`, ...) already exists, currently used only as a flat
  pool for one practitioner-specialty value set — never cross-referenced against
  Procedures/Conditions/Medications today.

**Architectural precedent for "coarse category + filtered pool" is already proven in this codebase:**
- `EdgeCases/EdgeCaseFamily.cs` — the `Family` enum idiom this design's `ClinicalDomain` mirrors
- `EdgeCases/EdgeCaseCatalog.cs:82-99` (`Resolve`) — selector-based filtering idiom
- `GenerationDensity.cs` — the settable-property-with-safe-default idiom `Theme` mirrors

**Cross-resource coherence already exists, but only via 100% hand-authored wiring — doesn't help the ad-hoc single-`Generate()` case the user hit:**
- `Scenarios/Predefined/DiabeticPatientScenario.cs` — manually correlates
  `Conditions.DiabetesType2` + `ObservationState.BloodGlucose()/HemoglobinA1c()` +
  `MedicationOrderState.Metformin500mg()/1000mg()`
- `Scenarios/ScenarioContext.cs:505-531` — the existing generic `Dictionary<string,object>`
  attribute bag (`SetAttribute`/`GetAttribute<T>`/`HasAttribute`) is the natural carrier if theme
  propagation is later extended into Layer 2, without inventing new plumbing.

**Regression-risk check — existing tests assert structure/membership, not exact code identity, so the blast radius is small:**
- `test/Ignixa.FhirFakes.Tests/BindingAwareGenerationTests.cs:29-77` — asserts pool **membership**
  (`codes.ShouldContain(...)`) against the 3-arg `TryGetCodesForValueSet` overload, never exact
  single-value equality; the additive 4-arg theme overload leaves this method and its tests
  untouched.
- `test/Ignixa.FhirFakes.Tests/GenerationDensityTests.cs:56-143` — Maximum-density tests assert
  structural properties (`ShouldNotBeNull`, element counts, "does not throw", "still validates"),
  never a specific expected SNOMED/LOINC code.
- `test/Ignixa.FhirFakes.Tests/GenerationDensityTests.cs:110-138` — the two
  `RealisticDensity...BehavesIdenticallyToMinimal` tests do assert exact `ToJsonString()` equality,
  but Minimal/Realistic only populate required fields, which are largely uncoded/structural, so
  they're insulated from a Maximum-only theming change — still worth an explicit regression run
  since these compare full JSON.

## Phased plan

**MVP:**
1. Add `Domain` (nullable `ClinicalDomain`) to the `FhirCode` record and the enum itself, mirroring
   `Specialties.cs` names
2. Tag the four pools the user's example exercised: `Procedures.cs`, `FhirCode.Conditions`,
   `FhirCode.Medications`, `FhirCode.Observations` — turning existing comment-groupings into real,
   queryable data
3. Add the theme-aware `BindingCodeMapper.TryGetCodesForValueSet` overload; existing 3-arg overload
   untouched
4. Add a nullable `Theme` settable property to `SchemaBasedFhirResourceFaker`, mirroring `Density`;
   resolved lazily via `_faker.PickRandom<ClinicalDomain>()` on first theme-eligible pick if unset,
   to avoid disturbing the RNG sequence for resource types with no coded elements at all
5. Route `TryGenerateFromBinding`, `GenerateCodeableConcept`, `GenerateCodeableReference`,
   `GenerateCoding` through the theme-aware overload
6. Convert `TryGetCodeFromHeuristic`'s three fixed constants into small per-domain arrays, keeping
   today's exact constants as the "no theme" / unmatched-domain default
7. Add `--theme` to the CLI `resource` command, optional, mirroring `--density`
8. Regression: `dotnet test All.sln`, particular attention to `GenerationDensityTests` and
   `BindingAwareGenerationTests`

**Phase 2:**
9. Extend tagging to the remaining pools (`Allergens`, `Immunizations`, `LabObservations`,
   `DiagnosticReports`, `ServiceRequestCodes`, `VitalSigns`)
10. Propagate the active theme into `ScenarioContext` via its existing attribute bag, so
    `Predefined` scenarios or `PatientLifecycleGenerator` can optionally share one theme across an
    entire scenario/lifecycle rather than a single resource
11. Consider theming free-text generation (`Lorem.Sentence(3)`) — lowest priority, cosmetic only

## Open decisions

1. **Taxonomy shape** — recommend a new, small `ClinicalDomain` enum (mirrors `EdgeCaseFamily`)
   rather than keying directly off `Specialties.cs`'s `FhirCode` constants: a `FhirCode` is "a
   coding to emit," not "a category to filter by," and record equality is the wrong tool for
   grouping.
2. **Default-on vs. opt-in** — recommend default-**on** once the MVP pools are tagged (an
   incoherent-by-default corpus is a worse default for a test-data tool), given the regression
   evidence above shows no test today pins an exact Maximum-density code value. Callers who want
   today's fully-independent behavior set `Theme` to a sentinel/leave it disabled via a CLI flag.
3. **Laziness of theme resolution** — recommend resolving the theme only on first theme-eligible
   pick, not unconditionally at the top of `Generate()`, to minimize RNG-sequence disruption for
   resource types with no coded elements.
4. **How far to propagate** — MVP is Layer 1 / single-resource only, matching the user's literal
   example; Layer 2/3 propagation (open decision 9-10 above) is real but deliberately deferred.

## Verdict

Viable. Tagging the existing curated pools with a `ClinicalDomain` and filtering through a new,
additive `BindingCodeMapper` overload closes the reported gap without touching Layer 2 machinery,
reuses two idioms already proven in this codebase (`Specialties.cs` vocabulary,
`EdgeCaseFamily`-style coarse grouping), and — per the regression-risk evidence gathered — is
unlikely to break pinned test expectations, since no current test asserts an exact generated code
value from Maximum-density Layer 1 generation. The real cost is curation: tagging ~100+ existing
constants by hand, which is why the MVP is deliberately scoped to only the four pools the user's
own example exercised.

## Alternatives considered (and why not)

- **Route single-resource `Generate()` through `Predefined`/`ScenarioBuilder` machinery** —
  rejected: pulls Patient/Encounter/Timeline generation into what should stay a single-resource
  call, and blurs the Layer 1/Layer 2 boundary `layered-architecture.md` treats as deliberate.
- **Post-hoc "coherence pass"** that detects a dominant theme after generation and re-rolls
  incoherent siblings (mirroring `EdgeCasePipeline`'s decorator shape) — rejected as the primary
  mechanism: "which field defines the theme" is order-dependent and ambiguous after the fact, and
  it still needs this design's tagged-pool primitive underneath it to have anything coherent to
  re-roll toward. It's a delivery-mechanism variant of this proposal, not an independent approach —
  and the two problems aren't symmetric: `EdgeCasePipeline` mutates fields that deliberately don't
  need to agree with each other, while theme coherence requires fields to agree with each other,
  which is naturally a generation-time concern.
- **Do nothing / document as a known limitation** — rejected: Maximum-density output is the sample
  data a developer eyeballs first (demos, docs, onboarding); leaving it actively misleading
  undermines trust in the tool for exactly the density setting meant to showcase it most fully.
