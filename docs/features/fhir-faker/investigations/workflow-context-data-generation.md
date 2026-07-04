# Investigation: Workflow and Context Data Generation

**Feature**: fhir-faker
**Status**: Proposed
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
4. **Make extension seams first-class.** Downstream teams should be able to register private providers, adapters, augmentors, and scenario packs without forking FhirFakes.
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
| CLI | Generates resources, scenarios, and populations across FHIR versions | Natural entry point for fixture pipelines |
| Scenario/state discovery | Merged PR #299 APIs expose discoverable scenario/state metadata and theme-consistent generation | Foundation for public scenario packs and private extension catalogs |

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
| EHR flavor quirks | Vendor-specific identifiers, extensions, coding systems, reference styles, and date precision | Profile adapter that changes systems, extensions, status values, or reference forms |
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

**Opportunity**: define flavor/profile adapters that alter generated graphs and bundles through explicit hooks.

### Fixture determinism needs bundle-level semantics

Seeds are useful, but workflow fixtures need deterministic resource IDs, page boundaries, timestamps, link URLs, and ordering. Otherwise tests cannot compare bundle output reliably.

**Opportunity**: define deterministic fixture metadata and allow timestamp normalization or fixed clocks for generated workflow bundles.

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
Resource graph augmentors
  Add Appointment/List/DocumentReference/Basic/etc.
  Add organization/practitioner/location topology
  Add private or profile-specific resources through registration
        │
        ▼
Flavor/profile adapters
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

### Scenario pack provider

Scenario packs should be discoverable and parameterized. A provider exposes generic workflow scenarios without forcing consumers to know implementation classes.

```csharp
public interface IWorkflowScenarioProvider
{
    IEnumerable<DiscoveredScenario> Discover();
    WorkflowScenario Build(string scenarioName, WorkflowScenarioOptions options);
}
```

Example built-in scenarios:

- `PractitionerPanel`
- `DailyAppointmentSchedule`
- `EncounterContext`
- `PatientList`
- `DocumentSelection`
- `PagedSearchResults`
- `MissingIncludes`
- `EhrFlavorSmokeTest`

Private downstream teams can register additional providers for proprietary workflows.

### Search response composer

The composer takes a resource graph and emits a specific FHIR response shape. It owns response-level details rather than mixing them into clinical states.

```csharp
public interface ISearchResponseComposer
{
    Bundle Compose(ResourceGraph graph, SearchResponseOptions options);
}
```

Options should cover:

- FHIR version.
- Search URL and query parameters.
- Bundle type: `searchset`, `batch-response`, or `transaction-response`.
- Page size and page count.
- Include/revInclude policy.
- Link policy for `self`, `next`, `previous`, and `related`.
- Entry ordering.
- Duplicate, stale, missing, or unrelated include behavior.

### Resource graph augmentor

Augmentors add workflow resources to an existing clinical graph. They are the safest place for custom resources, private profiles, and resource relationships that are not part of the core clinical state machine.

```csharp
public interface IResourceGraphAugmentor
{
    ResourceGraph Augment(ResourceGraph graph, ResourceGraphAugmentationContext context);
}
```

Built-in augmentors could add:

- Appointments around encounters.
- Lists or groups for patient cohorts.
- DocumentReferences for encounters and patients.
- Basic refresh markers.
- PractitionerRole, Organization, Location, HealthcareService, and affiliation networks.

### Flavor/profile adapter

Adapters alter generated resources and bundles to match a known style without changing scenario logic.

```csharp
public interface IEhrFlavorProfile
{
    string Name { get; }
    void Apply(ResourceGraph graph, FlavorProfileContext context);
    void Apply(Bundle bundle, FlavorProfileContext context);
}
```

Examples:

- Identifier system selection and MRN formats.
- Extension URLs and value conventions.
- Reference style: absolute, relative, logical, or `urn:uuid`.
- Date and dateTime precision.
- Status/code preferences.
- Include completeness and duplication patterns.

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
- Flavor/profile adapter name.
- Edge-case mutation manifest when decorators are used.
- Validation results and known intentional-invalid markers.

## CLI Shape

The CLI should keep current resource/scenario/population commands and add workflow-oriented entry points only when the library seams exist.

Possible shape:

```text
ignixa-fakes r4 workflow PractitionerPanel --count 25 --practitioners 2 --out ./fixtures
ignixa-fakes r4 workflow DailyAppointmentSchedule --date 2026-07-04 --theme cardiology --out ./fixtures
ignixa-fakes r4 workflow DocumentSelection --patient-count 10 --paged --out ./fixtures
ignixa-fakes r4 workflow PagedSearchResults --resource Patient --pages 3 --page-size 20 --out ./fixtures
```

Options:

- `--theme`
- `--seed`
- `--clock`
- `--profile`
- `--flavor`
- `--paged`
- `--page-size`
- `--include-policy complete|missing|duplicate|stale|mixed`
- `--resolved-references`
- `--ndjson`
- `--validate`
- `--edge-cases`

## Implementation Phasing

### Phase 1: Investigation and contracts

- Document public workflow fixture categories.
- Define resource graph, composer, augmentor, flavor/profile, and manifest contracts.
- Identify which merged scenario/state discovery APIs can be reused directly.

### Phase 2: High-value workflow builders

- Add dedicated builders/states for `Appointment`, `List`, `DocumentReference`, and `Basic` metadata markers.
- Add organization/practitioner/location topology helpers.
- Add deterministic ID and clock options for workflow fixtures.

### Phase 3: Search response composition

- Add searchset bundle composition.
- Add include/revInclude policies.
- Add paging and related-link generation.
- Add request/response paired fixture output.

### Phase 4: Built-in scenario packs

- Practitioner panel.
- Daily appointment schedule.
- Encounter context.
- Patient list.
- Document context.
- Paged search results.
- Missing/duplicate/stale include variants.

### Phase 5: Extension package pattern

- Document how downstream teams register private workflow providers, graph augmentors, and flavor profiles.
- Add sample extension package or test-only provider.
- Add CLI discovery output for workflow scenarios and supported parameters.

## Non-Goals

- Do not model private downstream services in the public repository.
- Do not make `ScenarioBuilder` handle multi-patient workflow orchestration directly; keep it patient-centric.
- Do not require every workflow fixture to be clinically exhaustive.
- Do not replace schema-based generation; use it as fallback behind higher-value dedicated builders.
- Do not guarantee vendor conformance without explicit profile/flavor adapters and validation.

## Recommended Next Step

Start with the contracts and the smallest useful built-in scenario pack: **DailyAppointmentSchedule**. It exercises multi-resource graph augmentation, appointment-specific states, search response composition, paging/link metadata, practitioner/patient/encounter relationships, and flavor/profile hooks without requiring a full cohort modeling system first.

The second scenario pack should be **PractitionerPanel**, because it establishes multi-patient cohort composition and provides reusable input for schedules, patient lists, and document-context fixtures.
