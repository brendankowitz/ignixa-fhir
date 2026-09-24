# Investigation: Application Core Infrastructure & Administrative Features Review

**Feature**: application-layer-modernization
**Status**: Complete
**Created**: 2026-07-11
**Scope**: Ignixa.Application/Events, Infrastructure, Utilities, Features/{Authorization,Admin,Conformance,Metadata,Specification,Packages}

## Summary

The subsystem is functional but carries four P0-class defects: a fail-open authorization pipeline, an unsynchronized shared-state race in `ConformanceState`, a missing-R6 switch that crashes `/metadata` for R6 tenants, and an OIDC discovery deserializer that cannot bind standard snake_case fields. Beyond those, the dominant pattern is **machinery that no longer (or never) does what its API claims**: a capability version-hash mechanism whose mismatch branch is unreachable, a schema eager-load path with zero callers guarded by an elaborate event/debounce invalidation chain, a `ClearAsync` that is a silent no-op behind an "all caches cleared" log, and a "multi-tenant" package API that ignores the tenant. Layering is also compromised: `HttpContext` and web packages live in the Application project, non-Application namespaces are compiled into the Application assembly, and DataLayer references Application in violation of ADR-2509.

## Strengths

- **SMART scope parsing** (`Features/Authorization/Smart/SmartScopeParser.cs`) is genuinely good: generated regexes, CRUDS order validation, v1→v2 conversion, search-constraint parsing, and a clean `SmartScope` model. The POST `_search` + constrained-scope denial in `SmartScopeAuthorizationHandler.cs:101-115` shows real security thinking.
- **Structured logging discipline**: `AuditLogger.cs` and `LocalMetricsService.cs` use `[LoggerMessage]` source generators correctly.
- **PackageActivationPipeline / event-sourced conformance** (`Features/Conformance/PackageActivationPipeline.cs`) follows ADR-2512 faithfully on the write side: validate → build events → append atomically → apply, with idempotency and a proper activation lock.
- **Dependency-closure package loading** (`Features/Packages/ImplementationGuideProvider.cs:143-297`) handles diamond dependencies, version conflicts, core-package pre-seeding, and partial-failure reporting well; the `LoadedPackages`/`SkippedPackages` result surface is caller-friendly.
- **SidecarLoggerProvider** uses a bounded channel with drop-oldest backpressure and batched flushes — the right shape for fire-and-forget log shipping.
- No `Hl7.Fhir.*` NuGet references anywhere in the project — the `Ignixa.*` rule is respected.

## Findings

### P0 — Authorization pipeline fails open for authenticated principals with no roles and no SMART scopes
**Location**: `src/Application/Ignixa.Application/Features/Authorization/Handlers/RbacAuthorizationHandler.cs:44-49`, `Handlers/SmartScopeAuthorizationHandler.cs:34-39`, `Services/FhirAuthorizationService.cs:47-86`
**Issue**: The pipeline is a chain of handlers where each returns `Success()` when its concern doesn't apply. RBAC skips when `!context.HasRoles` ("let next handler decide"); SMART skips when `SmartContext == null`. No handler is the "decider of last resort" — `FhirAuthorizationService` grants access when every handler passes. A valid JWT carrying zero roles and zero scopes therefore gets full CRUD within its tenant. The registered handlers (`Ignixa.Api/Registrations/ApplicationServicesRegistration.cs:479-510`) are exactly Authentication(10), TenantIsolation(20), Rbac-or-Sidecar(30), SmartScope(40) — nothing denies. ADR-2501's "5. Data Filtering" layer and the documented priority-50 capability handler (`Handlers/IAuthorizationHandler.cs:23`) do not exist in this pipeline.
**Recommendation**: Add a terminal default-deny: if the request reached the end of the pipeline and neither an RBAC grant nor a SMART scope match affirmatively authorized it, deny. Concretely, make RBAC's "no roles" path return `Denied` unless SMART context exists, or add a priority-100 `DefaultDenyHandler` that checks an "affirmatively granted" flag on the context.
**Effort**: S

### P0 — ConformanceState: unsynchronized reads of mutable dictionaries concurrent with locked writes
**Location**: `src/Application/Ignixa.Application/Features/Conformance/ConformanceState.cs:11-16, 71-92, 158-342`; `Features/Conformance/ActiveSearchParameter.cs:22-23`
**Issue**: Writers (`Apply` via `InitializeFromEventsAsync`/`ApplyAndTrack`/`CatchUpAsync`) mutate plain `Dictionary<,>` instances under `_activationLock`, but readers (`GetSearchParameter`, `GetEnabledSearchParameter`, `EnabledSearchParameters`, `AllSearchParameters`, `FindByCanonical`) take no lock at all. ADR-2512 explicitly makes this the query-time hot path ("query-time resolution becomes a single O(1) dictionary lookup"), so request threads read these dictionaries while package activation writes them. `Dictionary<TKey,TValue>` is not safe for read-during-write — this can throw or return corrupt state. `ActiveSearchParameter.Status`/`ReindexJobId` are also mutable setters flipped by writers while readers filter on `Status`, so even key-stable updates race. Secondary smells confirm the confusion: `Interlocked.Increment(ref _nextSearchParamId) - 1` at line 52 mixed with plain `_nextSearchParamId = sp.SearchParamId + 1` at line 237, and `Interlocked.Read(_lastProcessedEventId)` paired with plain writes.
**Recommendation**: Either switch the three maps to `ConcurrentDictionary` and make `ActiveSearchParameter` immutable (replace-on-change), or adopt an immutable-snapshot pattern: build a new projection under the lock and swap a single `volatile` reference readers dereference. The snapshot swap is cleaner and matches the event-sourced replay model.
**Effort**: M

### P0 — /metadata throws for R6 tenants: OperationsSegment version switch omits R6
**Location**: `src/Application/Ignixa.Application/Features/Metadata/Segments/OperationsSegment.cs:204-214`
**Issue**: `GetFhirVersionString` handles R4, R4B, R5, Stu3 and throws `ArgumentOutOfRangeException` for everything else. `FhirVersion.R6` exists and is wired (`Features/Search/FhirVersionContext.cs:73` creates `R6CoreSchemaProvider`; `CompositeStructureDefinitionSummaryProvider.cs:282` maps R6). Any capability-statement build for an R6 tenant crashes in `ApplyAsync` line 47 — this also breaks `CapabilityEnforcementBehavior`, which builds the capability statement for every guarded request, so R6 tenants likely fail far beyond `/metadata`.
**Recommendation**: Add the R6 case. Better: delete this private switch and use the existing shared `FhirVersion`→string conversion (`ToVersionString()` used in `FhirVersionContext.cs:114`) so new versions can't silently miss one of N hand-rolled switches. Audit the sibling switches (`SidecarMetricsService.ParseFhirOperation` style maps are fine; version maps are not).
**Effort**: S

### P0 — OIDC discovery deserialization cannot bind snake_case fields; SMART configuration endpoint is dead on arrival
**Location**: `src/Application/Ignixa.Application/Features/Authorization/Services/OidcDiscoverySmartConfigurationProvider.cs:25-28, 70-71, 128-129, 143-155`
**Issue**: `OidcDiscoveryDocument` has PascalCase properties (`AuthorizationEndpoint`, `JwksUri`, …) with no `[JsonPropertyName]` attributes, and the serializer options set only `PropertyNameCaseInsensitive = true`. The OIDC discovery document uses snake_case (`authorization_endpoint`, `jwks_uri`) per RFC 8414 — case-insensitivity does not bridge underscores, so every field except `Issuer` binds to null and line 128 throws `InvalidOperationException("authorization_endpoint missing from discovery")` on every fetch. There is no test covering this class. Additionally, `GetConfigurationAsync` caches per-`tenantId` keys while `FetchConfigurationAsync` ignores the tenant entirely — N identical cache entries.
**Recommendation**: Set `PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower` (or add `[JsonPropertyName]` attributes), and add a deserialization test against a real discovery document. Drop the per-tenant cache key or actually make the fetch tenant-aware.
**Effort**: S

### P1 — Composite schema provider eager-load is dead code guarded by a live invalidation pipeline
**Location**: `src/Application/Ignixa.Application/Features/Specification/CompositeStructureDefinitionSummaryProvider.cs:75-193, 112-116, 252-264`; `Features/Specification/CompositeSchemaProviderRegistry.cs`; `Events/Package/PackageLoadedNotificationHandler.cs`; `Utilities/DebounceInvalidationStrategy.cs`
**Issue**: `InitializeAsync` — the only thing that populates `_packageStructureDefinitions` — has **zero callers** in the entire solution. On top of that, the code itself admits the conversion is unimplemented (`// TODO: ... For now, this returns null until ToTypeDefinition() is fully implemented`, line 112). So package-profile type resolution never happens, `GetTypeDefinition` always falls through to base spec, and `ClearCache()` (line 252: "Requires re-initialization via InitializeAsync() after clearing") resets `_isInitialized` for an initialization that never occurs. Meanwhile a four-stage pipeline exists solely to invalidate this empty cache: package events → `PackageLoaded/UnloadedNotificationHandler` → `CompositeSchemaProviderRegistry` (whose `packageId` parameter is ignored and whose two interface methods are identical) → `DebounceInvalidationStrategy` (243 lines of timer/CTS management). This is the definitive early-agent artifact in the subsystem: elaborate, plausible-looking infrastructure around a feature that does nothing.
**Recommendation**: Decide the feature's fate. If package profiles must feed schema resolution, implement `ToTypeDefinition`, call `InitializeAsync` during tenant preload and after debounced invalidation, and add a test asserting a loaded profile resolves. If ADR-2512's `ConformanceState.StructureDefinitions` is the intended source of truth instead, delete the composite provider's eager-load path, the registry, the debounce strategy, and both notification handlers.
**Effort**: L

### P1 — DataLayer references Application: layering inversion around package events
**Location**: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Ignixa.DataLayer.SqlEntityFramework.csproj:32`; `src/Application/Ignixa.Application/Events/Package/*.cs`; `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Events/PackageLoadedSearchParameterSyncHandler.cs:146`
**Issue**: ADR-2509 mandates `DataLayer → Domain` only, yet `Ignixa.DataLayer.SqlEntityFramework` has a `ProjectReference` to `Ignixa.Application` so its event handlers can consume `IPackageLoaded`/`ICapabilityCacheInvalidator` and publish `TerminologyImportTriggeredEvent`. Consequences beyond the dependency arrow: capability-cache invalidation on package **load** happens only in the DataLayer's `PackageLoadedSearchParameterSyncHandler` (a class ADR-2512 lists as part of the legacy flow to be removed), while the Application-layer `PackageLoadedNotificationHandler` does not invalidate the capability cache at all — unload and load invalidation live in different layers, and removing the legacy sync handler per ADR-2512 would silently leave `/metadata` stale for up to the 1-hour TTL after package loads.
**Recommendation**: Move the package/terminology event contracts to `Ignixa.Domain` (they are already POCO records with a Medino marker), or into a small shared contracts project, and remove the DataLayer→Application reference. Add capability invalidation to the Application-layer load handler so behavior survives the planned removal of the legacy sync handler.
**Effort**: M

### P1 — Capability version-hash validation is unreachable; cache freshness rests solely on TTL and explicit invalidation
**Location**: `src/Application/Ignixa.Application/Features/Metadata/CapabilityStatementService.cs:30-32, 69-101, 110-122, 231-245`
**Issue**: The "smart caching with version hash validation" compares the cached entry's hash to a "current" hash — but the current hash is read from `_versionHashCache`, which is populated at the same moment as the entry and removed only in `InvalidateCacheAsync` (which also removes the entry itself). Both caches live and die together, so the mismatch branch at line 96 can never execute; every `GetVersionHashAsync` implementation across six segments (including SHA256 computations over all search parameters and reference targets) feeds a mechanism that cannot fire. Worse, step 4 (line 112) can attach a **stale** pre-rebuild hash to a freshly built statement. The entire `ICapabilitySegment.GetVersionHashAsync` surface is currently ballast.
**Recommendation**: Either make it real — recompute the composite hash on each validation (or on a short interval) instead of caching it beside the entry — or delete `GetVersionHashAsync` from the segment interface and rely on TTL + event-driven invalidation, which is what actually protects freshness today. Deleting is the honest option; the segments' hash code is nontrivial and all dead.
**Effort**: M

### P1 — MemoryCapabilityCache.ClearAsync is a silent no-op behind "All capability caches cleared" logs; three of four invalidator methods have no callers
**Location**: `src/Application/Ignixa.Application/Infrastructure/Caching/MemoryCapabilityCache.cs:58-69`; `Infrastructure/Caching/CapabilityCacheInvalidator.cs:36-63`; `Infrastructure/Caching/ICapabilityCacheInvalidator.cs`
**Issue**: `ClearAsync` intentionally does nothing (comment: "this is a no-op") yet `CapabilityCacheInvalidator.InvalidateAllAsync` logs "All capability caches cleared" after calling it, and `CapabilityStatementService.ClearCacheAsync` logs "Cleared all capability caches". Nothing was cleared — and `CapabilityStatementService._versionHashCache` isn't touched either. Mitigating factor that is itself a finding: `InvalidateAllAsync`, `InvalidateForProfileChangesAsync`, and `InvalidateForSearchParameterChangesAsync` have zero call sites; only `InvalidateForTenantAsync` is used. The interface is 75% speculative API.
**Recommendation**: Delete the three unused methods. If an "invalidate all" is ever needed, implement it properly (track keys, or wrap `MemoryCache` with a generation token via `CancellationChangeToken`). Never log success for a no-op.
**Effort**: S

### P1 — PackageResourceMapper silently drops malformed conformance resources
**Location**: `src/Application/Ignixa.Application/Features/Conformance/PackageResourceMapper.cs:141-144, 175-178`
**Issue**: Both `MapSearchParameter` and `MapStructureDefinition` end in a bare `catch { return null; }` — no logging, no issue collection. A SearchParameter with malformed JSON, or one missing `code`/`expression`/`type`, simply vanishes from activation. This directly contradicts ADR-2512, whose stated motivation includes eliminating "silent failures when sync handlers catch and log exceptions" and whose design point is that "package activation validates the entire configuration atomically." A package can "activate successfully" while indexing definitions were dropped on the floor.
**Recommendation**: Return mapping failures as `ValidationIssue`s (e.g., `SP_PARSE_ERROR` with the resource id) so `PackageActivationPipeline.ValidateActivation` surfaces them in `ActivationResult.Issues`. At minimum, log at Warning with the exception and resource identity.
**Effort**: S

### P1 — LoadPackageHandler reports success when activation failed; unvalidated `int.Parse(TenantId)` after the import already committed
**Location**: `src/Application/Ignixa.Application/Features/Admin/LoadPackageHandler.cs:93-113, 116-122`; `Features/Admin/LoadPackageCommand.cs`
**Issue**: Two problems. (1) When `ActivateAsync` fails, the handler logs a Warning and returns a normal `LoadPackageResult` — the API caller has no way to know the package's search parameters were never activated. `LoadPackageResult` has no activation status field. (2) `int.Parse(request.TenantId)` at line 120 runs after the import and activation succeeded; a non-numeric tenant id throws `FormatException` at the very end, producing a 500 for an operation that actually completed — and skipping the cache-invalidation event. Root cause: Admin commands model `TenantId` as `string` while every other layer (events, config store, partition strategy) uses `int`. `ImplementationGuideProvider.LoadPackageInternalAsync:85` even re-parses it back to int with proper validation, so the string round-trips twice.
**Recommendation**: Change `LoadPackageCommand`/`UnloadPackageCommand`/`ListPackagesQuery` to `int TenantId` and parse/validate once at the API boundary. Add `ActivationSucceeded`/`ActivationIssues` to `LoadPackageResult` (or fail the request when activation fails — a loaded-but-unactivated package is not a success under ADR-2512's explicit-activation model).
**Effort**: M

### P1 — Package management tenant scoping is cosmetic: list and unload operate globally
**Location**: `src/Application/Ignixa.Application/Features/Packages/ImplementationGuideProvider.cs:305-329, 339-377`
**Issue**: `ListLoadedPackagesAsync(tenantId, …)` validates and logs the tenant id, then calls `_packageRepository.ListLoadedPackagesAsync(cancellationToken)` — the comment admits "Phase 1 limitation - returns packages from global repository for all tenants." `UnloadPackageAsync` likewise deactivates globally: tenant A's admin can deactivate a package tenant B depends on. `LoadPackageInternalAsync` (line 85-88) *does* scope its existence check by tenant, so the same class is half tenant-aware. Under CLAUDE.md's multi-tenancy rules this is an isolation gap on an admin surface, and the signatures actively mislead callers (`ProfileCapabilitySegment` passes a tenant id believing it means something).
**Recommendation**: Thread the tenant id into `IPackageResourceRepository.ListLoadedPackagesAsync`/`DeactivatePackageAsync` (the load path proves the repository already partitions by tenant), or — if package state is intentionally global for now — remove the tenantId parameters and say so in the interface docs instead of a buried NOTE.
**Effort**: M

### P1 — Audit logging: fire-and-forget with silent loss, documentation claims the opposite, and fabricated audit values
**Location**: `src/Application/Ignixa.Application/Infrastructure/SidecarAuditLogger.cs:14-17, 30-51, 65-72, 141-144`
**Issue**: The class doc says "Fail-fast: Throws RpcException if sidecar is unavailable (returns 503 to client)" but every method is `_ = SomethingAsync(...)` fire-and-forget with all exceptions caught and logged — audit events are droppable by design, contradicting the doc and the usual compliance expectation for healthcare audit trails (contrast `SidecarRbacAuthorizationHandler`, which really does fail fast). The unawaited tasks also outlive the request with no queue/backpressure (unlike `SidecarLoggerProvider`, which got a bounded channel). Fabricated data compounds it: `LogTenantAccessAsync` invents `HttpStatusCode = authorized ? 200 : 403`, and `LogTtlDeletionAsync` hardcodes `IpAddress = "127.0.0.1"`.
**Recommendation**: Pick a guarantee and implement it: either fail-fast (await, propagate `Unavailable`) or reliable-async (bounded channel + background flusher like `SidecarLoggerProvider`, with a dropped-event counter). Fix the doc either way; stop synthesizing status codes and IPs — omit unknown fields.
**Effort**: M

### P1 — HttpContext and web-stack types embedded in the Application layer
**Location**: `src/Application/Ignixa.Application/Ignixa.Application.csproj:21-23` (`Microsoft.AspNetCore.Http`, `Microsoft.AspNetCore.OpenApi`); `Infrastructure/IPipelineExecutor.cs:21`; `Infrastructure/FhirVersionExtractor.cs:31`; `Features/Authorization/Models/FhirAuthorizationContext.cs:81` (`required HttpContext`); `Features/Authorization/Handlers/SmartScopeAuthorizationHandler.cs:104`; `Handlers/SidecarRbacAuthorizationHandler.cs:51`
**Issue**: The Application layer takes a hard dependency on ASP.NET Core's `HttpContext`. `FhirAuthorizationContext` — the core authorization model — *requires* an `HttpContext`, which authorization handlers then reach into (`Request.Method`, `TraceIdentifier`). `IPipelineExecutor` is an Application-defined interface whose contract is "execute an HttpContext". This makes the authorization pipeline untestable without faking a web server and violates the ADR-2509 layer diagram, where HTTP is the API layer's concern. The pattern of `IFhirRequestContext` shows the team already knows the right shape — HTTP facts get projected into a neutral context; the authorization slice just never got that treatment.
**Recommendation**: Replace `FhirAuthorizationContext.HttpContext` with the specific facts consumed (`HttpMethod` string, `CorrelationId`) — two properties cover all current uses. Move `FhirVersionExtractor` to the API layer (it is only meaningful there). `IPipelineExecutor` is harder (bundle re-entrancy is designed around synthetic HttpContexts) — at minimum document it as a sanctioned exception in an ADR rather than leaving it implicit.
**Effort**: M

### P1 — Foreign namespaces compiled into the Application assembly
**Location**: `src/Application/Ignixa.Application/Features/Packages/ImplementationGuideProvider.cs:8` and `PackageResourceImporter.cs:8` (`namespace Ignixa.PackageManagement.Infrastructure`); `IImplementationGuideProvider.cs:3`, `IPackageResourceImporter.cs:4` (`Ignixa.PackageManagement.Abstractions`); `PackageImportResult.cs:1` (`Ignixa.PackageManagement.Models`); `Infrastructure/ResourceReferenceHelper.cs:13` (`namespace Ignixa.Serialization.Helpers`)
**Issue**: Six files in the Application project declare namespaces belonging to the Core `Ignixa.PackageManagement` and `Ignixa.Serialization` packages. Anyone reading a stack trace or a `using Ignixa.PackageManagement.Abstractions;` line will look for these types in the wrong project, and since `Ignixa.Application` also *references* `Ignixa.PackageManagement`, the same namespace is now split across two assemblies — a collision waiting to happen if Core ever adds a type of the same name. This is classic copy-migration residue.
**Recommendation**: Either move these files into the actual `Ignixa.PackageManagement` project (they have no Application dependencies — check `KnownPackages` usage) or rename the namespaces to `Ignixa.Application.Features.Packages` / `Ignixa.Application.Infrastructure` and fix usings.
**Effort**: S

### P1 — ResourceReferenceHelper.UpdateReference destroys sibling reference fields
**Location**: `src/Application/Ignixa.Application/Infrastructure/ResourceReferenceHelper.cs:103, 110, 272-279`
**Issue**: Updating a reference replaces the whole node with `CreateReferenceJsonObject`, i.e. `{ "reference": "Patient/456" }` — any existing `display`, `type`, `identifier`, or extensions on the original Reference are silently deleted. For bundle reference rewriting (its apparent use), losing `display` mutates clinical data beyond the intended id swap. Additionally, `elementPath` lookups are top-level only (`jsonObject.TryGetPropertyValue(elementPath, …)`), so nested references (e.g., `Patient.contact.organization`) can never be found or updated — whether that's a real gap depends on what `IReferenceMetadataProvider` emits, but the helper can't handle what the metadata type's name implies. `ResourceReferenceExtensions.cs` is a one-method pass-through wrapper adding nothing.
**Recommendation**: Mutate only the `reference` property of the existing JsonObject (`node["reference"] = newValue`). Verify metadata never emits nested paths, or implement path traversal. Delete the extensions wrapper or the helper's duplicate entry point — one API is enough.
**Effort**: S

### P1 — `EnableV1ScopeCompatibility` is dead configuration: v1 scopes are always accepted
**Location**: `src/Application/Ignixa.Application/Features/Authorization/SmartOptions.cs:30`; `Features/Authorization/Smart/SmartScopeParser.cs:80-87`
**Issue**: `SmartOptions.EnableV1ScopeCompatibility` (default false, present in `appsettings.json`) is read by nothing. `SmartScopeParser.ParseScope` unconditionally falls back to the v1 regex, so v1 scopes (`patient/Observation.read`) are always honored regardless of configuration. An operator who believes they've disabled legacy scope acceptance has not. The parser is static, so wiring the option requires a design decision, not just a null check.
**Recommendation**: Either plumb the flag (make the parser instance-based or pass an options struct into `ParseScope`) or delete the option and document that v1 compatibility is always on. A config knob that does nothing is worse than no knob.
**Effort**: S

### P1 — CapabilityEnforcementBehavior: generic exception for authz denial, per-request statement materialization, arbitrary version fallback
**Location**: `src/Application/Ignixa.Application/Infrastructure/Behaviors/CapabilityEnforcementBehavior.cs:93-121, 130-152`
**Issue**: (1) Denial throws bare `InvalidOperationException` relying on middleware to translate it to 403 — but `InvalidOperationException` is thrown for dozens of unrelated failures across the codebase (including inside this very behavior's dependencies, e.g. "Tenant X not found"), so genuine server bugs can masquerade as 403 capability denials and vice versa. A dedicated exception type exists as prior art (`ValidationException` in `ValidationBehavior`). (2) Every guarded request calls `capabilityStatement.ToElement(provider)` and compiles/evaluates a FHIRPath expression against the full capability statement — the statement is cached but the element projection is rebuilt per request; ADR-2501 budgets <0.5ms for this layer, which per-request `ToElement` of a document with hundreds of resources will not meet. (3) When no request context exists, `GetFhirVersionForTenantAsync` silently uses "first tenant's version" — cross-tenant behavior leakage identical to the pattern in `GetCapabilityStatementHandler.cs:62-68`. (4) The XML doc example references `IRequiresCapability`; the interface is `IRequireCapability`.
**Recommendation**: Introduce `CapabilityNotSupportedException` mapped to 403+OperationOutcome. Cache the `ToElement` projection alongside the statement in `CapabilityCacheEntry` (it's invalidated at the same time). Make the no-context fallback explicit: default R4 or throw, but never "whichever tenant is listed first".
**Effort**: M

### P1 — DebounceInvalidationStrategy: `AddOrUpdate` factories with side effects leak live timers
**Location**: `src/Application/Ignixa.Application/Utilities/DebounceInvalidationStrategy.cs:70-73, 96-100, 110-151`
**Issue**: `ConcurrentDictionary.AddOrUpdate` may invoke its factories multiple times under contention and discard all but one result. Both factories here have side effects: `CreateNewDebounceState` starts a live `Timer`; `ResetDebounceTimer` *disposes the existing state's timer* and starts a new one. Under concurrent `RequestInvalidation` calls for the same tenant, a discarded factory result leaves an orphaned armed timer (double invalidation — benign here, but the callback also races `_debounceStates.TryRemove` cleanup), and the update factory's disposal of `existing` executes even when its result loses the race. The class works in the common single-threaded case, which is why it's survived.
**Recommendation**: Replace the AddOrUpdate dance with a per-tenant lock or `lock`-guarded plain dictionary — this is a low-frequency admin path; `ConcurrentDictionary` cleverness buys nothing. Given the P1 dead-feature finding above, this class may simply be deleted along with its only consumer.
**Effort**: S

### P1 — FhirRequestContextAccessor: static AsyncLocal contradicts scoped registration; two types per file
**Location**: `src/Application/Ignixa.Application/Infrastructure/FhirRequestContextAccessor.cs:18-33, 39-101`
**Issue**: The accessor's comment says "Registered as SCOPED service in DI container" but the backing store is a `private static readonly AsyncLocal<>` — all instances share one store, so scoped lifetime is meaningless (harmless today, but a maintainer adding per-instance state will introduce a cross-request leak that the "scoped" label hides). The file also contains both `FhirRequestContextAccessor` and `FhirRequestContext` (one-type-per-file violation), and `IFhirRequestContext.cs` likewise bundles `OperationOutcomeIssue`.
**Recommendation**: Register singleton and fix the comment (matching how ASP.NET Core's own `HttpContextAccessor` works), split the files.
**Effort**: S

### P1 — RBAC ReadOnly/read grants don't cover search, vread, or history
**Location**: `src/Application/Ignixa.Application/Features/Authorization/Handlers/InMemoryRolePermissionStore.cs:55-56, 60-63`; `Features/Authorization/Models/ResourceGrant.cs:30-39`; `Models/FhirAuthorizationContext.cs:116-117`
**Issue**: `RequiredPermission` uses FHIR interaction codes (`read`, `vread`, `search-type`, `history-instance`, …) and `ResourceGrant.Matches` does exact string comparison. The built-in `ReadOnly` role grants `("*", "read")` — which matches only the `read` interaction. A ReadOnly user cannot search, vread, or view history; the `Clinician` role's `("Practitioner", "read")` has the same problem. Unlike SMART scopes (where `MapInteractionToPermission` folds vread/history into Read), RBAC has no interaction-family mapping. The role is unusable as intended and nobody noticed, which suggests RBAC-only deployments are untested.
**Recommendation**: Add an interaction-family mapping to `ResourceGrant.Matches` (read ⊇ {read, vread, history-instance, history-type, search-type}), mirroring `SmartScope.MapInteractionToPermission`, or define grants in terms of the CRUDS permission flags shared with SMART.
**Effort**: S

### P1 — ProfileCapabilitySegment: cache-type abuse, serialize/deserialize round-trip per build, hardcoded tenant "1", parallel source of truth
**Location**: `src/Application/Ignixa.Application/Features/Metadata/Segments/ProfileCapabilitySegment.cs:71, 161, 204-317`
**Issue**: (1) The segment smuggles a `List<PackageResource>` (full StructureDefinition JSON blobs) through `ICapabilityCache` by stuffing serialized JSON into `CapabilityCacheEntry.Statement.MutableNode["_profileData"]` — "repurposed for segment-level caching" per its own comment. Every cache *hit* pays `ToJsonString()` + `JsonSerializer.Deserialize<List<PackageResource>>` over all loaded profiles, and `ApplyAsync` + `GetVersionHashAsync` each do it (twice per capability build). (2) `context.TenantId?.ToString() ?? "1"` hardcodes tenant 1 as the system-wide default in two places — system `/metadata` shows tenant 1's profiles, and the fallback disagrees with `CapabilityContext.ToCacheKey()`'s `"default"`. (3) The segment queries `IImplementationGuideProvider`/`IPackageResourceRepository` directly while ADR-2512's `ConformanceState._structureDefinitions` holds the same data as an event-sourced projection — two competing sources of truth for "active profiles".
**Recommendation**: Give segment-level caching its own typed `IMemoryCache` entry (a `record ProfileData` is already defined — cache it directly instead of via JSON). Remove the tenant-"1" default; system-wide should aggregate or omit profiles explicitly. Longer term, read profiles from `ConformanceState`.
**Effort**: M

### P1 — CompositeStructureDefinitionSummaryProvider: sync-over-async in property getter; invalidation leaves the provider permanently uninitialized
**Location**: `src/Application/Ignixa.Application/Features/Specification/CompositeStructureDefinitionSummaryProvider.cs:36-39, 219-221, 252-264, 352-390`; `Features/Search/FhirVersionContext.cs:338`
**Issue**: (1) `ResourceTypeNames` → `ComputeResourceTypeNames` → `GetCustomResourceTypesFromPackages` blocks on `Task.Run(...).GetAwaiter().GetResult()` inside a `Lazy` — a database query behind a synchronous property getter, called from capability-statement builds on request threads. The same pattern appears at `FhirVersionContext.cs:338`. CLAUDE.md forbids `.Result`/`.Wait()`; `Task.Run` avoids classic deadlock but burns a threadpool thread and hides latency in a property. (2) `GetTypeDefinition` negative-caches base-provider misses forever (`_cache[typeName] = baseType` even when null); `ClearCache()` resets `_isInitialized = false` and expects re-initialization that (per the P1 dead-feature finding) never comes, so post-invalidation the provider silently degrades to base-spec-only. `_isInitialized` is also a plain bool read/written across threads.
**Recommendation**: If the provider survives the dead-feature decision: make `ResourceTypeNames` computation async-initialized during tenant preload (it already has an `InitializeAsync` lifecycle — use it), make `_isInitialized` `volatile`, and have the debounced invalidation call `InitializeAsync` after `ClearCache`.
**Effort**: M

### P2 — One-type-per-file violations across the subsystem
**Location**: `Features/Admin/ListPackagesQuery.cs` (3 types), `Features/Admin/LoadPackageCommand.cs` (2), `Features/Admin/UnloadPackageCommand.cs` (2), `Features/Authorization/AuthorizationOptions.cs` (3), `Features/Authorization/Smart/SmartScope.cs` (3), `Smart/SmartAuthorizationContext.cs` (2), `Smart/SpecialScope.cs` (2), `Features/Authorization/Services/ISmartConfigurationProvider.cs` (2), `Services/OidcDiscoverySmartConfigurationProvider.cs` (2), `Features/Conformance/ActivationResult.cs` (2), `Features/Conformance/PackageResources.cs` (2), `Features/Conformance/SearchParameterInfo.cs` (2), `Infrastructure/FhirRequestContextAccessor.cs` (2), `Infrastructure/IFhirRequestContext.cs` (2), `Infrastructure/SidecarLogger.cs` (3 public types)
**Issue**: CLAUDE.md's ONE TYPE PER FILE rule is violated in at least 15 files in scope. Enum-plus-record pairs are defensible; three public types in `SidecarLogger.cs` and query+result+DTO bundles in Admin are not.
**Recommendation**: Split mechanically; prioritize files with 3+ types or public non-nested types.
**Effort**: M

### P2 — Log-and-rethrow catch blocks add noise without value
**Location**: `Features/Admin/ListPackagesHandler.cs:61-65`, `LoadPackageHandler.cs:128-135`, `UnloadPackageHandler.cs:83-90`; `Features/Packages/ImplementationGuideProvider.cs:127-134, 324-328, 369-376`; `Features/Packages/PackageResourceImporter.cs:132-140`
**Issue**: Seven `catch (Exception ex) { _logger.LogError(...); throw; }` blocks whose only content is context already present in the exception path and the preceding Information logs. CLAUDE.md: "Log once at the boundary where you handle it. Don't log-and-rethrow."
**Recommendation**: Delete the try/catch wrappers; the exception middleware is the logging boundary.
**Effort**: S

### P2 — Argument validation via InvalidOperationException; `ct` parameter names
**Location**: `Features/Admin/LoadPackageHandler.cs:31-36`, `UnloadPackageHandler.cs:42-47`, `ListPackagesHandler.cs:37-38` (should be `ArgumentException`); `Infrastructure/AppSettingsTenantConfigurationStore.cs:97,114`, `CompositeRepositoryFactory.cs:38`, `CompositeSearchServiceFactory.cs:38`, `PassthroughExecutionStrategy.cs:40,76` (parameter named `ct`, CLAUDE.md mandates `cancellationToken`)
**Issue**: Wrong exception type for bad arguments; `ct` naming contradicts the project's own critical-violations table. The `ct` names originate in Domain interfaces — fix them there and ripple.
**Recommendation**: Mechanical rename + exception-type swap.
**Effort**: S

### P2 — Copyright header chaos: "Microsoft Corporation" on Ignixa code, three header styles, and headerless files
**Location**: Pervasive — e.g. `Infrastructure/ApplicationVersionInfo.cs:1-4` (Microsoft), `Infrastructure/Behaviors/ValidationBehavior.cs:1-4` (Ignixa Contributors), `Infrastructure/ResourceReferenceExtensions.cs:1-4` (XML-style Microsoft), `Features/Admin/*.cs`, `Features/Conformance/ConformanceState.cs` (no header)
**Issue**: Most files claim "Copyright (c) Microsoft Corporation" — presumably template residue from the Microsoft FHIR Server. For a project publishing NuGet packages this is a legal-hygiene problem, not just style.
**Recommendation**: Pick one header (or none), enforce via `.editorconfig` `file_header_template`, and sweep.
**Effort**: S

### P2 — ValidationBehavior: vacuous depth condition and misleading skip branch
**Location**: `src/Application/Ignixa.Application/Infrastructure/Behaviors/ValidationBehavior.cs:79, 145-151`
**Issue**: `if (validationDepth != ValidationDepth.Minimal || validationDepth == ValidationDepth.Spec || validationDepth == ValidationDepth.Full)` — the two `||` arms are unreachable given the first (a value equal to Spec or Full already satisfies `!= Minimal`). The `else` branch then logs "Validation running with minimal depth" while actually skipping validation entirely. Also, `ValidationBehavior` is a non-generic `IPipelineBehavior<CreateOrUpdateResourceCommand, ResourceKey>` living in Infrastructure/Behaviors while coupled to a specific Resource-feature command — it's feature logic, not infrastructure.
**Recommendation**: Simplify to `if (validationDepth != ValidationDepth.Minimal)`, fix the log message, and consider relocating next to the Resource feature.
**Effort**: S

### P2 — Capability statement advertises unimplemented-verified capabilities and omits supported ones
**Location**: `src/Application/Ignixa.Application/Features/Metadata/Segments/ResourceInteractionCapabilitySegment.cs:83-101, 134-151`; `Features/Metadata/CapabilityStatementService.cs:165`
**Issue**: Every resource type unconditionally advertises `ConditionalCreate`, `ConditionalUpdate`, `ConditionalDelete=Single`, `UpdateCreate`, `ReadHistory` — while the interaction list omits `vread`, `history-instance`, `history-type`, and `patch` even though the server implements them (`FhirInteraction` enumerates them; History/Patch features exist). Under ADR-2501 the CapabilityStatement is an enforced contract: interactions not advertised may be denied by capability enforcement, and flags advertised but unimplemented break conformance. Also `CapabilityStatementService.cs:165` hardcodes `statement.Url = "http://Ignixa.example.com/fhir/CapabilityStatement"` — an example.com URL served in production metadata.
**Recommendation**: Advertise the full implemented interaction set (vread/history/patch) and derive conditional flags from actual feature registration. Make the canonical URL configurable from server base URL.
**Effort**: M

### P2 — IncludeRevInclude segment ignores tenant; two segments share priority 40
**Location**: `src/Application/Ignixa.Application/Features/Metadata/Segments/IncludeRevIncludeCapabilitySegment.cs:38, 48, 123`; `Segments/ProfileCapabilitySegment.cs:49`
**Issue**: `GetSearchParameterDefinitionManager(context.FhirVersion)` omits `context.TenantId` (contrast `SearchParameterCapabilitySegment.cs:48` which passes it), so tenant-package reference parameters never appear in `searchInclude`/`searchRevInclude`. Both this segment and ProfileCapabilitySegment declare `Priority => 40`, making their relative order registration-order-dependent.
**Recommendation**: Pass the tenant id; renumber priorities uniquely.
**Effort**: S

### P2 — `IsBaseFhirPackage` logic triplicated
**Location**: `Features/Conformance/PackageActivationPipeline.cs:278-280`, `Features/Conformance/ConformanceState.cs:340-342`, plus `KnownPackages.IsCorePackage` (Ignixa.PackageManagement) used in `LoadPackageHandler.cs:39`
**Issue**: The "is this a core FHIR package" predicate exists in at least three places with two different implementations (`hl7.fhir.r*.core` prefix/suffix vs `KnownPackages.CorePackages` list). Divergence here changes which SearchParameters skip the reindex lifecycle — a behavioral fork waiting to happen.
**Recommendation**: Single predicate on `KnownPackages`; delete the private copies.
**Effort**: S

### P2 — Azure resource-provider string in Ignixa RBAC sidecar contract
**Location**: `src/Application/Ignixa.Application/Features/Authorization/Handlers/SidecarRbacAuthorizationHandler.cs:88-92`
**Issue**: `BuildDataAction` emits `Microsoft.HealthcareApis/fhir/{INTERACTION}` — the Azure Healthcare APIs RBAC action namespace, copied verbatim into a non-Azure product's authorization protocol. Any sidecar implementation must now match Azure's action-string format forever.
**Recommendation**: Define an Ignixa-native data-action format in the sidecar proto contract.
**Effort**: S

### P2 — Event abstraction inconsistency: interface events vs concrete-record events
**Location**: `Events/Package/IPackageLoaded.cs`/`IPackageUnloaded.cs`/`Events/Terminology/ITerminologyImportTriggered.cs` vs their records; `Ignixa.Application.BackgroundOperations/Terminology/EventHandlers/TerminologyImportTriggeredHandler.cs:19`; `Ignixa.Api/Services/SqlReferenceDataPreloadService.cs:24`
**Issue**: Package events define interface + record pairs and handlers subscribe to the interfaces; terminology and startup events are handled by their concrete records (`INotificationHandler<TerminologyImportTriggeredEvent>`), making `ITerminologyImportTriggered` decorative. One event (`TenantPackagePreloadCompletedEvent`) has no interface at all. Three patterns for one mechanism; the interface layer buys nothing since the records are already immutable contracts.
**Recommendation**: Drop the event interfaces; publish and subscribe to the records.
**Effort**: S

### P2 — PackageLoaded/Unloaded handler asymmetry within the Application layer
**Location**: `Events/Package/PackageLoadedNotificationHandler.cs` vs `PackageUnloadedNotificationHandler.cs:52-58`
**Issue**: The unload handler invalidates both schema registry and capability cache; the load handler invalidates only the schema registry — load-side capability invalidation lives in the DataLayer legacy handler (see the P1 layering finding). Also inconsistent ctor null-checking within the pair (unload null-checks one of three params, load none).
**Recommendation**: Mirror the invalidation set in both handlers.
**Effort**: S

### P2 — ConditionalHeaderParser: no `*`, no ETag lists, no strict HTTP-date parsing
**Location**: `src/Application/Ignixa.Application/Utilities/ConditionalHeaderParser.cs:16-56`
**Issue**: `If-None-Match: *` (a meaningful conditional-create guard per RFC 7232 §3.2) parses to the literal `"*"` rather than being recognized; multi-value headers (`"5", "7"`) parse to garbage (`5", "7`); `ParseIfModifiedSince` uses culture-sensitive `DateTimeOffset.TryParse` instead of the `"R"` format it emits. Also redundant `using System;` under ImplicitUsings, and HTTP-header parsing arguably belongs in the API layer.
**Recommendation**: Handle `*` explicitly, split on commas, parse with `DateTimeOffset.TryParseExact(..., "R", CultureInfo.InvariantCulture, ...)` with lenient fallback.
**Effort**: S

### P2 — Misleading comments: partition-0 example, PE-header claim, phase-number archaeology
**Location**: `Infrastructure/IsolatedModePartitionStrategy.cs:18` ("Tenant 0 = Mayo Clinic" — partition 0 is the reserved system partition per CLAUDE.md/ADR-2510; the example teaches the forbidden pattern); `Infrastructure/ApplicationVersionInfo.cs:149-150` (comment says "build timestamp from linker timestamp in PE header", code reads `File.GetLastWriteTimeUtc`); pervasive "Phase 1.2/7/11/12/20.2" comments (`ICapabilityCache.cs:12`, `MemoryCapabilityCache.cs:13-14`, `ProfileCapabilitySegment.cs` etc.) that reference a roadmap no reader can consult
**Recommendation**: Fix the partition-0 example immediately (it's a security-relevant doc bug); delete or correct the rest.
**Effort**: S

### P2 — SidecarMetricsService passes the request token into a fire-and-forget task
**Location**: `src/Application/Ignixa.Application/Infrastructure/SidecarMetricsService.cs:27`
**Issue**: `_ = RecordMetricInternalAsync(metrics, cancellationToken)` — the request-scoped token can cancel the unawaited gRPC send when the request aborts, dropping the metric for exactly the requests you most want measured. `SidecarLoggerProvider.ProcessLogEntriesAsync` also has a dead `lastFlush` variable (lines 72, 112) and its `Dispose()` uses sync `.Wait(...)` alongside a proper `DisposeAsync`.
**Recommendation**: Use `CancellationToken.None` (or a service-lifetime token) for the detached send; delete `lastFlush`.
**Effort**: S

### P2 — Redundant `#nullable enable` directives and leftover TODO
**Location**: `Infrastructure/ResourceReferenceHelper.cs:6`, `Utilities/DebounceInvalidationStrategy.cs:6`, `Features/Specification/CompositeSchemaProviderRegistry.cs:6`, `CompositeStructureDefinitionSummaryProvider.cs:6`, `ICompositeSchemaProviderRegistry.cs:6` (project already has `<Nullable>enable</Nullable>`); `CompositeStructureDefinitionSummaryProvider.cs:112` (TODO in committed code, banned by CLAUDE.md checklist)
**Recommendation**: Delete the directives; resolve the TODO as part of the dead-feature decision.
**Effort**: S

### P2 — CompositeRepositoryFactory / CompositeSearchServiceFactory: duplicated routing with magic strings and ambiguous DI
**Location**: `Infrastructure/CompositeRepositoryFactory.cs:48-53`, `Infrastructure/CompositeSearchServiceFactory.cs:48-53`
**Issue**: Two near-identical classes switch on `"FileSystem"` / `"SqlEntityFramework" or "SqlServer"` string literals (defined nowhere central), and each takes two parameters of the *same interface type* distinguished only by parameter name — correct resolution depends entirely on Autofac positional/named registration, which a refactor can silently break.
**Recommendation**: Extract storage-type constants (or an enum on `TenantConfiguration.Storage`), and use keyed services so the container enforces which factory is which.
**Effort**: S

## Addendum: Cross-scope handoffs from background-operations review (verified)

The background-operations review found that `Ignixa.Application.Operations/Features/Transform` and `.../Terminology/{Expand,Subsumes,Translate}` are dead namespace-only duplicates of `Ignixa.Application/Features/Experimental/Transform` and `.../Experimental/Terminology` (its report recommends deleting the duplicates). Three behavioral issues in the **live** copies were handed off to this review and have been verified against the source:

### P1 — FhirPathEvaluatorWithTimeout: "timeout" abandons the thread, never cancels the work
**Location**: `src/Application/Ignixa.Application/Features/Experimental/Transform/FhirPathEvaluatorWithTimeout.cs:64, 102-108`; `Features/Experimental/Infrastructure/ExperimentalAutofacRegistration.cs:154-160`
**Issue**: `Task.Run(() => _evaluator.Evaluate(element, compiled), cts.Token)` — the token gates only the *start* of the task; `FhirPathEvaluator.Evaluate` receives no cancellation and runs to completion regardless. On timeout the caller gets `TimeoutException` while the runaway expression keeps burning a threadpool thread; repeated timeouts (the exact scenario this class exists for — pathological expressions) leak threads until pool starvation. The sync `Evaluate` wrapper blocks via `.GetAwaiter().GetResult()` (CLAUDE.md-banned pattern; here it means every FML transform step blocks a second thread waiting on the first). The 5-second timeout is hardcoded in the registration rather than configuration.
**Recommendation**: Thread a `CancellationToken` into `FhirPathEvaluator.Evaluate` (cooperative checks in the evaluation loop) so timeout actually stops work. If the evaluator can't be made cancellable, at minimum document the abandonment and add a runaway-expression counter so pool exhaustion is diagnosable. Move the timeout to options.
**Effort**: M

### P1 — ConceptMapResolverService sync wrapper converts infrastructure failure into "no translation"
**Location**: `src/Application/Ignixa.Application/Features/Experimental/Transform/ConceptMapResolverService.cs:122-148` vs `:96-110`
**Issue**: `TranslateAsync` deliberately wraps and rethrows failures ("Rethrow to let caller handle"); the sync `Translate` wrapper — the one the FML `translate()` transform actually calls — catches **all** exceptions and returns null. Null means "no translation found", so a terminology-service outage or a missing ConceptMap silently produces transformed resources with absent coded fields instead of a failed transform. That is data-quality corruption in output resources, and it directly contradicts the async method's documented contract two screens up in the same file.
**Recommendation**: Remove the catch-all in the sync wrapper — let the `InvalidOperationException` from `TranslateAsync` propagate so the transform fails loudly. If FML semantics require lenient translate, make leniency an explicit parameter, not an accident of which overload was called.
**Effort**: S

### P1 — MapRegistryCache: per-request lifetime nullifies both the cache and its invalidation handler
**Location**: `src/Application/Ignixa.Application/Features/Experimental/Transform/MapRegistryCache.cs`; `Events/PackageLoadedMapCacheInvalidationHandler.cs`; `ExperimentalAutofacRegistration.cs:142-146, 168-170`
**Issue**: `MapRegistryCache` is registered `InstancePerLifetimeScope` — every request gets a fresh, empty cache, so the class's documented performance profile ("Cache hit: ~1-5ms … Throughput: 200-500 transforms/sec (cached)") only holds within a single request. `PackageLoadedMapCacheInvalidationHandler` (`InstancePerDependency`) resolves the cache from the publishing request's scope, so it "invalidates" an instance that dies with that admin request — the handler, its statistics logging, and `InvalidatePackage` are all dead weight under this lifetime. Caution for the obvious fix: naively promoting the cache to `SingleInstance` would (a) share `Register()`-ed inline maps and repository-loaded maps **across tenants** — `GetOrLoadAsync`/`GetStructureMapByUrlAsync` key by canonical URL with no tenant discriminator — and (b) the singleton would need tenant-keying before the invalidation handler becomes safe to keep.
**Recommendation**: Decide the intended lifetime. If transforms are hot enough to justify caching, make the cache singleton keyed by `(tenantId, canonicalUrl)` and keep the invalidation handler; otherwise delete the handler, the statistics machinery, and the misleading performance claims, leaving a simple per-request registry.
**Effort**: M

## Architectural Observations

1. **Layer boundaries are the systemic weakness.** Three independent violations converge: `HttpContext`/ASP.NET packages inside `Ignixa.Application` (authorization models, `IPipelineExecutor`, `FhirVersionExtractor`), DataLayer referencing Application (package events), and Core-package namespaces compiled into the Application assembly. Each is individually explainable; together they mean ADR-2509's dependency diagram no longer describes the build graph. An ADR amendment or a cleanup epic should pick one story and enforce it (an architecture test asserting reference direction would prevent regression).

2. **ADR-2512 is half-executed and the halves disagree.** The event-sourced `ConformanceState` write path exists and is good, but: the legacy `PackageLoadedSearchParameterSyncHandler` flow ADR-2512 promised to remove still carries load-side cache invalidation; `ProfileCapabilitySegment` reads profiles from the package repository instead of the projection; and the composite schema provider builds a third profile source that is dead. Until reads converge on `ConformanceState`, every consumer must be audited against three sources of truth.

3. **"Belt-and-braces that doesn't buckle" is the signature early-agent pattern here.** The capability version-hash system, the schema-invalidation debounce chain, the four-method cache invalidator, and the event interface layer are all sophisticated-looking infrastructure whose critical path is unreachable, uncalled, or no-op. The subsystem would be *smaller and more correct* after deletion: roughly 800-1,000 lines (segments' hash methods, debounce strategy, registry, three invalidator methods, event interfaces, dead eager-load) can go once the two "make it real or delete it" decisions (version-hash, composite provider) are made.

4. **Authorization is centralized (good) but its contract is implicit (bad).** The handler-pipeline design avoids the duplicated-authorization-in-handlers antipattern — there is exactly one pipeline. But its semantics ("skip means pass") produce the P0 fail-open, ADR-2501's five layers don't match the four registered handlers, and the RBAC/SMART interaction vocabularies don't align (`read` vs CRUDS vs FHIR interaction codes — three permission languages in one folder). Worth a short ADR refresh defining: default-deny, the canonical interaction→permission mapping, and where capability enforcement actually lives (Medino behavior, not authz handler).

5. **Tenant handling has three type systems.** Route/config use `int`, Admin commands and package management use `string`, authorization compares `context.TenantId` (string from claims) with `fhirContext.TenantId.ToString()`. Every seam is a parse or ToString with its own failure mode (`LoadPackageHandler`'s post-hoc `int.Parse` being the worst). Standardize on `int` at the Application boundary.

6. **Conventions adherence is otherwise decent.** Medino Query/Handler separation is followed where CQRS applies; constructor injection is universal (no service locators found); primary constructors and file-scoped namespaces are used in newer files; `CancellationToken` is threaded through I/O correctly in nearly all paths (the exceptions are the deliberate fire-and-forget spots and the sync-over-async property getters flagged above).

## Recommendations Summary

| Priority | Recommendation | Effort | Files affected |
|----------|---------------|--------|-----------------|
| P0 | Add default-deny terminal to authorization pipeline | S | RbacAuthorizationHandler, FhirAuthorizationService (+1 new handler) |
| P0 | Make ConformanceState reads thread-safe (immutable snapshot swap) | M | ConformanceState, ActiveSearchParameter |
| P0 | Fix R6 in OperationsSegment; consolidate version-string switches | S | OperationsSegment |
| P0 | Fix OIDC snake_case deserialization + add test | S | OidcDiscoverySmartConfigurationProvider |
| P1 | Decide fate of composite-provider eager load; delete or complete invalidation chain | L | CompositeStructureDefinitionSummaryProvider, registry, debounce, 2 event handlers |
| P1 | Move package events to Domain; remove DataLayer→Application reference; fix load/unload invalidation asymmetry | M | Events/*, DataLayer csproj, 2 notification handlers |
| P1 | Delete or fix capability version-hash mechanism | M | CapabilityStatementService, ICapabilitySegment + 6 segments |
| P1 | Delete 3 unused invalidator methods; fix no-op ClearAsync logging | S | ICapabilityCacheInvalidator, CapabilityCacheInvalidator, MemoryCapabilityCache |
| P1 | Surface PackageResourceMapper parse failures as ValidationIssues | S | PackageResourceMapper, PackageActivationPipeline |
| P1 | int TenantId in Admin commands; report activation failure in LoadPackageResult | M | 6 Admin files |
| P1 | Make package list/unload actually tenant-scoped (or drop the parameter) | M | ImplementationGuideProvider, IPackageResourceRepository |
| P1 | Fix SidecarAuditLogger guarantee (channel or fail-fast); stop fabricating audit fields | M | SidecarAuditLogger |
| P1 | Remove HttpContext from FhirAuthorizationContext; move FhirVersionExtractor to API | M | FhirAuthorizationContext, 2 handlers, FhirVersionExtractor |
| P1 | Fix foreign namespaces in Application assembly | S | 6 files (Packages/, ResourceReferenceHelper) |
| P1 | UpdateReference must preserve sibling Reference fields | S | ResourceReferenceHelper |
| P1 | Wire or delete EnableV1ScopeCompatibility | S | SmartOptions, SmartScopeParser |
| P1 | Dedicated 403 exception + cached element in CapabilityEnforcementBehavior | M | CapabilityEnforcementBehavior, CapabilityCacheEntry |
| P1 | RBAC interaction-family mapping (read covers vread/history/search) | S | ResourceGrant |
| P1 | ProfileCapabilitySegment: typed cache, no tenant-"1" default | M | ProfileCapabilitySegment |
| P2 | One-type-per-file split | M | ~15 files |
| P2 | Remove log-and-rethrow wrappers; ArgumentException for args; rename `ct` | S | ~10 files |
| P2 | Header sweep (drop Microsoft Corporation headers) | S | pervasive |
| P2 | Advertise vread/history/patch; configurable canonical URL; fix vacuous ValidationBehavior condition | M | ResourceInteractionCapabilitySegment, CapabilityStatementService, ValidationBehavior |
| P2 | Tenant-aware includes; unique segment priorities; single IsBaseFhirPackage; Ignixa-native RBAC action strings; concrete-record events; ConditionalHeaderParser RFC gaps; comment fixes | S | ~12 files |
| P1 (addendum) | Make FHIRPath timeout actually cancel evaluation; configurable timeout | M | FhirPathEvaluatorWithTimeout, FhirPathEvaluator, ExperimentalAutofacRegistration |
| P1 (addendum) | Remove catch-all-return-null in ConceptMapResolverService sync Translate | S | ConceptMapResolverService |
| P1 (addendum) | Fix MapRegistryCache lifetime (tenant-keyed singleton) or delete invalidation handler + stats | M | MapRegistryCache, PackageLoadedMapCacheInvalidationHandler, ExperimentalAutofacRegistration |
