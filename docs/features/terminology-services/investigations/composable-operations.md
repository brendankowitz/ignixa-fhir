# Investigation: Composing FHIR Terminology Operations

**Feature**: terminology-services
**Status**: Proposed
**Created**: 2026-10-20
**Related ADR**: [ADR-2610: Composing FHIR Terminology Operations](../../../adr/adr-2610-composable-terminology-operations.md)

## Executive Summary

FHIR terminology operations have overlapping semantics that can be reused as internal sub-operations. A caller may submit one FHIR operation while the server resolves it by combining membership checks, concept lookups, ValueSet composition, and terminology-provider calls. This is an implementation choice, not a requirement that servers make HTTP calls to their own public endpoints.

Ignixa already has a small example: `ValidateBindingAsync` performs ValueSet membership validation and may then call `LookupCodeAsync` to determine a suggested display. The same opportunity exists in public `$validate-code`, `$expand`, `$translate`, and `$closure` workflows, but each has different correctness and cost constraints. In particular, expanding an entire ValueSet for every single-code validation is usually the wrong decomposition.

The recommended direction is to compose semantics through internal terminology-service capabilities, keep operation parsing and FHIR response shaping at the API boundary, and make sub-operation dependencies and incomplete results explicit. This investigation proposes opportunities and guardrails; it does not authorize an implementation or claim that the 2026 DevDays presentation described these exact call chains.

## Terminology Used Here

- **FHIR operation**: A public FHIR operation such as `ValueSet/$expand` or `CodeSystem/$lookup`.
- **Sub-operation**: Work performed inside an operation by reusing another terminology capability. It may call an internal method or, when explicitly configured, a remote terminology server.
- **FHIR ValueSet composition**: The `ValueSet.compose` include/exclude definition. This is FHIR resource content, not itself an operation-call architecture.
- **Composite terminology service**: An implementation that combines local and/or remote terminology capabilities. It does not imply that public endpoints call themselves over HTTP.

FHIR R4 defines the inputs and outputs of each operation, but does not prescribe an internal call graph. The implementations below should be assessed against the required observable result, not by requiring a particular number or sequence of sub-calls.

## Evidence and Scope

The FHIR R4 operation definitions are the contract references used for this investigation. Firely Server's public Advanced Terminology documentation provides a concrete sub-operation example: for ValueSet `$validate-code` requests with complex filters over CAR-backed CodeSystems, its delegated terminology implementation uses CodeSystem `$validate-code`, `$subsumes`, and `$lookup`, then aggregates the results. The CAR host handles supported requests and falls back to the configured terminology proxy when it cannot. This requires configured Conformance Archive (CAR) files and the Advanced Terminology license.

The Firely DevDays 2026 recap confirms a terminology session but does not publish its detailed sub-call design. The documented Firely Server example is relevant implementation evidence, but this investigation does not claim it is the specific design presented at DevDays.

FHIRsmith is used as a comparison for operation surface and implementation breadth, not as a normative source. Its TX module documents `$lookup`, `$validate-code`, `$expand`, `$translate`, `$subsumes`, and `$closure`; its OpenAPI definitions and tests also include `$batch-validate-code`. Batch validation is not an R4 core terminology operation and should be evaluated as an extension or optimization, not treated as required FHIR parity.

## Current Ignixa State

- The experimental API exposes type-level `$expand` as GET, `$translate` as POST, and `$subsumes` as POST. The feature is gated by the experimental terminology setting. There are no public terminology routes for `$lookup`, `$validate-code`, or `$closure`.
- `ITerminologyService` already exposes methods for validation, lookup, expansion, translation, and subsumption. The SQL Server implementation provides the corresponding storage-backed methods; the in-memory implementation has limited lookup support and returns unsupported/default results for expansion, translation, and subsumption.
- SQL ValueSet validation checks membership against an imported expansion. It can report a display mismatch. `ValidateBindingAsync` also invokes lookup to supply a suggested display when needed.
- SQL expansion returns stored/imported expansion rows, supports text filtering and pagination, and reports whether the import marked the expansion incomplete. ValueSet import resolves `compose` includes and excludes through `SqlServerValueSetComposer`; unsupported compose filters result in an explicitly partial expansion. The current endpoint emits `expansion.incomplete`, which is not part of the FHIR R4 ValueSet expansion contract.
- The current translate request DTO accepts `source`, `target`, and source-system `version`; the SQL translation query does not currently apply those three fields. It does apply ConceptMap URL/version, source system/code, target system, and reverse direction.
- Terminology resources are currently shared across tenants; the experimental handlers accept a tenant id but do not pass it into `ITerminologyService`.

These are observations of the current implementation, not recommendations to preserve its current limits.

## Operation Dependency Matrix

| Public operation | Candidate internal sub-operations | Ignixa today | Opportunity and constraints |
|---|---|---|---|
| `CodeSystem/$validate-code` | Targeted concept resolution; optionally `$lookup`-equivalent display/property resolution | No public endpoint. Lookup exists; validation is currently ValueSet-scoped. | A targeted lookup can establish whether a code is known and obtain its recommended display. Do not require the full lookup payload when a cheaper code-existence query is sufficient. Preserve the distinction between invalid input and terminology that could not be resolved. |
| `ValueSet/$validate-code` | Test membership against relevant includes; for hierarchy filters, use CodeSystem validation/subsumption; optionally lookup display after membership succeeds | Internal ValueSet membership validation exists against imported expansions. No public endpoint. | Prefer direct membership or targeted compose evaluation. Firely documents decomposing filtered ValueSet validation into CodeSystem `$validate-code`, `$subsumes`, and `$lookup` calls for CAR-backed systems. Expanding the whole ValueSet to test one code can be prohibitively expensive for large systems and introduces expansion limits and paging concerns. For a `CodeableConcept`, the FHIR contract allows one coding to establish membership and may check for conflicting codings; it is not equivalent to accepting any unchecked coding. |
| `ValueSet/$expand` | Resolve included CodeSystems and referenced ValueSets; apply exclusions; enrich displays/designations when needed | Public GET endpoint; imported expansion and import-time compose evaluation exist. Compose can resolve local references; expansion responses do not currently populate alternate designations. | A provider-backed implementation could delegate unresolved systems or referenced sets. Apply exclusions and request filters with the correct set semantics; preserve version, inactive, designation, and pagination behavior. Under R4, return an error when the server cannot correctly expand because it does not understand a code system or has incomplete definitions. Do not use `expansion.incomplete` as a substitute; `valueset-unclosed` is a distinct extension for genuinely unbounded expansions. Avoid one lookup per expanded concept (N+1 requests). |
| `CodeSystem/$lookup` | Resolve code and requested properties/designations from CodeSystem content or a terminology provider | Internal lookup exists; no public endpoint. SQL returns display, definition, properties, and designations when imported data contains them. | This is a natural reusable primitive for display correction and concept decomposition. Use requested properties and display language where supported; do not fabricate missing properties or treat unsupported systems as confirmed not-found. |
| `CodeSystem/$find-matches` (preliminary in R4) | Match supplied properties against CodeSystem concepts; optionally use lookup to enrich returned matches | No public endpoint or dedicated matching capability. | R4 defines this preliminary operation for finding complete, partial, or possible code matches from properties. A matching/index strategy is required; `$lookup` may enrich candidate results but is not a mandated sub-call. Firely documents `$find-matches` support; it is not listed in FHIRsmith's TX operation table. STU3-era `$compose` naming and supported FHIR releases need separate compatibility review. |
| `ConceptMap/$translate` | Evaluate relevant ConceptMaps; for a `CodeableConcept`, consider each coding and aggregate allowed matches | Public POST endpoint for a reduced JSON request. SQL mapping supports URL/version, reverse, and target system; source, target and source-system version are currently ignored. | A multi-coding request is a possible composition point, but aggregation must preserve each mapping's equivalence, negative/no-map meaning, dependencies/products, and provenance. Do not turn “one coding had a match” into an unconditional positive result if the other codings conflict or the mappings are unacceptable. |
| `CodeSystem/$subsumes` | Resolve both concepts, then evaluate hierarchy/ancestry | Public POST endpoint. SQL compares imported concepts and walks parents up to 50 levels. | The existing hierarchy implementation is already a targeted internal composition. Unknown systems/codes currently return `not-subsumed`, which is not the same as proving no relationship; the public contract should distinguish inability to determine from a negative result where required. |
| System-level `$closure` (returns a ConceptMap) | Incrementally compare submitted codings with known closure entries, using subsumption relationships for newly observed pairs | No public endpoint or closure table. | `$subsumes` may be a semantic building block, but a naïve request-per-pair loop has poor scaling. Closure needs its own identity, incremental update, and persistence semantics; it is not just a wrapper around two-code subsumption. |
| Resource/profile validation | Validate each coded binding, optionally share batched requests or reuse results | Binding validation exists in the validation service; it is not the same public contract as `$validate-code`. | Reuse the same membership and lookup semantics to avoid divergence. A batch interface can reduce repeated calls, but batching is an optimization/extension and must preserve per-binding outcomes and severity. |

## Candidate Compositions

### 1. ValueSet validation with conditional lookup — highest confidence

The semantic flow is:

1. Resolve the ValueSet membership for the supplied code, system, and version.
2. If membership is valid and a display was supplied or a recommended display is requested, resolve concept display data.
3. Return the membership result separately from display warnings and terminology-unavailable conditions.

Ignixa already performs a variant of this in `ValidateBindingAsync`: membership first, then `LookupCodeAsync` when it has a display to compare. The public ValueSet `$validate-code` operation should reuse the same semantics but add its full FHIR parameter/result contract. This does not imply routing validation through the public `$lookup` HTTP endpoint.

For one code, the storage layer should prefer an indexed membership query over building a full expansion. Where the ValueSet is composed from external systems, the provider can expose a targeted validation capability or a compose evaluator can issue bounded membership checks for only the relevant clauses. For hierarchy filters such as `is-a` or `descendent-of`, a CodeSystem `$subsumes` capability may answer the relationship without materializing the entire set. Lookup is conditional: use it when the response needs a recommended display or properties, not as an unconditional extra call.

Firely Server documents a concrete version of this composition for complex ValueSet filters over CAR-backed CodeSystems: it decomposes validation into CodeSystem `$validate-code`, `$subsumes`, and `$lookup` operations and aggregates the results. Its configured terminology proxy handles requests the CAR host cannot. This is an example of a semantic decomposition across operation capabilities, not a recommendation to use HTTP loopback inside Ignixa.

### 2. ValueSet expansion across providers — conditional

An expansion may resolve a ValueSet definition through multiple `compose.include` entries, referenced ValueSets, and `compose.exclude` entries. Ignixa's SQL composer already evaluates local imports; a future external-provider path could resolve a clause through an appropriate provider, then combine the results using ValueSet include/exclude semantics.

This path needs explicit policies for:

- Selecting a provider by code system, canonical, FHIR version, and supported operation.
- Applying `system-version`, `check-system-version`, `force-system-version`, and ValueSet version consistently.
- Applying exclusions and text filters after the right candidate sets are known.
- Under FHIR R4, return an error when the server cannot correctly expand because it does not understand a code system or has incomplete definitions. Do not use Ignixa's current `expansion.incomplete` JSON field as a substitute for this required error. The R4 `valueset-unclosed` extension is a distinct case for a genuinely unbounded expansion, such as one involving post-coordinated value sets.
- Enforcing expansion-size limits and doing pagination only after a deterministic, correctly ordered expansion exists.
- Avoiding per-code `$lookup` fan-out; providers should return required concept data in bulk or in the expansion result.

Expanding a referenced ValueSet by an internal call may be useful when that ValueSet is stored separately or owned by a different provider. It should not replace the local composer for imported local definitions by default.

### 3. `CodeableConcept` translation — plausible, higher semantic risk

FHIR `$translate` accepts a code, `Coding`, or `CodeableConcept`. When given a `CodeableConcept`, a server may translate its codings, but it must preserve the relationship of each returned mapping to its source coding, evaluate the supplied source/target scopes, and account for ConceptMap dependencies and products. Independent per-coding queries can be parallelized only when doing so does not lose these conditions or return misleading equivalence.

Ignixa's current JSON DTO accepts only a single code/system pair. The contract and SQL implementation need correction before this is a safe composition.

### 4. Closure using subsumption — separate operation design

`$closure` naturally relates to subsumption but has incremental table semantics and can involve many code pairs. Calling `$subsumes` repeatedly as independent HTTP requests would add latency and may repeat hierarchy work. A future closure implementation should use shared ancestry/hierarchy primitives with batched database access and a dedicated closure state model, rather than exposing a loop around the public subsumes endpoint.

## Architectural Options

| Option | Description | Assessment |
|---|---|---|
| Public-endpoint loopback | An operation handler calls another public Ignixa endpoint over HTTP. | Reject for local composition: adds serialization, routing, authentication/tenant-context, network, and tracing failure modes; risks recursive call cycles. Remote terminology calls remain valid when explicitly delegated to a configured external provider. |
| Shared internal terminology capabilities | API parses the operation; application-level workflow composes internal service capabilities; DataLayer uses targeted SQL/provider work and may optimize multiple sub-operations in one query. | Recommended. Reuses existing `ITerminologyService` contract without requiring a new generic workflow engine. Requires an explicit acyclic dependency map and consistent errors/incomplete-result semantics. |
| General operation-graph orchestrator | Describe arbitrary operation dependencies as configurable graphs with fan-out, retries, result merging, and provider selection. | Defer. No current evidence requires arbitrary graph configuration; it introduces policy, cycle, ordering, partial-result, timeout, and merge complexity before the supported call graph is known. |

## Recommended Priorities

1. Add public standards-shaped `$lookup`, CodeSystem `$validate-code`, and ValueSet `$validate-code` routes, including FHIR `Parameters` parsing and response/error semantics. Reuse existing internal methods and add a targeted CodeSystem validation capability rather than expanding entire ValueSets.
2. Align current `$translate` with the parameters it already accepts: source/target scopes and source system version. Add support for `Coding`/`CodeableConcept` inputs only with tests for multi-coding, map equivalence, and dependencies/products.
3. Extend expansion only where imports or provider boundaries require it. Add explicit tests for nested ValueSets, exclusions, unresolved systems, pagination and designations before adding external sub-calls. For R4, unresolved or incompletely understood systems must not yield a success-shaped partial expansion.
4. Assess CodeSystem `$find-matches` separately: it is a preliminary R4 operation whose matching semantics and supported versions should be explicit before adding an API.
5. Design `$closure` independently, using shared hierarchy work but not repeated HTTP `$subsumes` calls.
6. Treat batching as an optional optimization. FHIRsmith supports `$batch-validate-code`, but it is not a core R4 terminology operation.

## Cross-Cutting Requirements

- **Layering:** API owns FHIR HTTP/Parameters serialization; application workflows own operation-level composition; reusable validation contracts remain in Core; DataLayer owns efficient local queries and provider-specific behavior.
- **Acyclic dependencies:** Document each operation's sub-operation graph and reject cycles. Do not invoke endpoint handlers from each other.
- **Failure semantics:** Distinguish confirmed invalid/not-found from unsupported, missing, incomplete, timeout, and unavailable terminology. For R4 expansion, fail when the server cannot correctly determine the expansion; reserve `valueset-unclosed` for the unbounded-set case. Never convert an unresolved sub-call into a success or definitive negative result.
- **Cancellation and budgets:** Propagate `CancellationToken`; bound remote calls, fan-out, depth, and expansion size. A caller's cancellation must stop sub-operations.
- **Versioning:** Honor operation parameters and supported FHIR-version differences. Cache keys include canonical/version, operation parameters that affect results, and provider identity.
- **Multi-tenancy:** Current terminology is shared across tenants. Do not imply tenant isolation in new sub-calls until tenant scope is actually part of the terminology service contract and storage routing.
- **Observability:** Record which sub-capability/provider was used, duration, and whether the final result is incomplete, without logging sensitive request content.
- **Performance:** Prefer targeted and batched backend queries over N+1 FHIR operations. Measure expansion and validation workloads rather than setting unverified latency targets.
- **Conformance:** Test through public FHIR operation contracts and supported versions; internal call count is not itself a conformance requirement.

## Open Questions

- Which configured external terminology-service model should Ignixa support: operation-level delegation only, selective sub-operation delegation, or both?
- How should supported FHIR releases that differ from R4 represent unresolved or unbounded expansion results, and how will that behavior be tested per release?
- Which FHIR releases and optional parameters are in-scope for the first public `$validate-code` and `$lookup` endpoints?
- Should CodeableConcept translation be implemented as separate mappings per coding, or should conflicting/multiple codings change the overall result? Confirm against each supported FHIR version before implementation.
- What closure identity and persistence lifetime should apply across tenants and terminology versions?

## References

- [FHIR R4 ValueSet `$validate-code`](https://hl7.org/fhir/R4/valueset-operation-validate-code.html)
- [FHIR R4 ValueSet `$expand`](https://hl7.org/fhir/R4/valueset-operation-expand.html)
- [FHIR R4 CodeSystem `$validate-code`](https://hl7.org/fhir/R4/codesystem-operation-validate-code.html)
- [FHIR R4 CodeSystem `$lookup`](https://hl7.org/fhir/R4/codesystem-operation-lookup.html)
- [FHIR R4 CodeSystem `$find-matches`](https://hl7.org/fhir/R4/codesystem-operation-find-matches.html)
- [FHIR R4 ConceptMap `$translate`](https://hl7.org/fhir/R4/conceptmap-operation-translate.html)
- [FHIR R4 ConceptMap `$closure`](https://hl7.org/fhir/R4/conceptmap-operation-closure.html)
- [FHIR R4 ConceptMap `$closure`](https://hl7.org/fhir/R4/conceptmap-operation-closure.html)
- [FHIR R4 terminology service](https://hl7.org/fhir/R4/terminology-service.html)
- [Firely Server terminology services](https://docs.fire.ly/projects/Firely-Server/en/latest/features_terminology/terminology.html), especially [Advanced Terminology: How it works](https://docs.fire.ly/projects/Firely-Server/en/latest/features_terminology/terminology.html#how-it-works)
- [Firely DevDays 2026 recap](https://fire.ly/blog/devdays-2026-recap-fhir/) — confirms a terminology session; does not expose the detailed sub-call design.
- [FHIR DevDays 2026 schedule](https://www.devdays.com/wp-content/uploads/2026/06/Schedule_2026_june_2.pdf)
- [FHIRsmith TX module operations](https://github.com/HealthIntersections/FHIRsmith/blob/94196eac42fca261c96b44f5af9ea94d387d4b42/tx/README.md#L89-L132)
- [FHIRsmith batch validation definition](https://github.com/HealthIntersections/FHIRsmith/blob/94196eac42fca261c96b44f5af9ea94d387d4b42/tx/openapi-operations.js#L151-L186)
- Ignixa implementation: `src/Application/Ignixa.Api/Endpoints/Experimental/TerminologyEndpoints.cs`, `src/Core/Ignixa.Validation/Abstractions/ITerminologyService.cs`, `src/DataLayer/Ignixa.DataLayer.SqlServer/Features/Terminology/SqlServerTerminologyService.cs`, and `src/DataLayer/Ignixa.DataLayer.SqlServer/Features/Terminology/SqlServerValueSetComposer.cs`.
