# Application Layer Modernization — Roadmap

**Status**: Draft for human decision (no ADR yet)
**Created**: 2026-07-11
**Basis**: Six independent Fable-model architecture reviews of `src/Application` (743 files / 9 projects), full findings in `investigations/*.md`.

This is a menu of options, not a decision. Per the project's Transformer Mandate, prioritization and scope are a human call — this document ranks by evidence-based severity and groups by the kind of decision required, so you can pick a cut line.

## Headline numbers

| Subsystem | Files reviewed | P0 | P1 | P2 |
|---|---|---|---|---|
| API/HTTP (Ignixa.Api, .OpenIddict, .Web) | ~77 | 3 | 14 | 8 groups |
| Domain & Conformance.Events | ~80 | 1 | 13 | 12 |
| Application core infra + admin features | ~150 | 4 | 17 (+3 addendum) | 15 |
| FHIR CRUD/REST vertical slices | ~120 | 8 | 19 | 9 |
| Features/Experimental | 103 | 4 | 14 | 12 |
| Background operations + sidecar contracts | ~75 | 5 | 13 | 10 |
| **Total** | **~743** (some double-counted dead duplicates) | **~25** | **~90** | **~80** |

For a codebase built largely by early-generation agents over 6 months with no prior holistic review, this is roughly what you'd expect: the "happy path" mostly works (it demos, F5 works, ADR macro-architecture mostly holds), and the damage is concentrated in error handling, concurrency, authorization completeness, and abandoned/superseded designs left wired into DI.

## Cross-cutting root causes (read this before the phase list)

The ~25 P0s and ~90 P1s are not 115 unrelated bugs. They cluster into a handful of recurring authorship patterns — fixing the pattern is cheaper than fixing each instance:

1. **"Skip means pass" fail-open logic.** The FHIR authorization pipeline, the MCP tool authorization, and several endpoint-filter registrations are all built as "each check either denies or defers" with no terminal default-deny. This one shape produced 4 of the 25 P0s (API systemic bypass, core-infra fail-open pipeline, MCP dead wiring, OpenIddict dev-auth-in-prod) — plus a related but architecturally distinct gap: bundle-entry execution bypasses the middleware pipeline outright, so even a correctly-configured endpoint's authorization never runs when reached through a bundle (item 5b below).
2. **Sophisticated infrastructure around dead code.** The composite schema provider + 4-stage debounce invalidation chain, the capability version-hash mechanism, the `MemoryCapabilityCache.ClearAsync` no-op, `MapRegistryCache`'s per-request "cache", and the Domain `Caching/` subsystem are all elaborate, plausible-looking machinery whose critical path is unreachable, uncalled, or a no-op. Roughly 800–1,000 lines fall into this category in `Ignixa.Application` alone, plus ~20% of `Ignixa.Domain`.
3. **Wiring built on both ends, never connected in the middle.** `If-Match` is parsed by the API, carried on the command, and never read by any handler. `IValidationBehavior` targets the wrong generic type and silently never runs. IPS strategy registration resolves strategies that the generator service never looks up. Same shape, three different features.
4. **Orchestrations/handlers designed to "never fail."** Background job orchestrations catch everything and return a failure payload instead of throwing, so `OrchestrationStatus.Failed` is unreachable and the status handler reports failed jobs as Completed. This single decision explains several background-ops P0s and P1s at once.
5. **Exception-message-substring control flow.** At least 6 separate locations (`ex.Message.Contains("conflict")`, `"not found"`, `"already loaded"`, etc.) decide HTTP status or business branching by string-matching exception text — silently breaks the moment a message is reworded.
6. **Tenant ID as three type systems.** `int` (routes, config, partition strategy) vs `string` (Admin commands, authorization claims) vs occasionally ignored entirely. Every seam is a parse/`ToString()` with its own failure mode; this caused real bugs (`LoadPackageHandler`'s post-hoc `int.Parse` after work already committed).
7. **ADR/reality drift.** ADR-2509 claims Domain has "no dependencies" (false) and diagrams `ResourceKey` in Domain (it's in Core). ADR-2512 is marked "Proposed" though fully implemented. The physical folder layout (`Ignixa.Domain` living under `src/Application/`) contradicts the layer diagram every reviewer read first. Nobody has needed to correct these because nobody had read the whole layer before this.
8. **Provenance markers, not malice.** Pervasive "Microsoft Corporation" copyright headers (this is `Ignixa Contributors`' code), `EnsureArg`/`ct` naming from the microsoft/fhir-server lineage, roadmap "Phase N" comments referencing plans that exist nowhere in the repo. Harmless individually; collectively they mean **comments in this codebase should be treated as unverified until re-checked against the code**, which several findings above depended on doing.

## Phase 0 — Security & data-integrity emergency

Target: before any further feature work. These are either unauthenticated access to PHI, or silent data corruption/loss on the core FHIR write path. Sequencing within the phase doesn't matter much — none depend on each other — but the security items are the ones that matter most if this server is (or will be) internet-reachable with auth enabled.

### Security / auth bypass

| # | Finding | Location | Effort |
|---|---|---|---|
| 1 | `FhirAuthorizationFilter` catch-all turns every intentional 400/404/412 into a 500 once auth is enabled — the reason the bypass below went unnoticed (tests run with auth off) | `Ignixa.Api/Filters/FhirAuthorizationFilter.cs:68-121` | S |
| 2 | Systemic authz/audit bypass: `$export`, `$import`, admin package endpoints, system `/_history`, all agnostic operation routes never get the filter stack. **Fix constraint (verified)**: a global ASP.NET `FallbackPolicy` is *not* a sufficient alternative fix — `AspNetCorePipelineExecutor` invokes matched endpoints' `RequestDelegate`s directly for bundle entries, bypassing `AuthorizationMiddleware` entirely, so policy-based enforcement silently wouldn't apply inside bundles anyway (see item 5b). The fix must be filter-based (or the executor must run the real middleware pipeline) | `ExportEndpoints.cs`, `ImportEndpoints.cs`, `AdminPackageEndpoints.cs`, `HistoryEndpoints.cs`, `OperationEndpoints.cs`, `DeIdOperationEndpoints.cs` | M |
| 3 | OpenIddict `/connect/authorize` auto-approves every request as `dev-user`, gated only by a config flag with no environment check; checked-in prod-shaped config ships it enabled | `Ignixa.Api.OpenIddict/Endpoints/AuthorizationEndpoints.cs`, `OpenIddictServiceExtensions.cs` | S |
| 4 | Authorization pipeline fails open: an authenticated principal with zero roles and zero SMART scopes gets full CRUD (no terminal default-deny handler) | `Features/Authorization/Handlers/RbacAuthorizationHandler.cs`, `SmartScopeAuthorizationHandler.cs`, `FhirAuthorizationService.cs` | S |
| 5 | MCP tool authorization is entirely dead wiring — every MCP tool (including patch and package install/uninstall) runs with no authorization check at all | `Features/Experimental/Mcp/Tools/TenantAwareMcpTool.cs` + all tool constructors | M |
| 5b | Bundle entries bypass ASP.NET Core's middleware pipeline entirely (routing is hand-executed and the matched endpoint's `RequestDelegate` is invoked directly), and the mini `HttpContext` built for each entry never copies the parent request's `User` — so any endpoint whose authorization is enforced via middleware/endpoint metadata is silently skipped when reached through a bundle, running as an anonymous principal. A caller can write through a bundle to an endpoint they couldn't reach directly. Verified via cross-agent review handoff between the API and CRUD-slices passes | `Ignixa.Api/Infrastructure/AspNetCorePipelineExecutor.cs:108`, `Ignixa.Application/Features/Bundle/BundleEntryExecutor.cs` | M |

### Silent data corruption / spec-conformance failures on the write path

| # | Finding | Location | Effort |
|---|---|---|---|
| 6 | `ValidationBehavior` targets the wrong generic response type — **resource validation never runs on create/update**, regardless of tenant config | `Infrastructure/Behaviors/ValidationBehavior.cs`, `ApplicationServicesRegistration.cs` | S |
| 7 | `If-Match`/optimistic concurrency is parsed everywhere and enforced nowhere — stale-version writes silently succeed | `Features/Resource/CreateOrUpdateResourceHandler.cs`, `Features/Patch/PatchResourceHandler.cs`, both conditional handlers | M |
| 8 | Intra-bundle `urn:uuid` reference rewriting isn't implemented — transaction bundles persist literal `urn:uuid:...` strings instead of resolved references | `Features/Bundle/ReferenceResolutionContext.cs`, `BundleEntryExecutor.cs` | L |
| 9 | Transaction bundles aren't atomic (split commit across two coordinators, one already logs "rollback not implemented"), and Phase-2 response mapping can throw/misassign responses to the wrong entry | `Features/Bundle/BundleProcessor.cs`, `BundleChannelExecutor.cs` | L |
| 10 | Streaming batch path throws (aborting the whole batch) on any entry URL containing `?` — rejects legal `GET Patient?name=...`, `DELETE Patient?identifier=...` | `BundleChannelExecutor.cs`, `BundleProcessor.cs` | M |
| 11 | PATCH rejects legal `identifier` patches via a substring bug (`path.Contains(".ID")` matches `PATIENT.IDENTIFIER`) | `Features/Patch/Validation/ImmutablePathChecker.cs` | S |
| 12 | PATCH persists resources with **no search indices** and hardcodes `FhirVersion = "4.0"` regardless of tenant version — patched resources vanish from search and get silently rewritten to R4 | `Features/Patch/PatchResourceHandler.cs` | M |
| 13 | Streaming bundle parser can't handle any JSON string token > 8KB (routine for `Binary.data`/attachments) — throws "stuck in infinite loop" and leaks raw patient data into the exception message | `Features/Bundle/Serialization/StreamingBundleParser.cs` | M |
| 14 | Batch bundles processed via the streaming path never call `CommitAsync` — writes may be invisible/orphaned depending on backend transaction handling | `Features/Bundle/StreamingBundleContext.cs`, `BundleProcessor.cs` | S |
| 15 | IPS `$summary`: package-registered generation strategies are resolved, logged, and silently discarded — the generator always falls back to the default strategy | `Features/Experimental/Ips/Generator/IpsGeneratorService.cs` vs `IpsGeneratorHandler.cs` | S |
| 16 | MCP patch tools: numeric patch values are always sent as `valueString` instead of `valueDecimal`/`valueInteger` (type mismatch in the value-part builder) | `Features/Experimental/Mcp/Tools/FhirOperations/PatchResourceTool.cs` | S |
| 17 | `ResourceNotFoundException` returns HTTP 400 instead of 404 (base `StatusCode` never overridden); `MethodNotAllowedException` similarly wrong | `Ignixa.Domain/Exceptions/ResourceNotFoundException.cs` | S |

### Availability / crash / reliability

| # | Finding | Location | Effort |
|---|---|---|---|
| 18 | `/metadata` (and every capability-enforced request) crashes for R6 tenants — version-string switch omits R6 despite R6 being otherwise wired | `Features/Metadata/Segments/OperationsSegment.cs` | S |
| 19 | OIDC discovery deserialization can't bind RFC 8414 snake_case fields — SMART configuration discovery is dead on arrival for every fetch | `Features/Authorization/Services/OidcDiscoverySmartConfigurationProvider.cs` | S |
| 20 | `ConformanceState`: writers lock, readers don't — plain `Dictionary` read concurrent with locked writes on the request hot path (can throw or return corrupt state) | `Features/Conformance/ConformanceState.cs`, `ActiveSearchParameter.cs` | M |
| 21 | Two eternal orchestrations (TransactionWatcher, TtlCleanup) perform non-deterministic tenant-store I/O directly inside `RunTask` — a DurableTask determinism violation | `TransactionWatcherOrchestration.cs`, `TtlCleanupOrchestration.cs` | M |
| 22 | Failed background jobs are reported as Completed — orchestrations swallow exceptions into a "Completed" state with a failure payload; the status handler never inspects it | `Jobs/GetJobStatusHandler.cs`, `ImportOrchestration.cs`, `ExportOrchestration.cs` | M |
| 23 | Export job results record `tenant/...` paths while workers write to `partition/...` — every reported bulk-export output file is a 404 | `Export/Orchestrations/ExportOrchestration.cs` | S |
| 24 | Type-less `$export` silently exports only 6 hardcoded resource types instead of "all", contradicting both the command's own doc and the bulk-export spec | `ExportOrchestration.cs` | S/M |
| 25 | Zero retry policy anywhere in background operations (`ScheduleWithRetry` unused) — the entire fault-tolerance justification for choosing DurableTask (ADR 2510) is unimplemented | all orchestrations | M |

**13 dead byte-identical duplicate files** (`Ignixa.Application.Operations/Features/{Transform,Terminology}`) are flagged separately below (Phase 2) — not a correctness bug today, but the reason two of the fixes above (FHIRPath timeout, ConceptMap null-swallow) need to be applied to the *live* copy only, and the dead copy deleted so it can't silently diverge.

## Phase 1 — Reliability & observability

Once Phase 0 stops active bleeding, these fix the "we can't tell what's broken" problem — mostly background-ops and cross-cutting audit/metrics debt:

- Fix the failure-channel inversion in background jobs properly (orchestrations throw; status handler reconciles from real DurableTask state) — this one change collapses several P1s (empty catch compensation blocks, progress reporting, zombie-job detection).
- Replace fake fire-and-forget audit/metrics (`SidecarAuditLogger`, `FhirAuditFilter`/`FhirMetricsFilter`) with a real bounded-channel background flusher; stop fabricating audit field values (hardcoded `127.0.0.1`, invented status codes).
- Add job cancellation (`TerminateAsync` + cooperative checks in long loops) — nothing can currently stop a multi-hour export.
- Cap/stream import error accumulation instead of buffering full resource JSON (PHI) through orchestration state; write import error logs to blob storage, not local disk.
- Fix denied/unauthenticated requests never being audited (filter ordering).
- Deterministic IDs for ID-less import rows so retries (once retry policy exists) don't duplicate data.
- MemberMatch: match on `system|value`, not bare value — current behavior risks a wrong-patient match.
- Patient `$everything` truncates at 50 with no paging signal — make truncation visible at minimum.

Full list: see each investigation's P1 table, especially `background-operations-review.md` and `application-core-infra-review.md`.

## Phase 2 — Layering & dead-code cleanup

This is where the codebase gets meaningfully smaller and the ADR stops lying. No user-facing behavior change; mechanical or near-mechanical once each decision below is made (some require a one-line "graduate or delete" call):

**Delete outright** (verified zero live references):
- Domain `Caching/` subsystem (4 files) — dead, registered in DI, contains a cache-poisoning bug and a silent no-op invalidation
- 6 dead `Term*` terminology model classes in Domain (~350 lines, superseded by DataLayer entities)
- `BulkImportJob`, vestigial `TenantContext` (Domain)
- Composite schema provider eager-load path + its 4-stage debounce invalidation chain (`CompositeStructureDefinitionSummaryProvider`, `CompositeSchemaProviderRegistry`, `DebounceInvalidationStrategy`) — **or** finish it; it currently does nothing (zero callers, `ToTypeDefinition` is a stub)
- Capability version-hash mechanism (`ICapabilitySegment.GetVersionHashAsync` across 6 segments) — the mismatch branch is structurally unreachable
- `MemoryCapabilityCache.ClearAsync` no-op + 3 unused `ICapabilityCacheInvalidator` methods
- 13 dead byte-identical Transform/Terminology files in `Ignixa.Application.Operations` (retarget the 5 test files that currently cover only the dead copies)
- `Ips/Strategy/StructureDefinitionBasedStrategy.cs` (shadowed dead public class), `Mcp/Tools/DiagnosticTool.cs` (phase-1 spike exposing internal HTTP state)
- ~15 miscellaneous dead files/methods catalogued in the background-ops and API reviews (unused activities, unreferenced helpers, commented-out pseudo-code blocks)

**Architectural decisions needed** (pick one path, don't leave both):
- `Ignixa.Domain`/`Ignixa.Conformance.Events` physically live under `src/Application/` while all DataLayer projects depend on them, and ADR-2509 claims "no dependencies" (false — it references `Ignixa.Search`, `Caching.Memory`). Either move the projects to a `src/Domain/` sibling or amend the ADR to document the real, intentional dependency set.
- `Ignixa.DataLayer.SqlEntityFramework` has a project reference to `Ignixa.Application` (package/terminology event contracts) — violates the documented dependency direction. Move those event contracts to Domain.
- `HttpContext` and ASP.NET Core packages are compiled into `Ignixa.Application` (`FhirAuthorizationContext.HttpContext`, `IPipelineExecutor`, `FhirVersionExtractor`) — makes the authorization pipeline untestable without a web server. Project the specific HTTP facts needed (method, correlation id) instead of the whole context.
- Terminology import activity constructs concrete SQL DataLayer types directly from the Application layer (service-locates `SqlEntityFrameworkRepositoryFactory`, hand-builds raw SQL) — define `ITerminologyImportStore` in Domain, implement in SqlEntityFramework.
- `MapRegistryCache` (Transform) is built as a long-lived cache but registered per-request-scope, so its entire cache/TTL/invalidation apparatus is inert — decide singleton-keyed-by-tenant vs. delete the machinery.
- `IPackageResourceRepository` is an 18-method god interface accreted one method per feature — split into lifecycle vs. query-lookup interfaces.

**ADR hygiene**: amend ADR-2509's dependency claims, flip ADR-2512 from "Proposed" to "Accepted" (it's fully implemented), fix the Conformance.Events published README (documents 4 event types that don't exist).

## Phase 3 — Consistency sweep (mechanical, batchable)

Every review found the same style-drift issues, attributable to different code having been written by different agent generations. None of these are individually urgent; all are cheap to fix in bulk with a script/analyzer pass rather than a manual PR per file:

- Rename `ct` → `cancellationToken` (CLAUDE.md calls this out explicitly as a critical violation; ~10+ files, mechanical Roslyn rename)
- One-type-per-file splits (~50+ files across all six reviews)
- Seal classes by default (~40+ classes never designed for inheritance)
- Copyright header normalization — most files claim "Microsoft Corporation," this is Ignixa's code (legal-hygiene issue, not just style)
- Replace stringly-typed state (`JobStatus`, `ImportMode`, `ValidationDepth`, storage/search provider types) with enums — several are magic strings that compile a typo into a runtime bug
- Re-enable suppressed Roslyn analyzers currently NoWarn'd that would have caught several findings above (CA1031 broad-catch, CA1852 seal-types, CA1508 dead-conditions)
- Delete stray "Phase N" roadmap comments referencing plans that exist nowhere in the repo (including one citing an ADR number that doesn't exist)

## Phase 4 — Feature-fate decisions (need a product/eng call, not just a fix)

These aren't bugs so much as "is this thing supposed to exist" questions the reviews surfaced:

| Feature | Current state | Options |
|---|---|---|
| GraphQL (`Features/Experimental/GraphQl`, 33 files) | Highest-quality, best-tested code in the whole layer; blocked from graduating only by the registration-time config-binding issue (the same one blocking PR #277 off-by-default) | Fix config-bind timing, then graduate out of Experimental |
| Terminology (`Features/Experimental/Terminology`, 6 files) | Thin, stable wrappers; nothing experimental about them once the dead Operations duplicates are removed | Graduate |
| Transform (`Features/Experimental/Transform`, 9 files) | Real feature, but sync-over-async FHIRPath timeout that doesn't cancel, dead-lifetime cache, unread config | Fix P1s first, then reconsider |
| IPS `$summary` (`Features/Experimental/Ips`, 18 files) | Broken strategy handoff (Phase 0 #15), half-finished identifier lookup that always throws | Needs the P0 fix + a decision on identifier lookup (implement or remove the parameter) |
| MCP (`Features/Experimental/Mcp`, 31 files) | Must not graduate until authorization is enforced (Phase 0 #5) | Fix, then reassess |
| Composite schema provider eager-load | Zero callers, stub conversion method | Implement (if package-profile type resolution is actually wanted) or delete (Phase 2) |
| Capability version-hash validation | Structurally unreachable | Delete (Phase 2) — the segments' hash computation is nontrivial and entirely unused |

## What this review deliberately didn't do

- No code was changed. This is read-only analysis in an isolated worktree (`worktree-application-layer-review`).
- `src/Core` and `src/DataLayer` were out of scope except where an Application-layer finding required tracing a reference into them (e.g., the DataLayer→Application layering violation, the SQL terminology entity duplication). A DataLayer-focused pass would likely surface its own findings — the background-ops review flagged one open question (transaction heartbeat semantics under long-running imports) that needs DataLayer-side verification.
- Findings are as-observed on 2026-07-11; no attempt was made to check whether any have already been fixed on other branches.

## Source material

- [`investigations/api-http-layer-review.md`](investigations/api-http-layer-review.md)
- [`investigations/domain-conformance-events-review.md`](investigations/domain-conformance-events-review.md)
- [`investigations/application-core-infra-review.md`](investigations/application-core-infra-review.md)
- [`investigations/crud-vertical-slices-review.md`](investigations/crud-vertical-slices-review.md)
- [`investigations/experimental-feature-review.md`](investigations/experimental-feature-review.md)
- [`investigations/background-operations-review.md`](investigations/background-operations-review.md)
