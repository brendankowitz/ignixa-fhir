# Investigation: Workflow and Context Data Generation

**Feature**: fhir-faker
**Status**: MVP Implemented (DailyAppointmentSchedule pack; PractitionerPanel and later phases remain proposed)
**Created**: 2026-07-04
**Related**: Merged scenario/state discovery and theme-consistent generation APIs from PR #299

---

## Summary

FhirFakes already generates useful clinical resources, patient-centric scenarios, populations, and edge-case mutations. The next gap is **workflow-shaped data**: datasets that look like the FHIR responses consumed by context retrieval, assistant, chart review, patient panel, scheduling, and document-selection pipelines.

Those consumers usually need more than valid resources. They need coherent resource graphs, searchset bundles, includes, reverse includes, paging links, refresh markers, profile-specific identifiers, EHR-flavored quirks, and partial-data cases. FhirFakes should support those needs through generic scenario packs and extension seams, not by baking private downstream assumptions into the core library.

## Problem Statement

The current faker stack is strongest when the target is:

1. A standalone FHIR resource.
2. A single-patient clinical scenario.
3. A realistic patient population.
4. A validity-preserving or intentionally invalid edge-case corpus.

Many downstream systems consume a different shape: **FHIR search responses that carry workflow context**. They test whether a consumer can answer questions like:

- Which patients are on this practitioner's panel?
- What appointments does this practitioner have today, and which patient/encounter data came along with each appointment?
- Which documents are selectable for a patient or encounter?
- Did the response include a refresh marker or synchronization cursor?
- Can the consumer follow `next` or `related` links correctly?
- What happens when includes are missing, duplicated, stale, or vendor-specific?

Generating only valid individual resources does not exercise those paths. Generating only patient-centric transaction bundles does not exercise search response composition, paging, related links, partial includes, or EHR-specific metadata.

## Design Principles

1. **Keep FhirFakes public and generic.** Do not encode internal service names, private endpoint assumptions, or consumer-specific models.
2. **Separate clinical realism from workflow realism.** Clinical states create plausible medical history; workflow composers shape that history into the FHIR response forms downstream systems ingest.
3. **Prefer discoverable scenario packs over hardcoded methods.** Build on scenario/state discovery, metadata, domains, parameters, and theme-consistent generation.
4. **Make extension seams first-class.** Downstream teams should be able to register private scenario packs, enrichers, and flavor adapters without forking FhirFakes.
5. **Measure output shape.** Fixture generation should report resource counts, bundle links, included resource coverage, validation results, and deterministic seed metadata.

## Current Strengths

FhirFakes already has several pieces needed for richer workflow fixtures:

| Area | Existing capability | Relevance |
|------|---------------------|-----------|
| Schema-based generation | Generates arbitrary FHIR resource types from schema with density control | Useful fallback for resources without dedicated builders |
| Patient builders | Rich demographics, identifiers, addresses, telecom, profiles, tags, extensions, and edge cases | Foundation for patient panels and context fixtures |
| Scenario builder | Creates patient-centered longitudinal graphs across encounters, conditions, observations, medications, diagnostic reports, procedures, immunizations, allergies, care plans, teams, practitioners, organizations, coverages, goals, and timelines | Foundation for encounter and chart-review context |
| Population generator | Creates geography-aware populations and CLI-exportable bundles/NDJSON | Foundation for practitioner panels and multi-patient cohorts |
| Edge-case pipeline | Produces valid-hostile and optionally invalid data with mutation manifests | Foundation for robustness fixtures |
| CLI | Generates resources, scenarios, and populations across FHIR versions (`ignixa-fakes {stu3\|r4\|r4b\|r5\|r6} {resource\|scenario\|population}`) | Natural entry point for fixture pipelines |
| Scenario/state discovery | `ScenarioCatalog` / `ObservationStateCatalog` (merged PR #299): attribute-driven reflection discovery of static factory methods, with `DiscoveredScenario`/`DiscoveredScenarioParameter` metadata, `Find`/`Invoke`, typed parameter overrides with Min/Max validation, and CLI `--param name=value` binding | Foundation for public scenario packs and private extension catalogs — the workflow discovery model below must extend this, not duplicate it |

## Data Categories for Context Consumers

The extension model should describe needs by reusable fixture category instead of by downstream project.

| Category | What it exercises | Example fixture shape |
|----------|-------------------|-----------------------|
| Longitudinal clinical context | Patient summary, chart review, condition progression, medication reconciliation | Patient + Encounters + Conditions + Observations + Medications + DiagnosticReports |
| Practitioner panels | Patient list membership, attribution, organization/practitioner relationships | Practitioner + Organization + Patient cohort + List/Group/CareTeam/Coverage |
| Schedules and appointments | Calendar context, appointment participants, encounter linkage, patient enrichment | Appointment searchset with included Patient, Practitioner, Location, Encounter |
| Encounter-centric context | Workflows that start from visits rather than patients | Encounter searchset with subject Patient, participants, diagnoses, observations |
| Document context | Document selection, note retrieval, descriptions, attachment metadata | DocumentReference searchset with Patient/Encounter references and optional Binary/Composition links |
| Refresh and synchronization markers | Incremental refresh, cursor extraction, metadata-only resources | Basic or OperationOutcome-like marker resources with extensions and timestamps |
| Pagination and related links | Client navigation across search pages and auxiliary queries | Searchset bundle with `self`, `next`, `previous`, and `related` links |
| Include/revInclude coverage | Consumer handling of present, missing, duplicate, stale, or unrelated included resources | Searchset bundle with controlled include completeness |
| EHR flavor quirks | Vendor-specific identifiers, extensions, coding systems, reference styles, and date precision | Flavor adapter that changes systems, extensions, status values, or reference forms |
| Temporal and identifier edge cases | Timezone, partial precision, MRN collisions, multi-system identifiers | Seeded edge-case decorators layered on workflow fixtures |

## Gaps and Opportunities

### Workflow-shaped bundles are not first-class

`ScenarioContext` can emit transaction-style and resolved-reference bundles, but downstream context consumers often ingest FHIR searchset or transaction-response-like bundles. Those bundles need `Bundle.link`, `Bundle.entry.search`, include/revInclude behavior, page boundaries, and stable `self` URLs.

**Opportunity**: add a `SearchResponseComposer` layer that wraps generated resource graphs into FHIR search responses.

### Multi-patient panels need a higher-level model

`ScenarioBuilder` is intentionally patient-centric. That is a good boundary for clinical scenarios, but practitioner panels, appointment schedules, and document worklists are cohort problems.

**Opportunity**: add cohort/workflow scenario packs that compose multiple patient scenarios under a practitioner, organization, location, or care-team context.

### Workflow resources need dedicated states/builders

Clinical states cover many patient history resources, but workflow fixtures need stronger support for resources such as `Appointment`, `List`, `DocumentReference`, `Basic`, `Group`, `PractitionerRole`, `OrganizationAffiliation`, `Location`, and `HealthcareService`.

**Opportunity**: add dedicated builders/states for high-value workflow resources, using schema generation only as fallback.

### EHR-specific variation should be injectable

Different FHIR-backed systems vary in identifier systems, extension URLs, coding systems, reference formats, date precision, included resources, and metadata conventions. FhirFakes should not hardcode those variants, but it should make them easy to add.

**Opportunity**: define flavor adapters that alter generated graphs and bundles through explicit hooks.

### Fixture determinism needs bundle-level semantics

Seeds are useful, but workflow fixtures need deterministic resource IDs, page boundaries, timestamps, link URLs, and ordering. Otherwise tests cannot compare bundle output reliably.

**Opportunity**: make determinism a concrete contract, not an aspiration. Three mechanisms, all recorded in the manifest:

1. **Seed** — reuse the existing seeded `Faker`/`Random` plumbing (`SchemaBasedFhirResourceFaker(schemaProvider, seed)`, `PatientBuilder.WithSeed`). The same caveat from theme-consistent-generation applies: reproducibility holds for a fixed library version and option set; adding a draw to the sequence shifts output for a pinned seed.
2. **Clock** — a `TimeProvider` (or fixed `DateTimeOffset`) supplied through the workflow options, so `meta.lastUpdated`, appointment times, and page timestamps derive from a fixed instant instead of `DateTime.Now`.
3. **ID strategy** — resource IDs derived from the seeded RNG (or a sequential counter scoped to the generation run), never `Guid.NewGuid()`, so entry `fullUrl`s and `next`-link continuation tokens are stable across runs.

Entry ordering and page boundaries must be a pure function of (seed, options); a test asserting the exact JSON of page 2 of 3 must pass on every run.

## Proposed Extensibility Model

The recommended model is layered. Each layer has one responsibility and can be used independently.

```text
Clinical/resource generation
  SchemaBasedFhirResourceFaker
  PatientBuilder
  ScenarioBuilder
  PopulationGenerator
        │
        ▼
Workflow scenario packs
  Practitioner panel
  Schedule
  Encounter context
  Patient list
  Document context
        │
        ▼
Resource graph enrichers
  Add Appointment/List/DocumentReference/Basic/etc.
  Add organization/practitioner/location topology
  Add private or profile-specific resources through registration
        │
        ▼
Flavor adapters
  Identifier systems
  Extension URLs
  Reference formats
  Date/time precision
  Missing-data behavior
        │
        ▼
Search response composers
  Searchset bundles
  Includes/revIncludes
  Paging and related links
  Transaction response fixtures
        │
        ▼
Validation and manifest output
  Resource validation
  Bundle shape checks
  Counts and coverage
  Seed and clock metadata
```

### Scenario pack discovery — extend `ScenarioCatalog`, don't parallel it

The repository already has a merged, tested discovery model: `ScenarioCatalog` finds attribute-annotated public static factory methods by reflection, exposes `DiscoveredScenario`/`DiscoveredScenarioParameter` metadata (including Min/Max validation and string parsing for CLI/form values), and invokes them with typed parameter overrides. An earlier draft of this proposal sketched a fresh `IWorkflowScenarioProvider { Discover(); Build(name, options); }` interface — rejected: it duplicates the catalog's discovery, metadata, and parameter-binding responsibilities with a second, stringly-typed lookup, and every consumer (CLI, UIs, downstream teams) would have to learn two discovery models.

Instead, workflow scenario packs follow the same convention with a different return type:

```csharp
// A workflow pack is a static factory method, discovered by attribute + signature,
// exactly like clinical scenarios — but it returns a workflow result, not a ScenarioContext.
public static class DailyAppointmentScheduleScenario
{
    [WorkflowScenario(Id = "DailyAppointmentSchedule",
        Description = "Practitioner day schedule with appointment searchset and included context")]
    public static WorkflowScenarioResult GetDailyAppointmentSchedule(
        IFhirSchemaProvider schemaProvider,
        WorkflowScenarioOptions options,
        [ScenarioParameter(Min = 1, Max = 10)] int practitionerCount = 1,
        [ScenarioParameter(Min = 0, Max = 50)] int appointmentCount = 12) => ...;
}

public static class WorkflowScenarioCatalog
{
    public static IReadOnlyList<DiscoveredScenario> GetAll();
    public static DiscoveredScenario? Find(string id);
    public static WorkflowScenarioResult Invoke(
        DiscoveredScenario scenario,
        IFhirSchemaProvider schemaProvider,
        WorkflowScenarioOptions options,
        IReadOnlyDictionary<string, object?>? parameterOverrides = null);

    // The genuinely new capability: ScenarioCatalog only scans its own assembly today.
    // Registration is the extension seam for private downstream packs.
    public static void RegisterAssembly(Assembly assembly);
}
```

`WorkflowScenarioResult` (sealed) carries the composed graph, the emitted bundles, and the manifest described under Output Contracts. `WorkflowScenarioOptions` (sealed record, `init` properties) carries the cross-cutting knobs — seed, clock, ID strategy, theme, flavor — while pack-specific knobs stay as factory-method parameters so they surface through discovery metadata. `DiscoveredScenario`/`DiscoveredScenarioParameter` and the `--param name=value` CLI binding are reused as-is.

Design notes:

- All members stay synchronous. Generation is in-memory throughout FhirFakes; file and network I/O belongs to the CLI (or the caller), which is where `async`/`CancellationToken` already live.
- `RegisterAssembly` must be thread-safe (registration at startup, lock-free reads afterward — the existing `Lazy` pattern in `ScenarioCatalog` needs rework to admit late registration; alternatively, registration is only honored before first enumeration and throws afterward, which is simpler and probably sufficient).
- **Open question (human decision)**: generalize `ScenarioCatalog` itself to discover both return types, or add the sibling `WorkflowScenarioCatalog` shown above. A sibling keeps the merged, published `ScenarioCatalog` API untouched (it is on a stable package — see Versioning below); generalizing avoids a second static catalog. This document recommends the sibling but does not decide.

Candidate built-in packs (only the first two are committed by the Recommended Next Step; the rest are backlog until a consumer asks):

- `DailyAppointmentSchedule`
- `PractitionerPanel`
- `EncounterContext`
- `PatientList`
- `DocumentSelection`
- `PagedSearchResults`
- `MissingIncludes`

### Search response composer

The composer takes a resource graph and emits a specific FHIR response shape. It owns response-level details rather than mixing them into clinical states. Note the return type: FhirFakes has no POCO `Bundle` model — `ScenarioContext.ToBundle()`/`ToBatchBundle()` already return `BundleJsonNode`, and the composer emits the same type. Paged output is a list of pages, never null (empty result set → one empty searchset page).

```csharp
public interface ISearchResponseComposer
{
    IReadOnlyList<BundleJsonNode> Compose(ResourceGraph graph, SearchResponseOptions options);
}
```

`SearchResponseOptions` is a sealed record with `init` properties; behavioral choices are enums, not booleans:

```csharp
public sealed record SearchResponseOptions
{
    public required string SearchUrl { get; init; }
    public ResponseBundleType BundleType { get; init; } = ResponseBundleType.Searchset;
    public int PageSize { get; init; } = 20;
    public IncludeCompleteness IncludeCompleteness { get; init; } = IncludeCompleteness.Complete;
    // seed / clock / id-strategy per the determinism contract above
}

public enum ResponseBundleType { Searchset, BatchResponse, TransactionResponse }
public enum IncludeCompleteness { Complete, Missing, Duplicate, Stale, Unrelated, Mixed }
```

Options also cover link policy for `self`, `next`, `previous`, and `related`, entry ordering, and revInclude selection — same enum-over-bool discipline.

### Resource graph enricher

Enrichers add workflow resources to an existing clinical graph. They are the safest place for custom resources, private profiles, and resource relationships that are not part of the core clinical state machine.

`ResourceGraph` does not replace `ScenarioContext` — it aggregates the outputs of one or more patient-centric `ScenarioContext`s (plus non-patient workflow resources) into a single cross-patient registry, keeping `ScenarioBuilder`'s one-scenario-one-patient boundary intact. Consistent with the codebase's mutable-JSON-node idiom, enrichers mutate the graph in place rather than returning a copy:

```csharp
public interface IResourceGraphEnricher
{
    void Enrich(ResourceGraph graph, ResourceGraphEnrichmentContext context);
}
```

Enrichers must be stateless: all per-run state (RNG, clock, accumulated resources) lives on the graph or the context, so a single enricher instance can be registered once and reused across concurrent generations.

Built-in enrichers could add:

- Appointments around encounters.
- Lists or groups for patient cohorts.
- DocumentReferences for encounters and patients.
- Basic refresh markers.
- PractitionerRole, Organization, Location, HealthcareService, and affiliation networks.

### Flavor adapter

Adapters alter generated resources and bundles to match a known vendor style without changing scenario logic. Naming note: this document deliberately says **flavor**, not "profile" — in FHIR, "profile" means a StructureDefinition conformance profile, and overloading it for vendor quirks would mislead exactly the healthcare developers this library targets. Reserve profile/`meta.profile` language for actual conformance claims.

```csharp
public interface IEhrFlavorAdapter
{
    string Name { get; }
    void Apply(ResourceGraph graph, FlavorContext context);
    void Apply(BundleJsonNode bundle, FlavorContext context);
}
```

Like enrichers, adapters are stateless and mutate in place; the same instance is reused across generations.

Examples:

- Identifier system selection and MRN formats.
- Extension URLs and value conventions.
- Reference style: absolute, relative, logical, or `urn:uuid`.
- Date and dateTime precision.
- Status/code preferences.
- Include completeness and duplication patterns.

### Registration model

FhirFakes is a plain library with no DI container — builders take `IFhirSchemaProvider` through a single constructor, and the existing extensibility precedent is explicit catalog composition (`EdgeCaseCatalog.CreateDefault()` plus registration), not `IServiceCollection`. Workflow extensibility follows the same pattern:

- Workflow packs: `WorkflowScenarioCatalog.RegisterAssembly(assembly)` (attribute discovery, as above).
- Enrichers and flavor adapters: an explicit, instance-based catalog mirroring `EdgeCaseCatalog` — `WorkflowCatalog.CreateDefault()` returns the built-ins; consumers add their own instances. No static mutable registry for these; a consumer that wants DI wraps the catalog in its own container.

Because registered enrichers/adapters are held once and reused across generations, statelessness (per the notes above) is a contract requirement, not a suggestion — the documentation for each seam must say so.

## Proposed Generic Scenario Packs

### Practitioner panel

Creates a practitioner- or organization-scoped cohort with multiple patient scenarios. Useful for testing patient list views, panel summaries, population filters, and organization/practitioner attribution.

Suggested resources:

- Practitioner
- PractitionerRole
- Organization
- Location
- HealthcareService
- Patient cohort
- List or Group
- CareTeam or Coverage when relevant
- Optional clinical summaries per patient

### Daily appointment schedule

Creates one or more practitioners with appointments across a day or date range. Each appointment can include patient, encounter, location, and selected clinical context.

Suggested resources:

- Appointment
- Patient
- Practitioner and PractitionerRole
- Encounter
- Location
- Organization
- Basic refresh marker or metadata resource

Useful variants:

- Appointments with and without linked encounters.
- Patient participant present but included Patient missing.
- Encounter present only through reverse include.
- Cancelled/no-show/proposed/fulfilled appointment statuses.
- Timezone and daylight-saving boundary cases.

### Encounter context

Starts from encounters and adds subject, participants, diagnoses, observations, procedures, medications, and documents.

Useful variants:

- Active encounter.
- Recently discharged encounter.
- Historical encounter.
- Encounter with multiple diagnoses.
- Encounter with missing subject include.
- Encounter with duplicate patient include.

### Patient list

Models workflows that use FHIR `List`, `Group`, or `Encounter` search results to identify patients.

Useful variants:

- List entries reference Encounter resources.
- List entries reference Patient resources directly.
- Encounter.subject resolves to Patient.
- Empty list.
- List with inactive/deleted-like entries.
- List with stale references.

### Document context

Generates selectable documents for a patient or encounter.

Suggested resources:

- DocumentReference
- Patient
- Encounter
- Practitioner
- Organization
- Optional Composition or Binary

Useful variants:

- Missing description/title.
- Multiple content attachments.
- Unsupported content types.
- Same document linked to multiple encounters.
- Documents outside the requested date range.

### Paged and linked search responses

Creates bundles where the main value is response shape, not clinical content.

Useful variants:

- Single page.
- Multiple pages with stable `next` links.
- `related` links for auxiliary queries.
- Empty page with total count.
- Page with duplicate or stale included resources.
- Response bundle for a submitted batch/transaction search request.

### Edge-case workflow corpus

Composes workflow scenarios with the edge-case pipeline so context consumers see hostile-but-valid data in realistic response shapes.

Useful variants:

- Unicode names in panel and schedule data.
- Partial birth dates.
- Extreme dateTime offsets.
- Identifier collisions across systems.
- Very long document descriptions.
- Missing optional fields that consumers commonly assume are present.

## Output Contracts

Workflow fixtures should emit both data and manifest metadata.

### Data outputs

- FHIR bundle JSON.
- Optional NDJSON split by resource type for bulk import.
- Optional paired request bundle and response bundle.
- Optional one-file-per-page output for paged search fixtures.

### Manifest outputs

- Scenario name and parameters.
- Seed and clock settings.
- Resource counts by type.
- Bundle link summary.
- Include/revInclude coverage.
- Flavor adapter name.
- Edge-case mutation manifest when decorators are used.
- Validation results and known intentional-invalid markers.

## CLI Shape

The CLI should keep current resource/scenario/population commands and add workflow-oriented entry points only when the library seams exist. The command nests under the existing FHIR-version commands (`ignixa-fakes {stu3|r4|r4b|r5|r6} workflow ...`), and — following the existing `scenario` command — pack-specific parameters use the generic, repeatable `--param name=value` convention bound through `DiscoveredScenarioParameter.TryParseValue`, not bespoke per-pack flags. An unknown workflow name lists available packs and exits with code 2, mirroring `ScenarioCommand`.

Possible shape:

```text
ignixa-fakes r4 workflow PractitionerPanel --param patientCount=25 --param practitionerCount=2 --out ./fixtures
ignixa-fakes r4 workflow DailyAppointmentSchedule --param date=2026-07-04 --theme cardiology --out ./fixtures
ignixa-fakes r4 workflow DocumentSelection --param patientCount=10 --page-size 20 --out ./fixtures
ignixa-fakes r4 workflow PagedSearchResults --param resource=Patient --param pages=3 --page-size 20 --out ./fixtures
```

Cross-cutting options (shared across packs, not `--param`-bound):

- `--theme` (existing `ClinicalDomain` theming)
- `--seed`
- `--clock` (fixed instant for deterministic timestamps)
- `--flavor`
- `--page-size` (presence implies paged output)
- `--include-policy complete|missing|duplicate|stale|unrelated|mixed`
- `--resolved-references`
- `--ndjson`
- `--validate`
- `--edge-cases`

## Implementation Phasing

### Phase 1: Investigation and contracts

- Document public workflow fixture categories. **Done** (this document).
- Define resource graph, composer, enricher, and manifest contracts. **Shipped** via `docs/superpowers/plans/2026-07-04-fhir-fakes-workflow-context.md`: `ResourceGraph`, `IResourceGraphEnricher`, `ISearchResponseComposer`, `WorkflowManifest`. **Not shipped**: a flavor adapter contract (`IEhrFlavorAdapter`) — no flavor adapter type exists yet; flavor adapters remain proposed only (see Phase 4/Recommended Next Step, which already deferred them past the first pack). The shipped contracts are public from the start, not staged `internal`-then-promoted — this repo is pre-v1, so the internal-staging idea below was superseded by shipping the real public surface directly.
- Resolve the open discovery question (generalize `ScenarioCatalog` vs sibling `WorkflowScenarioCatalog`) and confirm `DiscoveredScenario`/`DiscoveredScenarioParameter` reuse. **Shipped**: resolved as a sibling catalog (`WorkflowScenarioCatalog`) sharing a newly-extracted `ScenarioParameterBinder` with `ScenarioCatalog`, per the discussion that closed this open question.

### Phase 2: High-value workflow builders

- **Shipped** (via the plan above): enricher support for `Appointment` (`AppointmentSchedulingEnricher`), reusing the existing `PractitionerState` for practitioner generation rather than adding a new builder.
- **Not implemented, remains proposed**: dedicated builders/states for `List`, `DocumentReference`, and `Basic` metadata markers.
- **Not implemented, remains proposed**: organization/location topology helpers — the shipped pack reuses the existing `PractitionerState` directly; no new Organization or Location resource support was added.
- **Partially shipped**: clock options (`WorkflowScenarioOptions.Clock`, a `TimeProvider`) shipped. Deterministic resource IDs did **not** ship — `AppointmentSchedulingEnricher` and every existing `ScenarioState` still assign IDs via `Guid.NewGuid()`; seed-reproducibility covers Bogus-driven value picks only, not byte-identical bundle output (see this document's Fixture determinism discussion above — that gap was scoped out of the shipped plan deliberately, not an oversight).

### Phase 3: Search response composition

- **Shipped** via the plan above: searchset bundle composition (`SearchsetBundleComposer`), paging, and `self`/`next`/`previous` link generation.
- **Partially shipped**: include completeness has two modes, `Complete` and `Missing` (the two variants the DailyAppointmentSchedule pack's own "useful variants" called for) — `Duplicate`/`Stale`/`Unrelated`/`Mixed` were deliberately descoped, not implemented.
- **Not implemented, remains proposed**: `related` links, and request/response paired fixture output.

### Phase 4: Built-in scenario packs

- **Shipped** via the plan above: daily appointment schedule (the `DailyAppointmentSchedule` pack), reachable via `ignixa-fakes {version} workflow DailyAppointmentSchedule`.
- Practitioner panel — not implemented, remains proposed.
- Remaining candidates (encounter context, patient list, document context, paged search results, include variants) only as consumers materialize — not implemented, remain proposed.

### Phase 5: Extension package pattern

- Document how downstream teams register private workflow packs, graph enrichers, and flavor adapters.
- Add a sample extension package or test-only pack exercising `RegisterAssembly`.
- Add CLI discovery output for workflow scenarios and supported parameters.
- Promote the Phase 1 contracts to public once the built-in packs have exercised them.

## Testing Strategy

Standard repo conventions apply (AAA with Shouldly, `GivenContext_WhenAction_ThenResult` naming, no `#region`), plus workflow-specific expectations:

- **Determinism tests are the contract tests.** With a fixed seed, clock, and ID strategy, a composed searchset page serializes to byte-identical JSON on every run — assert exact output for at least one paged fixture per pack, the same way `RealisticDensity...BehavesIdenticallyToMinimal` pins full JSON today.
- **Catalog tests** mirror `ScenarioCatalogTests`: discovery finds annotated packs, `Find` is case-insensitive, parameter overrides validate Min/Max, and `RegisterAssembly` surfaces packs from a test-only assembly.
- **Composer shape tests** assert `Bundle.link` correctness across pages (`self`/`next`/`previous` chain closes), `entry.search.mode` assignment (`match` vs `include`), and each `IncludeCompleteness` mode's observable effect.
- **Validation**: generated workflow fixtures pass schema validation by default; intentionally degraded fixtures (missing/stale includes) still validate structurally — the degradation is semantic, not syntactic — and are marked in the manifest.

## Versioning and Compatibility

`Ignixa.FhirFakes` publishes to NuGet.org as a **stable** package (`<PackageStability>stable</PackageStability>`, ADR 2606), so every public type this proposal adds is semver-committed the moment it ships. Consequences:

- Phase 1 contracts start `internal` (exercised by built-in packs and tests via the existing `InternalsVisibleTo`) and go public in Phase 5, after the built-in packs have proven the shapes. Widening `internal` → `public` is additive; the reverse is a major-version break.
- Prefer additive evolution idioms already used in this package: `init`-only properties on options records (see the `FhirCode.Domain` precedent in theme-consistent-generation — positional record parameters are binary-breaking), new overloads over signature changes, and new enum members appended rather than reordered.
- Interfaces are the least evolvable public surface (adding a member breaks external implementors). For seams downstream teams implement (`IResourceGraphEnricher`, `IEhrFlavorAdapter`), keep them minimal — one or two members — and put anything likely to grow on the context parameter instead.

## Open Decisions

1. **Discovery mechanism** — generalize `ScenarioCatalog` to discover workflow factory methods too, or add the sibling `WorkflowScenarioCatalog` sketched above. Recommendation: sibling catalog (leaves the published `ScenarioCatalog` API untouched); not decided.
2. **Late registration semantics** — whether `RegisterAssembly` is allowed after first enumeration (requires reworking the `Lazy` discovery pattern) or throws once the catalog is materialized (simpler, likely sufficient for startup-time registration).
3. **`ResourceGraph` ownership** — new type aggregating `ScenarioContext` outputs (recommended above), or grow `ScenarioContext` itself. Growing `ScenarioContext` was rejected in the body because it breaks the one-scenario-one-patient boundary, but the aggregation type's exact shape (registry reuse, reference-rewrite interaction) needs a design pass in Phase 1.
4. **Flavor adapter timing** — the seam is defined here for completeness but neither committed pack needs it; decide during Phase 4 whether any built-in flavor ships or the seam stays extension-only.

## Non-Goals

- Do not model private downstream services in the public repository.
- Do not make `ScenarioBuilder` handle multi-patient workflow orchestration directly; keep it patient-centric.
- Do not require every workflow fixture to be clinically exhaustive.
- Do not replace schema-based generation; use it as fallback behind higher-value dedicated builders.
- Do not guarantee vendor conformance without explicit flavor adapters and validation.
- Do not introduce async APIs or I/O into the core library; generation stays synchronous and in-memory, and file output remains a CLI/caller concern.

## Recommended Next Step

Start with the contracts and the smallest useful built-in scenario pack: **DailyAppointmentSchedule**. It exercises multi-resource graph enrichment, appointment-specific states, search response composition, paging/link metadata, and practitioner/patient/encounter relationships without requiring a full cohort modeling system first. Flavor adapters can be deferred past both initial packs — nothing in either pack requires them, and the seam is cheap to add later.

The second scenario pack should be **PractitionerPanel**, because it establishes multi-patient cohort composition and provides reusable input for schedules, patient lists, and document-context fixtures.
