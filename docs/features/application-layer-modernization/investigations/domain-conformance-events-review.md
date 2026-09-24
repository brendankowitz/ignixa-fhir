# Investigation: Domain Layer & Conformance Events Architecture & Technical Review

**Feature**: application-layer-modernization
**Status**: Complete
**Created**: 2026-07-11
**Scope**: Ignixa.Domain, Ignixa.Conformance.Events

## Summary

`Ignixa.Domain` is not the dependency-free models-and-abstractions project ADR-2509 describes. It carries two Core project references plus an implementation package (`Microsoft.Extensions.Caching.Memory`), contains a dead caching subsystem with latent correctness bugs, ~350 lines of dead terminology models duplicated by DataLayer entities, and a copy-pasted exception hierarchy in which two exceptions return the wrong HTTP status code (a live, spec-visible defect). `Ignixa.Conformance.Events` is by contrast small, clean, and close to its ADR — its main problems are a published README that documents event types that do not exist, and an event-type registry that lives in DataLayer instead of the package that owns the contract.

## Strengths

- **Zero-copy read models are well designed.** `Models/SearchEntryResult.cs` and `Models/UpdateResult.cs` (raw `ReadOnlyMemory<byte>` + metadata) are a deliberate, documented design that avoids re-serialization on the hot path, and the whole `IFhirRepository` read surface is built around it consistently.
- **`Models/TransactionId.cs`** is a textbook `readonly record struct` value type — small, immutable, with `Parse`/`TryParse`.
- **`Models/HistoryQueryParameters.cs`** is a sealed record with self-validating `WithValidatedCount()`/`Validate()` methods returning sanitized copies — good "invalid state hard to keep" design.
- **`required`/`init` is used broadly** across models (`TenantConfiguration`, `ExportJobDefinition`, `HttpRequestAuditEvent`, `FhirOperationMetrics`), so construction completeness is enforced at compile time in most places.
- **`Caching/ConformanceResourceResolver.cs`** uses source-generated `[LoggerMessage]` throughout instead of string-interpolated logging (the subsystem is dead, but the logging pattern is right).
- **Ignixa.Conformance.Events events** (`Events/*.cs`) are small immutable positional records with no behavior — exactly what event payloads should be. `Abstractions/ISourceEventStore.cs` is a minimal 4-method contract, uses `IAsyncEnumerable` for replay, and every method takes a properly named `cancellationToken`.
- **No `Hl7.Fhir.*` dependency anywhere** in either project — the layer rule that matters most is respected.
- Nullable is enabled and warnings-as-errors is inherited from the root `Directory.Build.props` in both projects; no `#nullable disable` found.

## Findings

### P0 — ResourceNotFoundException returns HTTP 400 instead of 404
**Location**: `src/Application/Ignixa.Domain/Exceptions/ResourceNotFoundException.cs` (whole file); base default at `src/Core/Ignixa.Serialization/Abstractions/FhirException.cs:50`
**Issue**: `FhirException.StatusCode` defaults to 400 and `FhirExceptionMiddleware` (`src/Application/Ignixa.Api/Middleware/FhirExceptionMiddleware.cs:62`) writes it straight to the response. `ResourceNotFoundException` never overrides `StatusCode`, so every throw produces a 400 with a "not found" diagnostic. There is a live throw site: `src/Application/Ignixa.Application/Features/Experimental/Ips/Generator/IpsGeneratorService.cs:73` (`$summary` for a missing patient). This is a FHIR-spec-visible conformance defect (read of missing resource must be 404).
**Recommendation**: Add `public override int StatusCode => 404;`. Audit the other subclasses that silently inherit 400: `MethodNotAllowedException` (should be 405 — it also mislabels the issue as `IssueType.Forbidden`), and confirm 400 is intentional for `BadRequestException`, `EverythingOperationException`, `RequestNotValidException`, `RequestTooCostlyException`, `ResourceNotSupportedException`, `UnsupportedConfigurationException`.
**Effort**: S

### P1 — Dead Domain/Caching subsystem, registered in DI, containing latent cache-poisoning and no-op-invalidation bugs
**Location**: `src/Application/Ignixa.Domain/Caching/ConformanceResourceResolver.cs`, `Caching/InMemoryConformanceCache.cs`, `Caching/IFhirConformanceCache.cs`, `Abstractions/IConformanceResourceResolver.cs`; registration at `src/Application/Ignixa.Api/Registrations/ValidationServicesRegistration.cs:126-137`
**Issue**: `IConformanceResourceResolver` is registered as a singleton but **never injected anywhere** — the entire 4-file subsystem is dead code, superseded by the ADR-2512 event-sourced design. It is not harmless dead code:
1. **Version-key mismatch / cache poisoning**: `InMemoryConformanceCache.GetAsync` builds keys including the version (`InMemoryConformanceCache.cs:44,154-159`), but `SetAsync` and `InvalidateAsync` always build the key with `version: null` (`:71`, `:124`). So versioned lookups can never hit the cache, and `ConformanceResourceResolver.ResolveAsync` (`ConformanceResourceResolver.cs:84`) caches an *exact-version* resolution under the *unversioned* ("latest") key — a subsequent unversioned request would get a pinned old version.
2. **Silent no-op invalidation**: `InMemoryConformanceCache.InvalidateTenantAsync` (`:137-149`) does nothing, while `ConformanceResourceResolver.InvalidateTenantCacheAsync` logs "Invalidating conformance cache for tenant" at Information level (`:232`) — a caller would believe invalidation succeeded and serve stale conformance resources for up to 24h. CLAUDE.md rule: no silent failures.
3. The design also contradicts accepted ADR-2510 ("No TTL/expiration" as a caching design principle) — this cache uses 24h absolute + 1h sliding TTLs.
Because it is registered and discoverable, any future handler that injects `IConformanceResourceResolver` inherits these bugs silently — in a server whose conformance resources drive validation, that is dangerous.
**Recommendation**: Delete all four files and the `RegisterConformanceServices` block, and drop the now-unneeded `Microsoft.Extensions.Caching.Memory` PackageReference from `Ignixa.Domain.csproj`. If the resolver is ever needed again, ADR-2512's `ConformanceState` projection is the sanctioned mechanism.
**Effort**: S

### P1 — Six dead terminology model classes duplicated by DataLayer entities
**Location**: `src/Application/Ignixa.Domain/Terminology/TermCodeSystem.cs`, `TermConcept.cs`, `TermConceptMap.cs`, `TermConceptMapElement.cs`, `TermValueSet.cs`, `TermValueSetExpansion.cs`
**Issue**: None of these six classes is instantiated or referenced as a type anywhere in the solution — every match outside the Domain folder is a code comment. The real implementations are parallel EF entities in `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Entities/Terminology/*Entity.cs` (e.g., `TermCodeSystemEntity`). Even if they were live, they are relational row mirrors (surrogate PKs, FK ID columns, `GroupIndex`, JSON-blob columns) — persistence shapes, not domain models, and they hardcode R4 semantics (`TermConceptMapElement.Equivalence` — R5 renamed this to `relationship`) in a server that targets STU3–R6. ~350 lines of misleading dead code. Classic early-agent artifact: model written to a plan, implementation later diverged into DataLayer, original never deleted.
**Recommendation**: Delete all six classes. Keep `ITerminologyImporter.cs`, `TerminologyImportResult.cs`, `TerminologyImportStatus.cs`, which are live (used by `PackageResource` and the DataLayer importers).
**Effort**: S

### P1 — `object`-typed search indexes in the core write contract
**Location**: `src/Application/Ignixa.Domain/Models/ResourceWrapper.cs:34` (`IReadOnlyList<object>? SearchIndices`); `src/Application/Ignixa.Domain/Abstractions/IFhirRepository.cs:51` (`IReadOnlyList<object> searchIndexes` inside the `BatchWriteAsync` tuple)
**Issue**: The runtime type is always `SearchIndexEntry` (`src/Core/Ignixa.Search/Indexing/SearchIndexEntry.cs`; populated at `src/Application/Ignixa.Application/Features/Resource/CreateOrUpdateResourceHandler.cs:226,337`), and `Ignixa.Domain` **already references `Ignixa.Search`**, so there is no layering reason for `object`. Every data layer must downcast, and a wrong element type becomes a runtime failure instead of a compile error. This is the single worst type-safety hole in the domain contract.
**Recommendation**: Change both to `IReadOnlyList<SearchIndexEntry>`. While touching `BatchWriteAsync`, replace the 6-element tuple parameter with a small record (e.g., `BatchWriteOperation`) — a positional 6-tuple in a public repository contract is unreadable at call sites.
**Effort**: M

### P1 — ADR-2509 dependency claim is false; implementation code lives in Domain
**Location**: `src/Application/Ignixa.Domain/Ignixa.Domain.csproj:9-17`; `docs/adr/adr-2509-vertical-slice-architecture.md:56`
**Issue**: The ADR states `Ignixa.Domain → Models and abstractions (no dependencies)`. Today the project references `Ignixa.Serialization`, `Ignixa.Search` (project refs), `Microsoft.Extensions.Caching.Memory` (an *implementation* package), and `Microsoft.Extensions.Logging.Abstractions`, and contains concrete classes with I/O-adjacent behavior (`Caching/ConformanceResourceResolver.cs`, `Caching/InMemoryConformanceCache.cs`) and algorithmic helpers (`Abstractions/IdHelper.cs`). The Core project refs are defensible (they are `Ignixa.*` building blocks, and `ResourceWrapper`/`SearchEntryResult` legitimately need `ResourceJsonNode`) — but the ADR should say so instead of claiming "no dependencies". `Caching.Memory` is not defensible in a contracts project.
**Recommendation**: (a) Delete the Caching subsystem (see finding above), which removes `Caching.Memory`; (b) amend ADR-2509 (or add a superseding note) to state the actual allowed dependency set: `Ignixa.Domain → Ignixa.Serialization, Ignixa.Search only`; (c) treat any future `Microsoft.Extensions.*` implementation package in Domain as a review blocker.
**Effort**: S (after Caching deletion)

### P1 — Folder placement contradicts the layering: DataLayer depends on projects under `src/Application/`
**Location**: `src/Application/Ignixa.Domain/`, `src/Application/Ignixa.Conformance.Events/`; consumed by `src/DataLayer/Ignixa.DataLayer.{SqlEntityFramework,InMemoryIndex,FileSystem,BlobStorage}/*.csproj`
**Issue**: Both projects physically live inside `src/Application/`, yet all four DataLayer projects reference them. ADR-2509 draws Domain as its own layer below Application; CLAUDE.md's dependency diagram does the same. The folder layout tells a new reader "this is Application code," which is exactly wrong — these are the shared contracts of the whole system. Nothing in the ADRs or `src/Application/Directory.Build.props` documents this placement as deliberate (the props file only covers package-feed stability). Practical consequence: `src/Application/Directory.Build.props` policies (internal-feed stable packaging per ADR-2606/2607) silently apply to Domain and Conformance.Events, which may not be intended for contract packages.
**Recommendation**: Either move both projects to `src/Domain/` (or a `src/Contracts/` sibling of Core/DataLayer/Application) or document the placement explicitly in ADR-2509 with the reason. Moving is a solution-file and path edit — cheap now, more expensive every month.
**Effort**: M

### P1 — Pervasive `ct` parameter naming — explicit CLAUDE.md "critical violation"
**Location**: `Abstractions/IFhirRepository.cs` (all 12 methods), `Abstractions/ISearchService.cs`, `Abstractions/IQueryExecutionStrategy.cs`, `Abstractions/ISearchServiceFactory.cs`, `Abstractions/IFhirRepositoryFactory.cs`, `Abstractions/ITenantConfigurationStore.cs`
**Issue**: CLAUDE.md lists "Async methods without `CancellationToken` parameter → Name it `cancellationToken` (not `ct`)" as a critical violation. Six of the most-implemented interfaces in the codebase use `ct`, while the other half of the same folder (`IPackageResourceRepository`, `IBackgroundJobRepository`, `ISystemRepository`, `IConformanceResourceResolver`) uses `cancellationToken`. Because these are interfaces, every implementation inherits the wrong name, and named-argument call sites (`ct: token`) will break when this is eventually fixed — the cost grows with time.
**Recommendation**: Rename `ct` → `cancellationToken` across the six interfaces and their implementations in one mechanical PR (Roslyn rename, no behavior change).
**Effort**: M (wide but mechanical)

### P1 — Conformance.Events README documents event types that do not exist — and ships in the NuGet package
**Location**: `src/Application/Ignixa.Conformance.Events/README.md:44-56`
**Issue**: The README's event tables list `SearchParameterRegistered`, `SearchParameterStatusChanged`, `StructureDefinitionRegistered`, `StructureDefinitionStatusChanged`. None of these exist. The actual events are `SearchParameterActivated/ReindexStarted/ReindexCompleted/ReindexFailed/Deactivated/Deleted` and `StructureDefinitionActivated/Deactivated`. Root `Directory.Build.props:65-67` packs `README.md` into the package (`IsPackable=true` in the csproj), so this fabricated API surface is the published documentation of a shipped package.
**Recommendation**: Rewrite the two tables from the real types in `Events/SearchParameterEvents.cs` and `Events/StructureDefinitionEvents.cs`.
**Effort**: S

### P1 — Event-type ↔ CLR-type mapping owned by DataLayer; renames silently break replay; no versioning story
**Location**: contract: `src/Application/Ignixa.Conformance.Events/SourceEvent.cs` (`string EventType`, `object Data`); mapping: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/EventStore/SqlSourceEventStore.cs:138-156`
**Issue**: The persisted discriminator is `nameof(RecordType)`, and the string→Type switch lives in the SQL store. Consequences: (1) renaming any event record in Conformance.Events breaks deserialization of all historical events — the compiler cannot catch it because the coupling crosses the package boundary via a string; (2) adding an event requires editing DataLayer (shotgun surgery, and any second store implementation must duplicate the switch); (3) `SourceEvent.Data` as `object` plus a free-text `EventType` makes invalid states representable (`EventType` says X, `Data` is Y) — ADR-2512's own audit-trail goal depends on this envelope staying coherent. There is also no event-versioning/upcasting strategy anywhere, which every event-sourced system eventually needs. Minor related defect: `SqlSourceEventStore.AppendAsync:62-67` returns `SourceEvent`s without propagating `entity.TransactionId`, so appended events report `TransactionId = 0` to callers while replayed ones carry the real value.
**Recommendation**: Move the registry into Ignixa.Conformance.Events (e.g., a `ConformanceEventTypes` static map, or a marker interface `IConformanceEvent` with a `[EventType("...")]` attribute decoupling persisted names from CLR names). Have the store consume that registry. Document "event records are append-only; never rename" in the package README until versioning exists.
**Effort**: M

### P1 — `Ignixa.Domain.Exceptions.NotImplementedException` shadows `System.NotImplementedException`
**Location**: `src/Application/Ignixa.Domain/Exceptions/NotImplementedException.cs`
**Issue**: With `ImplicitUsings` enabled, any file that also imports `Ignixa.Domain.Exceptions` gets an ambiguous-reference error on `throw new NotImplementedException()` — or worse, code that imports only one namespace throws a different exception than the author believes (System's derives from `Exception` → 500; Domain's derives from `FhirException` → 501). Framework Design Guidelines explicitly prohibit reusing BCL type names.
**Recommendation**: Rename to `OperationNotImplementedException` (or `FhirNotImplementedException`).
**Effort**: S

### P1 — Copy-pasted exception hierarchy: duplicated issue construction, three inconsistent validation styles, issue-less default constructors
**Location**: all 13 files in `src/Application/Ignixa.Domain/Exceptions/`
**Issue**: Every subclass duplicates an identical `Issues.Add(new IssueComponent { Severity, Code, Diagnostics })` block in both its message constructors (~200 redundant lines across the folder). Message validation is inconsistent three ways: `Debug.Assert` (`EverythingOperationException.cs:23`, five others — vanishes in Release builds), `EnsureArg.IsNotNull` *after* the base call has already consumed the message (`RequestNotValidException.cs:23`), and nothing at all (`BadRequestException`). Every subclass also exposes a parameterless constructor producing an exception with no message and no issues — the middleware then serializes an empty OperationOutcome. And `ResourceNotSupportedException.cs:28` calls `string.Format(CultureInfo.CurrentCulture, $"{resourceType} not supported", resourceType)` — an interpolated string passed to `string.Format` with an unused argument (copy-paste damage from the microsoft/fhir-server original, which used a composite format string).
**Recommendation**: Add one protected `FhirException(string message, IssueSeverity severity, IssueType code)` constructor in the base (or a protected `AddIssue` helper) and collapse every subclass to 3–8 lines. Delete the parameterless constructors. Fix the `string.Format` misuse. Seal the leaf types.
**Effort**: M

### P1 — Stringly-typed job and tenant models: invalid states are representable everywhere
**Location**: `Models/BackgroundJob.cs:27,37` (`JobType` is `int` despite `Models/BackgroundJobType.cs` existing; `Status` is free-text `"Queued"/"Running"/...`); `Models/ImportJobDefinition.cs:35` (`Mode` string `"InitialLoad"/"IncrementalLoad"`); `Models/TenantConfiguration.cs:74` (`ValidationDepth` string `"Spec"`), `:90` (`TenantStorageConfiguration.Type` string), `:122` (`TenantSearchConfiguration.Type` string); `Abstractions/IBackgroundJobRepository.cs:50` (`int? jobType`)
**Issue**: A `BackgroundJobType` enum exists but the model and repository contract take raw `int`; job status, import mode, validation depth, and storage/search provider types are all magic strings. A typo (`"Runnning"`) compiles and fails at runtime in status filters. CLAUDE.md/standards: make invalid state unrepresentable. Also, `BackgroundJob<T>` XML docs call `Definition` "immutable" while every property on the class is `get; set;`, and `Models/ExportJobDefinition.cs:58` (`GroupId`) is the lone `set` among `init` siblings.
**Recommendation**: Use `BackgroundJobType` in `BackgroundJob<T>`/`IBackgroundJobRepository`; introduce `JobStatus`, `ImportMode`, `ValidationDepth`, `StorageType`, `SearchType` enums (serialize as strings for the JSON payloads). Change `GroupId` to `init`.
**Effort**: M

### P1 — Dead `BulkImportJob` model
**Location**: `src/Application/Ignixa.Domain/Models/BulkImportJob.cs`
**Issue**: Referenced by nothing outside its own file. It is the pre-`BackgroundJob<T>` merged model (its fields are the union of `ImportJobDefinition` + `ImportJobProgress` + `ImportJobResult`, plus a "Phase 6" comment block). Near-duplicate models are the most confusing kind of dead code because both look plausible.
**Recommendation**: Delete.
**Effort**: S

### P1 — `TenantContext` is vestigial: string-typed tenant ID, only ever used as `TenantContext.Default`
**Location**: `src/Application/Ignixa.Domain/TenantContext.cs`; consumers `src/Application/Ignixa.Application/Features/Search/SearchOptionsBuilderFactory.cs:26-27,41,48` and two MCP diagnostic tools
**Issue**: Every other tenant identifier in the system is `int` (`TenantConfiguration.TenantId`, `IFhirRepositoryFactory`, `IJobDefinition`); `TenantContext.TenantId` is `string?` and `Create` is never called with a real value — `SearchOptionsBuilderFactory` keys two `ConcurrentDictionary` caches on a component that is always `TenantContext.Default` (a constant), while carrying the *actual* tenant as a separate `int? tenantId` tuple member. The class also sits in the project root rather than `Models/`, and manually implements `Equals`/`GetHashCode` where a record would do.
**Recommendation**: Delete `TenantContext` and remove it from the cache keys (the `int? tenantId` member already does the job). If a richer context is ever needed, it should carry `int TenantId` to match the rest of the system.
**Effort**: S

### P1 — `IPackageResourceRepository` is an 18-method god interface with drifting conventions
**Location**: `src/Application/Ignixa.Domain/Abstractions/IPackageResourceRepository.cs`
**Issue**: One interface covers package CRUD/lifecycle, StructureDefinition queries, SearchParameter queries, OperationDefinition lookup, StructureMap lookup, custom-resource-type extraction, and activation loading. Conventions drift across it: `GetResourcesForActivationAsync:266` returns `PackageResource[]` while everything else returns `IReadOnlyList<>`; `ListLoadedPackagesAsync:94` returns raw tuples; `PackageVersionExistsAsync:175` takes `int tenantId = 0` — defaulting to the *reserved system partition* that the rest of the codebase forbids touching; `CancellationToken` is required on some methods and defaulted on others. Every new conformance feature has appended a method here (visible accretion pattern).
**Recommendation**: Split into `IPackageRepository` (lifecycle), `IConformanceQueryRepository` (SD/SP/OD/SM lookups), keeping the pair in Domain. Normalize returns to `IReadOnlyList<>`, replace the tuple with a `LoadedPackage(string Id, string Version)` record, remove the `tenantId = 0` default.
**Effort**: L

### P1 — Undeclared transitive dependency on `Ensure.That`
**Location**: `Abstractions/IdHelper.cs:7`, `Exceptions/RequestNotValidException.cs:6`, `Exceptions/RequestTooCostlyException.cs:6`, `Exceptions/ResourceNotSupportedException.cs:7`
**Issue**: `Ignixa.Domain.csproj` does not reference `Ensure.That`; it compiles only because `Ignixa.Serialization` happens to reference it (`src/Core/Ignixa.Serialization/Ignixa.Serialization.csproj:21`). If Serialization ever drops or privatizes that dependency, Domain breaks. The codebase otherwise standardizes on `ArgumentNullException.ThrowIfNull` / `ArgumentException` — `EnsureArg` here is copy-paste residue from microsoft/fhir-server.
**Recommendation**: Replace the four `EnsureArg` call sites with BCL guard clauses (`ArgumentException.ThrowIfNullOrWhiteSpace`, `ArgumentOutOfRangeException.ThrowIfGreaterThan`) and drop the `using EnsureThat;` lines. No new PackageReference needed.
**Effort**: S

### P2 — One-type-per-file violations (explicit CLAUDE.md rule)
**Location**:
- `Abstractions/IAuditLogger.cs` (+ `HttpRequestAuditEvent`)
- `Abstractions/IMetricsService.cs` (+ `FhirOperationMetrics`)
- `Abstractions/IExportStreamWriter.cs` (+ `IExportStreamWriterFactory`)
- `Abstractions/IPartitionStrategy.cs` (+ `PartitionResolutionContext`)
- `Models/RequestPartition.cs` (+ `PartitionMode` enum)
- `Models/TenantConfiguration.cs` (4 types: `TenantPackageConfiguration`, `TenantConfiguration`, `TenantStorageConfiguration`, `TenantSearchConfiguration`)
- `Ignixa.Conformance.Events/SourceEvent.cs` (+ `NewSourceEvent`)
- `Events/PackageEvents.cs` (3 records), `Events/SearchParameterEvents.cs` (7 records), `Events/StructureDefinitionEvents.cs` (2 records)
**Issue**: CLAUDE.md: "ONE TYPE PER FILE". The event files are the defensible end of the spectrum (tiny cohesive records), but the Domain cases hide real types — `HttpRequestAuditEvent` and `FhirOperationMetrics` are substantial models a reader will not find by filename.
**Recommendation**: Split the Domain files. For the event-record files, either split or get an explicit convention exemption recorded in CLAUDE.md; the current state violates the written rule.
**Effort**: S

### P2 — Copyright-header chaos: three styles, wrong attribution, malformed text
**Location**: pervasive; e.g. banner-style "Microsoft Corporation" (`Abstractions/IFhirRepository.cs`), XML `<copyright>` style (`Models/HistoryQueryParameters.cs:1-4`), "Ignixa Contributors" (`Models/SearchEntryMode.cs:2`), no header at all (`Models/TransactionId.cs`, `Caching/*.cs`, all Conformance.Events files), malformed "Corporation.All rights reserved" missing spaces (`Exceptions/BadRequestException.cs:2`, 9 more exception files)
**Issue**: `Directory.Build.props` declares `Authors: Ignixa Contributors`, yet most Domain files claim Microsoft copyright — some genuinely derived from microsoft/fhir-server (fine, MIT permits it, but then it should be acknowledged deliberately), most just copy-paste template propagation by agents. The malformed headers prove nobody is reading them.
**Recommendation**: Pick one header (or none — the repo suppresses IDE0073) and apply it repo-wide with a script; add acknowledgment of microsoft/fhir-server-derived files in NOTICE if any remain substantially copied.
**Effort**: S

### P2 — Almost nothing is sealed
**Location**: all `Exceptions/*.cs`, `Caching/InMemoryConformanceCache.cs`, `Caching/ConformanceResourceResolver.cs`, `Models/BackgroundJob.cs`, `Models/BulkImportJob.cs`, `Models/PackageResource.cs`, all `Terminology/Term*.cs`, `Models/ExportJob*.cs`, `Models/ImportJob*.cs`
**Issue**: House style is seal-by-default. Only `TenantContext`, `HistoryQueryParameters`, and the `sealed record`s in `IAuditLogger.cs`/`IMetricsService.cs` comply. None of these classes are designed for inheritance.
**Recommendation**: Seal them (records too, where not already).
**Effort**: S

### P2 — Roadmap/phase-number comments embedded in contracts
**Location**: `Abstractions/IPartitionStrategy.cs:49,69` ("Phase 20.2+"), `Abstractions/IQueryExecutionStrategy.cs:21,33` ("Phase 20 / 20.2+"), `Models/TenantMode.cs:19`, `Models/RequestPartition.cs:44`, `Models/PackageResource.cs:77` ("Phase 1"), `Models/BulkImportJob.cs:31` ("Phase 6"), `Caching/ConformanceResourceResolver.cs:11` ("Phase 2"), `Constants/SystemConstants.cs:21` ("ADR-2523 Phase 20" — no ADR-2523 exists in docs/adr)
**Issue**: Phase numbers reference a plan that lives nowhere in the repo and has already rotted (ADR-2523 doesn't exist). CLAUDE.md: comments explain *why*, not roadmap position.
**Recommendation**: Delete phase references; keep the behavioral content ("distributed mode may return multiple partitions — not yet implemented") where it documents a real contract nuance.
**Effort**: S

### P2 — `IdHelper`: implementation logic filed under Abstractions
**Location**: `src/Application/Ignixa.Domain/Abstractions/IdHelper.cs`
**Issue**: A static class with bit-shifting surrogate-ID math and a `DateTime.TruncateToMillisecond` extension is not an abstraction; it sits among 19 interfaces. It also carries the `EnsureArg` transitive-dependency problem (see P1) and trailing whitespace at line 38 (evidence the analyzer set isn't catching style in this file).
**Recommendation**: Move to `Ignixa.Domain/Helpers/` or alongside `TransactionId` in `Models/`; swap `EnsureArg.IsLte` for `ArgumentOutOfRangeException.ThrowIfGreaterThan`.
**Effort**: S

### P2 — Collection and tuple hygiene in contracts
**Location**: `Abstractions/IBlobStorageClient.cs:55` (`Task<List<string>> ListBlobsAsync` — mutable concrete list in a contract; standard says `IReadOnlyList<>`); `Abstractions/ISearchService.cs:55` (`IReadOnlyList<(long StartId, long EndId)>` tuples; also an export-only concern (`GetExportRangesAsync`) bolted onto the search abstraction); `Abstractions/IPackageResourceRepository.cs:94` (tuple list)
**Recommendation**: `IReadOnlyList<string>`; introduce `ExportRange(long StartId, long EndId)` record; consider moving `GetExportRangesAsync` to an export-specific interface when it next changes.
**Effort**: S

### P2 — Weakly-typed generic search options
**Location**: `Abstractions/ISearchService.cs:25-41`, `Abstractions/IQueryExecutionStrategy.cs:43-69` (`<TSearchOptions> where TSearchOptions : class`)
**Issue**: An unconstrained `class` generic is `object` with extra steps — implementations must runtime-check the concrete options type. The stated reason ("avoid circular dependencies with SearchOptions") no longer holds: Domain references `Ignixa.Search` directly. Either constrain to the real options type/marker interface from `Ignixa.Search`, or document why multiple unrelated option types genuinely flow through here.
**Recommendation**: Add a marker interface (e.g., `ISearchOptions`) in `Ignixa.Search` and constrain to it.
**Effort**: M

### P2 — `PackageResource` mixes persistence, import-tracking, and crypto into one mutable model
**Location**: `src/Application/Ignixa.Domain/Models/PackageResource.cs`
**Issue**: Database surrogate key (`PackageResourceId:20`), terminology-import state machine columns (`:84-114`), and an SHA-256 helper (`ComputeContentHash:132-137`) live on one all-`set` class shared across layers. Not wrong enough to block, but it is the shape of an EF entity, not a domain model, and each concern (identity, content, import state) changes for different reasons.
**Recommendation**: When terminology import is next touched, extract the import-tracking block into its own type and move `ComputeContentHash` to the importer that consumes it.
**Effort**: M

### P2 — Duplicate `Isolated/Distributed` enums
**Location**: `Models/TenantMode.cs` vs `PartitionMode` in `Models/RequestPartition.cs:30-46`
**Issue**: Two enums with identical members and near-identical doc text; one describes system-wide config, the other per-request mode, but nothing prevents them from drifting and every reader must figure out which to use.
**Recommendation**: Collapse to one enum (`TenantMode`) referenced by `RequestPartition`.
**Effort**: S

### P2 — `IConformanceResourceResolver` design defects (relevant only if the subsystem is revived instead of deleted)
**Location**: `Abstractions/IConformanceResourceResolver.cs`
**Issue**: (a) `tenantId` is `string` while the whole system uses `int`; (b) the interface mixes resource resolution with cache-invalidation operations, leaking the caching strategy into the contract; (c) `ConformanceResourceResolver.ResolveFromPackageAsync` validates `tenantId` then never uses it (dead parameter); (d) `InMemoryConformanceCache.SetManyAsync` throws on an empty dictionary (an empty batch is a no-op, not an error) and silently skips null/whitespace entries.
**Recommendation**: Moot if deleted per the P1; recorded here so a revival doesn't copy the interface as-is.
**Effort**: —

### P2 — Documentation drift in contracts
**Location**: `Terminology/ITerminologyImporter.cs:23-26` (three methods each take a leading `int tenantId` with no `<param>` doc — the docs were written for a signature without it); `Abstractions/IRequireCapability.cs:16` (example code implements `IRequiresCapability` — wrong name); `Abstractions/IFhirRepository.cs:60` ("renaming the lock file" — FileSystem-implementation detail stated as the contract for all providers)
**Recommendation**: Fix the three doc blocks.
**Effort**: S

### P2 — ADR-2512 status is still "Proposed" though fully implemented
**Location**: `docs/adr/adr-2512-event-sourced-conformance.md:3`
**Issue**: The event store, events, `ConformanceState` projection, sync services, and SQL migration (`20251223154537_AddSourceEventsTable`) all exist. The ADR index treats status as meaningful; a "Proposed" ADR that already shipped misleads reviewers about what is settled.
**Recommendation**: Flip to Accepted (with implementation date).
**Effort**: S

## Architectural Observations

1. **Ignixa.Domain has become a junk drawer.** Its legitimate core — `IFhirRepository`, `ResourceWrapper`, `ResourceKey`-adjacent models, `SearchEntryResult`, tenancy models, exceptions — is sound and matches ADR-2509's intent. Around it has accreted: a dead caching implementation, dead terminology entities, a dead import model, a vestigial `TenantContext`, and helper implementations. Roughly 15 of 74 files (~20%) are deletable today with zero behavioral change. That deletion is the single highest-leverage action for this subsystem.

2. **Dependency direction is correct; the map is wrong.** Verified: Domain references only Core (`Ignixa.Serialization`, `Ignixa.Search`) — no Application, no DataLayer, no `Hl7.Fhir.*`. Conformance.Events references only `Ignixa.Specification`. DataLayer and Application both depend on Domain, as designed. But ADR-2509 says "no dependencies" (false), diagrams `ResourceKey` in Domain (it lives in `src/Core/Ignixa.Abstractions/ResourceKey.cs`), and the physical folder layout puts both reviewed projects under `src/Application/` while DataLayer consumes them. Every map a newcomer would consult misdescribes the actual structure.

3. **Two conformance-resolution generations coexist, one dead.** ADR-2512's event-sourced pipeline (Conformance.Events + `ConformanceState` + `SqlSourceEventStore`) is the live system. The Domain/Caching resolver chain is the pre-ADR generation, still registered in DI, never consumed, contradicting both ADR-2510 (no-TTL principle) and ADR-2512 (which explicitly replaced multi-cache invalidation). Early-agent signature: superseded designs are wired up, not removed.

4. **The domain model is anemic by design — and that's mostly fine here.** ADR-2509 deliberately scopes Domain to "models and abstractions"; behavior lives in Application handlers. Within that constraint, the type-system usage is inconsistent rather than absent: `TransactionId` and `HistoryQueryParameters` show the team knows how to make invalid states unrepresentable, while job status strings, `int` job types, and `IReadOnlyList<object>` search indices show where speed won. The P1s above target exactly those gaps.

5. **Conformance.Events is the healthiest code reviewed** — small immutable records, one clean abstraction, correct `cancellationToken` naming, minimal dependencies, `TreatWarningsAsErrors` set locally as well as inherited. Its risks are all at the boundary: the string-keyed type registry in DataLayer, the `object Data` envelope, and a README describing an API that doesn't exist. Fix the boundary, and this package is done.

6. **Provenance is visible and unmanaged.** A large fraction of Domain (exceptions, `IdHelper`, several models) is adapted from microsoft/fhir-server, carrying its headers, its `EnsureArg` idiom, and in one case a broken `string.Format`. Adapted code is fine; unacknowledged, half-converted adapted code is where the malformed headers, dead parameters, and undeclared dependencies came from.

## Recommendations Summary

| Priority | Recommendation | Effort | Files affected |
|----------|---------------|--------|-----------------|
| P0 | Override `StatusCode` (404) on `ResourceNotFoundException`; fix `MethodNotAllowedException` (405 + issue type); audit remaining 400-inheritors | S | 2–8 exception files |
| P1 | Delete dead Domain/Caching subsystem + DI registration; drop `Caching.Memory` package ref | S | 5 files |
| P1 | Delete 6 dead `Term*` model classes | S | 6 files |
| P1 | Delete `BulkImportJob`; delete/replace `TenantContext` | S | 4 files |
| P1 | Type `SearchIndices`/`BatchWriteAsync` as `SearchIndexEntry`; replace 6-tuple with record | M | 2 Domain files + all repo implementations |
| P1 | Rename `ct` → `cancellationToken` across 6 interfaces + implementations | M | ~6 Domain files + implementations |
| P1 | Fix Conformance.Events README event tables (published package docs) | S | 1 file |
| P1 | Move event-type registry from `SqlSourceEventStore` into Conformance.Events; document rename prohibition; fix `TransactionId` drop in `AppendAsync` | M | 3 files |
| P1 | Rename `NotImplementedException` → `OperationNotImplementedException` | S | 1 file + call sites |
| P1 | Base-class issue constructor; collapse exception copy-paste; delete parameterless ctors; fix `string.Format` misuse | M | 14 files |
| P1 | Replace stringly-typed job/config fields with enums (`JobStatus`, `ImportMode`, `ValidationDepth`, storage/search types); use `BackgroundJobType` | M | ~8 files + handlers |
| P1 | Split `IPackageResourceRepository`; normalize return types; remove `tenantId = 0` default | L | 1 interface + implementations |
| P1 | Replace `EnsureArg` with BCL guards (removes undeclared transitive dependency) | S | 4 files |
| P1 | Amend ADR-2509 dependency claim; move (or document placement of) Domain + Conformance.Events out of `src/Application/` | M | ADR + solution/paths |
| P2 | One-type-per-file splits; seal classes; header cleanup; delete phase comments; relocate `IdHelper`; `List<string>` → `IReadOnlyList<string>`; constrain `TSearchOptions`; merge `TenantMode`/`PartitionMode`; fix doc drift; flip ADR-2512 to Accepted | S–M | broad |
