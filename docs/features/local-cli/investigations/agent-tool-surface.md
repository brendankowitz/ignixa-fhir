# Investigation: Agent Tool Surface for a Folder of FHIR Data

**Feature**: local-cli
**Status**: In Progress
**Created**: 2026-10-15

## Approach

Define the capabilities the local tool exposes to agents, identically over MCP (stdio) and the CLI, on top of the folder catalog and in-memory index described in [local-cli-mcp-fhir-server](local-cli-mcp-fhir-server.md).

The shape follows a "patient context index" idea from a separate PatientIQ proposal (not in this repo). An agent should be able to orient itself in a folder, and in a patient, in two or three calls. It navigates a deterministic tree of facts and answers from cited source resources. The parts of that proposal that need a server, an EHR, or an LLM (triggers, live fetch, read-time authorization, generated summaries) are out of scope. On a local folder the calling agent is already the model, and the folder is the only source.

Design principles:

1. **Deterministic by default.** Every v1 capability is computed without an LLM; output is reproducible for the same folder and index.
2. **Answers cite leaves.** Facts are always returned with the resources they came from, collected by the tool, not written by the model.
3. **Truncation and absence are explicit.** Nothing is silently dropped; "not found" says what was checked.
4. **Read-only over the source folder.** Capabilities that produce data write only to a separate output location.
5. **One contract, two front doors.** CLI verbs and MCP tools share names, parameters and the response envelope. The CLI adds `--format json|ndjson|table`.

## Prototype stance

The first build (working name **cli-on-fhir**) is a prototype whose purpose is to learn which capabilities agents actually use and which make answers more accurate. While it is a prototype:

- **Duplication is acceptable.** It may copy code from `Ignixa.Application` (for example the IPS generator, the terminology importers' hierarchy flattening, search option building) and from fhir-candle (search extensions) instead of refactoring shared abstractions first. Copied code is marked with its origin so it can be reconciled later.
- **Upstreaming waits for evidence.** The search-extension upstreaming and the folder adapter to Domain interfaces happen only for capabilities the prototype shows are worth keeping.
- **The non-negotiables still apply**: no success-shaped fallbacks, no silent truncation, cited evidence, read-only source folder, and no licensed-content downloads.
- **Usage is measured.** An opt-in local log records each tool call (tool, arguments shape, response size, `omitted` count, latency), never resource content. Together with a small fixed question set per folder (lookup, explain, filter/count, absence, terminology), it answers "what is useful" with numbers rather than impressions.

## Capabilities

Tools are grouped by what the agent is trying to do. "Backing" names the existing library that does the work; "Gap" is what must be built.

### Orient

| Tool / CLI verb | Purpose | Key inputs | Backing | Gap |
|-----------------|---------|-----------|---------|-----|
| `folder_inspect` / `cli-on-fhir inspect` (prototype: implemented) | Corpus overview: resource types and counts, patients, date span, FHIR version hints, `meta.profile` in use, search parameters actually populated, and diagnostics (parse errors, duplicate ids, broken references, resources without ids) | `path` | Catalog | Catalog and diagnostics (new) |
| `folder_code_systems` / `cli-on-fhir systems` (prototype: implemented) | Which coding systems the data uses (SNOMED CT, LOINC, RxNorm, ICD, HL7 v2/v3, FHIR-defined, ...): per system a well-known name, distinct codes, codings, resources, codings per element and the most frequent codes; FHIR `code` elements (gender, status) listed separately; hints for non-preferred identifiers (OIDs, aliases), FHIR look-alike URIs and codings without a system; resolution counts only when terminology is already loaded | `count`, `offset`, `top`, `withTerminology` | Catalog, a JSON walk of every Coding (no index, no terminology, no network); well-known table from FHIR R4 terminologies-systems.html and the THO NamingSystems | Inventory and well-known table (new) |
| `patient_list` / `cli-on-fhir patients` | Patients with resource counts, date spans and populated domains, so the agent can choose one | `limit`, `sort` | Catalog + Patient compartment (`Ignixa.Search/Generated/*CompartmentDefinitions.g.cs`) | Compartment membership over the index (new) |

### Navigate one patient

| Tool / CLI verb | Purpose | Key inputs | Backing | Gap |
|-----------------|---------|-----------|---------|-----|
| `context_get_root` / `cli-on-fhir context <patient>` | Root digest: demographics, active problems, active medications, allergies (with absence facts), recent encounters, key lab latest values and trends; one stub per domain | `patient` | Compartment, FHIRPath, sort | Domain builders (new) |
| `context_expand` / `cli-on-fhir context <patient> --node <key>` | One node: problem cluster with linked medications and evidence, lab series for one code over time, encounter with its contents, document sections | `nodeKey`, `limit` | Reference index entries, FHIRPath | Node model and linking rules (new) |
| `context_find` / `cli-on-fhir find <patient> <code\|text>` | Jump to nodes by code (`system\|code`, LOINC/SNOMED/RxNorm), then by text; no embeddings in v1 | `patient`, `query` | Token and string index | Node-to-index mapping (new) |
| `context_read_leaves` / `cli-on-fhir get <ref>...` | Source resources, optionally trimmed with `_elements`, each with its file location | `references[]`, `elements` | Catalog random access | — |

Domains in v1: demographics, problems (`Condition`), medications (`MedicationRequest`/`MedicationStatement`), allergies (`AllergyIntolerance`), labs and vitals (`Observation`), encounters (`Encounter`), procedures (`Procedure`), immunizations (`Immunization`), documents (`DocumentReference`, `Composition`, `DiagnosticReport`). "Active" uses each resource's status elements per FHIR version, not dates alone.

### Query deterministically

| Tool / CLI verb | Purpose | Key inputs | Backing | Gap |
|-----------------|---------|-----------|---------|-----|
| `search` / `cli-on-fhir search <type> "<query>"` | FHIR search over the folder | `resourceType`, `query`, `count`, `elements` | `SearchQueryInterpreter` | `_sort`, `_include`/`_revinclude`, chaining, `_has` (see [Search extensions](#search-extensions)) |
| `plan_fhir_search` / `cli-on-fhir search --check` | Validate an agent-written query before running it: unknown parameters, unsupported modifiers, and parameters no resource in the folder populates, with suggestions | `resourceType`, `query` | `SearchOptionsBuilder`, search parameter definitions, catalog statistics | Populated-parameter statistics (new) |
| `fhirpath_evaluate` / `cli-on-fhir fhirpath <scope> "<expr>"` | Evaluate FHIRPath over one resource, a patient compartment, or a search result set | `scope`, `expression`, `limit` | `Ignixa.FhirPath` | Scope resolution (new) |
| `sql_view` / `cli-on-fhir view run` | Run a SQL-on-FHIR ViewDefinition over the folder; returns rows (capped) or writes CSV/Parquet/NDJSON | `view`, `filter`, `out?` | `Ignixa.SqlOnFhir`, `tools/Ignixa.SqlOnFhir.Cli` | Catalog as input source (new) |

### Terminology

Terminology is a first-class group, not a validation helper. Agents are weakest at codes: they guess plausible-looking SNOMED, LOINC or RxNorm codes, confuse similar concepts, and miss descendants when filtering. Letting the agent look up, search and navigate loaded terminologies, and making search and navigation subsumption-aware, removes that class of error.

| Tool / CLI verb | Purpose | Key inputs | Backing | Gap |
|-----------------|---------|-----------|---------|-----|
| `terminology_list` / `cli-on-fhir term list` | Which code systems, versions, ValueSets and ConceptMaps are loaded, and from where; which systems used in the folder are *not* loaded | — | Terminology store | Store (new) |
| `terminology_lookup` / `cli-on-fhir term lookup <system> <code>` | Display, designations, properties, status (active/inactive, replaced-by), parents | `system`, `code`, `version?` | Terminology store | Properties, designations, status (new) |
| `terminology_search` / `cli-on-fhir term search "<text>"` | Text to candidate codes, optionally restricted to a system or ValueSet; ranked, paged | `text`, `system?`, `valueSet?`, `limit` | Terminology store | Text index over displays and designations (new) |
| `terminology_validate_code` / `cli-on-fhir term validate` | Is this code in this CodeSystem or ValueSet; does the display match | `system`, `code`, `display?`, `valueSet?` | `InMemoryTerminologyService.ValidateCodeAsync` (value-set membership) | Hierarchy-aware ValueSets (new) |
| `terminology_subsumes` / `cli-on-fhir term subsumes <a> <b>` | Subsumption between two codes | `system`, `codeA`, `codeB` | Terminology store | Hierarchy closure (new) |
| `terminology_navigate` / `cli-on-fhir term tree <system> <code>` | Ancestors and descendants, with depth and paging | `system`, `code`, `direction`, `depth` | Terminology store | Hierarchy closure (new) |
| `terminology_expand` / `cli-on-fhir term expand <valueSet>` | ValueSet expansion with filter and paging | `valueSet`, `filter?`, `offset`, `count` | Terminology store | Intensional (filter-based) ValueSets (new) |
| `terminology_translate` / `cli-on-fhir term translate` | ConceptMap translation between systems | `system`, `code`, `targetSystem`, `conceptMap?` | Terminology store | ConceptMap evaluation (new) |

Terminology also feeds the other groups:

- **Search**: token modifiers `:below`, `:above`, `:in` and `:not-in` resolve through the store, so "Conditions that are any kind of diabetes" is one search, not a guessed list of codes. Current interpreter support for these modifiers is unverified.
- **Navigation**: `context_find` by code also matches descendants; problem and medication clusters can group by ancestor (for example, all beta blockers).
- **Inspection**: `folder_inspect` reports per system how many codes in the folder resolve, are inactive, or are unknown to the loaded terminologies. That is a direct data-quality signal for the agent.
- **Validation**: extensible and required binding checks use the same store.

### Check and transform

| Tool / CLI verb | Purpose | Key inputs | Backing | Gap |
|-----------------|---------|-----------|---------|-----|
| `validate` / `cli-on-fhir validate` | Validate resources against core or loaded IG profiles; summarized by profile and rule, with sample failures | `scope`, `profile?`, `packages?` | `Ignixa.Validation`, `Ignixa.PackageManagement` | Summarizer; lazy package load |
| `deid` / `cli-on-fhir deid` | De-identify the folder, a patient, or a query result into an output folder | `scope`, `config`, `out` | `Ignixa.DeId`, `tools/Ignixa.DeId.Cli` | Catalog as input source |
| `fml_transform` / `cli-on-fhir map` | Apply a StructureMap (FHIR Mapping Language) to resources; preview or write output | `map`, `scope`, `out?` | `Ignixa.FhirMappingLanguage` | Map resolution from folder or `--maps` |

### Later (not v1)

| Capability | Why later |
|------------|-----------|
| IPS / `$summary` document generation | Generator lives in `Ignixa.Application/Features/Experimental/Ips`; under the prototype stance it can be copied into the tool in P3 rather than waiting for the folder adapter |
| Narrative generation for resources without text | `Ignixa.NarrativeGenerator` is standalone; useful for leaves, but not needed for navigation |
| Generated summaries (`derived` field) | Optional, behind an explicit flag and a configured `IChatClient`; v1 leaves summaries to the calling agent |
| Embedding-based `context_find` | Code and text matching first; add only with an evaluation showing misses |
| Write-through edits | See `write-through-and-locking` candidate |

## Response envelope

Every tool returns the same top-level shape. The CLI prints the same JSON with `--format json`, or a rendered view of `facts` otherwise.

```json
{
  "facts": { },
  "derived": null,
  "evidence": [
    { "reference": "Observation/931", "versionId": "1", "source": { "file": "labs/obs.ndjson", "line": 412 } }
  ],
  "omitted": [ { "what": "37 older results", "why": "limit", "next": { "tool": "context_expand", "args": { "nodeKey": "lab/loinc|2823-3", "offset": 10 } } } ],
  "absent": [ { "what": "AllergyIntolerance", "checked": "Patient/p1 compartment, 3 files", "indexBuiltAt": "2026-10-15T09:12:00Z" } ],
  "issues": [ ],
  "freshness": { "indexBuiltAt": "2026-10-15T09:12:00Z", "staleFiles": 0 },
  "next": [ { "tool": "context_expand", "args": { "nodeKey": "problem/Condition-12" } } ]
}
```

| Field | Rule |
|-------|------|
| `facts` | Tool-specific, deterministic values. Never contains model-written text. |
| `derived` | Always `null` in v1. If summaries are enabled later: model, prompt version and source references are mandatory, and the content is labelled non-authoritative. |
| `evidence` | Filled by the tool from what it read; every fact traces to at least one entry. Includes the source file location so an agent or user can open it. |
| `omitted` | Every truncation (limit, size budget, unsupported element) is listed with a way to fetch more. An empty list means nothing was dropped. |
| `absent` | "Not found" results state the domain, scope and index time checked. A tool never reports absence it did not check. |
| `issues` | Problems with the request or data (OperationOutcome-style severity, code, diagnostics, location). Request errors fail the call; data issues (for example, an unparseable file in scope) are reported alongside results. |
| `freshness` | Index build time and the number of files changed since; when non-zero, `next` suggests a reindex. |
| `next` | Suggested follow-up calls with concrete arguments; generated from structure, never from resource content. |

Size budgets follow [tool-design.md](../../mcp-integration/investigations/tool-design.md): default responses target under 5 KB, hard cap 50 KB, list defaults of 10 and a maximum of 50, with `elements` supported wherever resources are returned.

## Terminology store

### What exists today

| Component | Capability | Limitation for this tool |
|-----------|-----------|--------------------------|
| `InMemoryTerminologyService` (`src/Core/Ignixa.Validation/Services/`) | `ValidateCodeAsync` and `ValidateBindingAsync` via value-set membership; `LookupCodeAsync` returns display only, through `ICodeSystemProvider` | `ExpandValueSetAsync` returns `null` (`:277-284`); `TranslateCodeAsync` returns no matches (`:311-320`); `SubsumesAsync` always returns `"not-subsumed"` (`:325-330`), a success-shaped answer for an unsupported operation |
| `SqlServerTerminologyService`, `HybridTerminologyService`, `SqlServerCodeSystemImporter` (`src/DataLayer/Ignixa.DataLayer.SqlServer/Features/Terminology/`) | Imports FHIR `CodeSystem`, `ValueSet` and `ConceptMap` resources with hierarchy flattening (parent reference + level); `$expand` implemented, per `docs/features/terminology-services/investigations/new-design.md` | Requires SQL Server; not usable from a local tool |
| Native distribution importers | None found: no SNOMED CT RF2, LOINC CSV, RxNorm RRF or ICD-10-CM loaders | Large terminologies can only arrive as FHIR `CodeSystem` resources today |

The `SubsumesAsync` behavior must not reach an agent. A tool that says two codes are unrelated when it cannot tell is worse than no tool. The local store must answer "unknown, `<system>` not loaded" instead, and the existing service should be changed to fail loudly in its own right (an AGENTS.md violation independent of this feature). Done in Task 7: `InMemoryTerminologyService.SubsumesAsync` now fails with `NotSupportedException`, and cli-on-fhir's store answers from its own hierarchy without it.

### Proposed local store

A terminology store owned by the tool, separate from the resource index:

- **Sources**, in order of effort:
  1. **P1: FHIR core terminology as distributed via npm.** The `CodeSystem`, `ValueSet` and `ConceptMap` resources in the core packages (`hl7.fhir.r4.core`, `hl7.fhir.r5.core`, and so on) and in `hl7.terminology.*` (HL7 Terminology/THO), plus any IG packages and terminology resources found in the folder itself. Packages are read from a local path or package cache, or fetched through `Ignixa.PackageManagement`'s NPM registry support when the user allows it.
  2. Native distributions the user already holds: SNOMED CT RF2 snapshot, LOINC CSV, RxNorm RRF, ICD-10-CM. These are new loaders.
  3. Optional remote terminology server (for example, tx.fhir.org) for systems not loaded. Off by default, because requests would carry codes out of the machine.
- **Model**: concepts (system, version, code, display, status, properties), designations, is-a edges with a transitive closure or ancestor index, ValueSet definitions with lazily materialized expansions, and ConceptMap groups.
- **Storage**: small code systems stay in memory. SNOMED CT-scale content (hundreds of thousands of concepts and more relationships; not measured here) is where an embedded on-disk store such as SQLite with a closure table earns its place, built once per release and reused across folders in a user-level cache. This is the strongest SQLite case in this feature, independent of the resource index. The ancestry-index design in `docs/features/terminology-services/investigations/sql-design.md` is the starting point.
- **Contract**: implement `ITerminologyService` so `Ignixa.Validation` uses the same store, with every result carrying the system version that answered it.

### P1 scope: FHIR core terminology

What already exists:
- `Ignixa.Specification` embeds pre-expanded core ValueSets per FHIR version (`Generated/{Stu3,R4,R4B,R5,R6}ValueSetProvider.g.cs`, flat `(system, code, display)` lists loaded lazily from embedded resources). That is a zero-download baseline for membership checks and displays.
- `Ignixa.PackageManagement` has an NPM registry model (`NpmPackageSourcePolicy`, `KnownPackages`), and the server treats `hl7.fhir.r4.core` as provided in-process.

What P1 adds:
- Loading the `CodeSystem` resources from those packages, which gives hierarchy (nested `concept.concept`, `parent`/`child` properties), definitions, designations and status. That makes `terminology_navigate`, `terminology_subsumes` and full `terminology_lookup` real for FHIR-defined and THO systems, beyond the flat expansions.
- `terminology_search` over those systems' displays and designations.
- Loading the ConceptMaps shipped in the packages, so `terminology_translate` (P2) has real maps to work with as soon as it lands.

What P1 does not cover: SNOMED CT, LOINC, RxNorm and ICD content. Core packages reference these systems but do not contain their concepts (their `CodeSystem` stubs have `content` = `not-present`). ValueSets defined by filters over them (for example "descendants of a SNOMED concept") cannot be expanded in P1. Tools must report this as *system not loaded*, not as an empty result. Native loaders for these systems are P2.

Verified in the prototype (Task 7): `Ignixa.PackageManagement` reads neither the standard FHIR package cache nor packages.fhir.org as published. Its exact-version `NpmPackageAcquirer` expects npm version metadata at `/<id>/<version>`, where packages.fhir.org serves the archive itself, and an SHA-512 `integrity` the registry does not publish (only an SHA-1 `shasum`); its strict extractor rejects `hl7.fhir.r4.core#4.0.1`, which ships 463 entries outside `package/` (`other/`, `openapi/`). cli-on-fhir therefore has its own bounded reader and registry client (see the tool README, "Terminology"), reads `~/.fhir/packages/<id>#<version>/package/` read only, and keeps downloads in `<cache root>/packages`. Measured: `hl7.fhir.r4.core@4.0.1` loads 1,059 CodeSystems with concepts (3 without), 1,316 ValueSets and 80 ConceptMaps; reading and parsing it takes about 0.5 s.

### Rules

- Every terminology answer names the code system and version it came from. "Not found" distinguishes *code not in the loaded version* from *system not loaded*.
- Inactive concepts are reported as inactive, with replacements where the distribution provides them, rather than silently treated as valid or invalid.
- Licensing is the user's responsibility and stays explicit: the tool loads terminology files the user points it at and never downloads licensed content (SNOMED CT, UMLS-sourced RxNorm) on its own.

## Search extensions

`fhir-candle` (`brendankowitz/fhir-candle`, branch `feature/ignixa-sdk-migration-implementation`, at `6d0af1e`, consuming Ignixa 0.6.41 packages) already extends Ignixa's in-memory search:

| Capability | Location in fhir-candle | Ignixa today |
|------------|------------------------|--------------|
| Forked interpreter: same-entry matching (multi-field criteria such as `system\|code` must match one index entry), field-aware comparisons (reference, uri, quantity, token text), range-aware bounds | `src/FhirCandle.Engine/Search/CandleSearchQueryInterpreter.cs` | `SearchQueryInterpreter` evaluates each field as its own `Index.Any(...)` and intersects (`SearchQueryInterpreter.cs:36-52`), which can cross-match fields from different entries (reported by fhir-candle; not yet reproduced here) |
| `_include`, `_revinclude` | `src/FhirCandle.Engine/Search/SearchExecutor.cs` | Throws |
| Forward chains (multi-level) and `_has` reverse chains | `SearchExecutor.EvaluateChained`, `CandleSearchService.TryParseChained` | Throws |
| `_sort` | `src/FhirCandle.Engine/Search/FhirSortComparer.cs` | Throws |
| Compartment search | `src/FhirCandle.Engine/Storage/VersionedFhirStore.cs` (`CompartmentSearch`, `IsInCompartment`), `Compartments/CoreCompartmentSource.cs` | Throws |
| In-memory index maintained on write | `src/FhirCandle.Engine/Storage/ResourceStore.cs` (`_indexes`) | Per-query disk scan in the FileSystem provider |

These are coupled to fhir-candle's storage: `SearchExecutor` takes `Func<string, ResourceStore?>` and re-extracts each resource's index on every call because `ResourceStore` does not expose its stored index.

For the prototype, these are copied into the tool as-is (with origin noted) and adapted to the folder catalog. Once usage shows which extensions matter, the proposed upstreaming into `Ignixa.Search.InMemory` is:

1. A small source contract (enumerate `(ResourceKey, IReadOnlyCollection<SearchIndexEntry>)` for a type; resolve one resource by key) implemented by the folder catalog, a cached FileSystem snapshot, and fhir-candle's store.
2. Includes, chaining, `_has`, `_sort` and compartments behind opt-in options. Disabled features keep throwing `SearchOperationNotSupportedException`, and the capability statement or `folder_inspect` reports exactly which are enabled.
3. Same-entry and range-bound matching fixes applied unconditionally, each preceded by a regression test that fails on the current interpreter.
4. fhir-candle consumes the upstream version instead of maintaining a fork.

## Tradeoffs

| Pros | Cons |
|------|------|
| Orientation in 0–2 calls (`folder_inspect`, `context_get_root`) instead of agents paging through raw search | The patient tree (domains, clusters, links) is new code with clinical judgement in its rules, such as what counts as "active" and how problems link to medications |
| Deterministic, cacheable, no model cost or PHI egress from the tool itself | Without summaries, large records still need several `context_expand` calls |
| Every capability except the tree is a thin wrapper over existing Core libraries | Validation and terminology depend on package loading, whose startup cost is unmeasured |
| The envelope makes truncation, absence and staleness visible, which is where agents most often go wrong | A uniform envelope adds overhead to trivial calls |
| Search extensions already exist and are tested in fhir-candle | Upstreaming needs an abstraction change and coordination with fhir-candle releases |

## Alignment

- [x] Follows architectural layering rules: tools live in `tools/`; search extensions go to `Ignixa.Search` (Core) behind a Core-level contract; no Application or DataLayer dependency in v1.
- [x] Developer Experience: one command to start (`cli-on-fhir mcp <dir>`); the first two calls orient the agent.
- [~] Specification compliance: search semantics follow FHIR R4+ for supported features; unsupported features fail loudly. Clinical "active" rules must be checked per FHIR version (STU3–R5 status value sets differ).
- [x] Consistent with existing patterns: reuses `tool-design.md` limits, `SearchResultsDto`/`ResourceEntryDto` where they fit, `System.CommandLine` CLIs.

## Evidence

- Core libraries backing the capabilities have no Application or Domain dependency (from their `.csproj` project references): `Ignixa.DeId` (Abstractions, Serialization, FhirPath, Validation), `Ignixa.FhirMappingLanguage` (Abstractions, FhirPath, Serialization), `Ignixa.NarrativeGenerator` (Abstractions, FhirPath, Serialization), `Ignixa.SqlOnFhir` (Abstractions, FhirPath, Serialization), `Ignixa.Validation` (FhirPath, Specification, Serialization).
- `InMemoryTerminologyService` (`src/Core/Ignixa.Validation/Services/InMemoryTerminologyService.cs`) implements value-set membership and display lookup only; expand, translate and subsumes are stubs (see [Terminology store](#terminology-store)). The result types (`ExpandResult`, `LookupResult`, `SubsumesResult`, `TranslateResult`) exist in `src/Core/Ignixa.Validation/Abstractions/` and can carry full answers.
- Compartment definitions are generated per version: `src/Core/Ignixa.Search/Generated/{STU3,R4,R4B,R5,R6}CompartmentDefinitions.g.cs`.
- Existing CLIs for de-identification (`tools/Ignixa.DeId.Cli`, JSON and NDJSON inputs) and SQL-on-FHIR (`tools/Ignixa.SqlOnFhir.Cli`) show both libraries working outside the server.
- IPS generation is in `src/Application/Ignixa.Application/Features/Experimental/Ips/` (ADR-2512, Proposed) and depends on the Application layer.
- fhir-candle findings above were read from file headers, signatures and design comments at `6d0af1e`; method bodies were not fully reviewed and nothing was run.

## Risks

1. **Prompt injection from folder content.** Notes, `DocumentReference` attachments and narrative in a third-party folder are untrusted. They are returned only inside `facts`/leaf payloads marked as resource content; they never influence tool descriptions, `next` suggestions, or tool selection.
2. **PHI flows to the agent's model.** The tool runs locally, but responses reach whatever model the MCP client uses. `deid` provides a de-identified working copy; a `--deid-output` mode that applies de-identification to every response is worth considering.
3. **Clinical rules in the tree.** "Active", "latest" and problem-medication linking are judgement calls. They must be documented, version-aware, and visible in `facts` (for example, which status values were treated as active), so an agent can tell a rule from a fact.
4. **Unbounded expressions.** `fhirpath_evaluate` and `sql_view` need result caps and a timeout (FML already has `TransformTimeout`, 30 s default).
5. **Output location.** `deid`, `fml_transform` and `sql_view` writes must refuse to target the source folder.
6. **Terminology size and licensing.** SNOMED CT-scale content needs a persistent store and a one-off build per release; the tool must never fetch licensed content itself, and must say which version answered.
7. **Wrong "no" answers.** Reusing the current `InMemoryTerminologyService` as-is would return "not-subsumed" for unsupported subsumption; the local store must not inherit that behavior.

## Phasing

Phases match the plan in [local-cli-mcp-fhir-server](local-cli-mcp-fhir-server.md#phased-plan): P1 ships on the CLI, P2 adds the MCP stdio server with parity.

| Phase | Capabilities |
|-------|--------------|
| P1 | `folder_inspect`, `patient_list`, `context_get_root`, `context_expand`, `context_find`, `context_read_leaves`, `fhirpath_evaluate`, `search`; envelope; usage log; search extensions copied from fhir-candle (at minimum `_sort`, `_include`, compartments); terminology store from the FHIR core and HL7 Terminology npm packages plus terminology resources in the folder (see [P1 scope](#p1-scope-fhir-core-terminology)) with `terminology_list`, `terminology_lookup`, `terminology_search`, `terminology_validate_code`, `terminology_navigate`, `terminology_subsumes` |
| P2 | All P1 capabilities over MCP; native loaders (SNOMED CT RF2, LOINC, RxNorm, ICD-10-CM) with a persistent store; subsumption-aware search modifiers and `context_find`; `terminology_expand`, `terminology_translate`; `plan_fhir_search`, `sql_view`, `validate` (binding checks backed by the same store) |
| P3 | `deid`, `fml_transform`; IPS once the folder adapter hosts Application handlers |
| Later | Optional `derived` summaries, embeddings, write-through |

## Open questions

1. Should `context_get_root` return a fixed domain set, or derive domains from what the folder contains?
2. Which lab and vital codes count as "key" for the root digest: a fixed LOINC list, or the most frequent series per patient?
3. Should `--deid-output` be offered from P1, given that the tool's main consumer is a remote model?
4. After the prototype, which search extensions earned upstreaming into `Ignixa.Search.InMemory`, and does fhir-candle switch to the upstream version in the same release?
5. Which native terminology distributions matter first (SNOMED CT, LOINC, RxNorm, ICD-10-CM), and is a shared user-level terminology cache acceptable?
6. Should the local terminology store become the basis for a non-SQL `ITerminologyService` in the server too, replacing the in-memory stubs?
7. Is an opt-in remote terminology server fallback wanted at all, given the egress implications?

## Verdict
*Pending evaluation*
