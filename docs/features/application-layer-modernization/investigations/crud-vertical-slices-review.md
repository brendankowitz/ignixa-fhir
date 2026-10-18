# Investigation: FHIR CRUD/RESTful Vertical Slices Review

**Feature**: application-layer-modernization
**Status**: Complete
**Created**: 2026-07-11
**Scope**: Features/{Resource,Bundle,Patch,ConditionalOperations,History,Search,Compartment,Export}

## Summary

The CRUD slices follow the ADR-2509 vertical-slice shape (Query/Command records + handlers) and the read path (Get/Search/History) is in reasonable shape. The write path is not: resource validation on create/update is structurally dead (DI generic mismatch), optimistic concurrency (`If-Match`) is parsed everywhere and enforced nowhere, PATCH silently drops search indices and rejects legal `identifier` patches, and the bundle pipeline neither rewrites intra-bundle `urn:uuid` references nor provides transaction atomicity — despite ADR-2509-bundle-processing advertising both. Several of these are silent data-integrity failures, not crashes, which is why they have survived: test coverage for exactly these paths (validation rejection, intra-bundle references, If-Match conflicts) is absent.

## Strengths

- **Read path design is sound**: `GetResourceHandler`, `SearchResourcesHandler`, and the History handlers consistently use `SearchEntryResult` raw-bytes zero-copy through to `FhirJsonWriter.WriteRawProperty` — resources are never deserialized on the read path (`Features/Resource/GetResourceHandler.cs`, `Features/Bundle/Serialization/StreamingBundleSerializer.cs:632`).
- **Count-as-render pagination** (`SearchResourcesHandler.cs:92-107` + `StreamingBundleSerializer.SerializeWithPaginationAsync`) is a good pattern: pageSize+1 fetch, serializer detects `hasMore` without a COUNT query, `_total` only triggers COUNT when explicitly requested.
- **Capability enforcement via `IRequireCapability`** on every query/command with a FHIRPath expression against the CapabilityStatement is clean and declarative (`GetResourceQuery.cs:25`, `DeleteResourceCommand.cs:23`).
- **Patch executors** use the strategy pattern per ADR-2510 correctly, and the before/after `ImmutablePropertyValidator` backstop is the right defense-in-depth idea (`Features/Patch/Executors/*`).
- **DeferredWriteCoordinator's** TCS-per-operation + bounded-channel batching design (`Features/Bundle/DeferredWriteCoordinator.cs`) is a solid pattern, including `RunContinuationsAsynchronously` and partition grouping.
- **`FilterUnsupportedParams`** in the serializer correctly strips unsupported params (incl. per-field `_sort`) from self links (`StreamingBundleSerializer.cs:842-900`).
- Bundle streaming serializer guarantees well-formed JSON on mid-stream failure by appending an error entry (`StreamingBundleSerializer.cs:404-439`).

## Findings

### P0 — Resource validation never runs: ValidationBehavior registered with wrong response type
**Location**: `src/Application/Ignixa.Application/Infrastructure/Behaviors/ValidationBehavior.cs:24`, `src/Application/Ignixa.Api/Registrations/ApplicationServicesRegistration.cs:129-131`
**Issue**: `ValidationBehavior` implements `IPipelineBehavior<CreateOrUpdateResourceCommand, ResourceKey>`, but the handler pipeline is `<CreateOrUpdateResourceCommand, UpdateResult>` (`CreateOrUpdateResourceHandler.cs:38`, registration line 140-142). The mediator resolves behaviors for `UpdateResult`, so this behavior is never in the pipeline. `CreateOrUpdateResourceHandler.cs:70` says "Validation now handled by ValidationBehavior in the pipeline" and performs no validation itself. Net effect: **PUT/POST resources are persisted without any schema validation** regardless of tenant `ValidationDepth` or `Prefer` header. No test in the repo references `ValidationBehavior`.
**Recommendation**: Change the behavior to `IPipelineBehavior<CreateOrUpdateResourceCommand, UpdateResult>` (and the `RequestHandlerDelegate<T>`), fix the registration, and add an E2E test that an invalid resource returns 400. Consider a Roslyn/RepoGuard check that behavior registrations match a registered handler signature.
**Effort**: S

### P0 — If-Match / optimistic concurrency is parsed but never enforced
**Location**: `src/Application/Ignixa.Application/Features/Resource/CreateOrUpdateResourceHandler.cs:63-204`, `Features/Patch/PatchResourceHandler.cs:44-181`, `Features/ConditionalOperations/ConditionalUpdate/ConditionalUpdateHandler.cs:276`, `Features/ConditionalOperations/ConditionalPatch/ConditionalPatchHandler.cs:125`
**Issue**: `CreateOrUpdateResourceCommand.IfMatch` and `PatchResourceCommand.IfMatch` are documented ("update only succeeds if resource version matches") and populated by the API layer (`FhirEndpoints.cs:477-504`) and by the conditional handlers ("prevents lost updates" comments) — but **no handler reads the property**. It is never compared against the stored version and never passed to the repository (`ResourceRequest` has an `IfMatch` slot that is also never filled). PUT with a stale `If-Match` succeeds; conditional update/patch have a TOCTOU lost-update window they explicitly claim to close; ADR-2510-patch's documented 412 path cannot occur. Aside: the API layer parses the If-Match header with `ConditionalHeaderParser.ParseIfNoneMatch` (`FhirEndpoints.cs:482`) — flagged to the API review.
**Recommendation**: Enforce in one place: fetch-or-pass-through the expected version to the repository write and translate mismatch into 412 (`ResourceVersionConflictException` already exists). Delete the parameter from the commands if the decision is to not support it — silent acceptance is the worst option.
**Effort**: M

### P0 — Intra-bundle urn:uuid reference rewriting is not implemented
**Location**: `src/Application/Ignixa.Application/Features/Bundle/ReferenceResolutionContext.cs:56` (`ResolveReference` — zero callers), `BundleReferencePreProcessor.cs:33-72`, `BundleEntryExecutor.cs:60-197`
**Issue**: The preprocessor assigns IDs for `urn:uuid` fullUrls and builds the map, and the target POST gets its pre-assigned ID via `BundleAssignedResourceId`. But nothing ever rewrites `urn:uuid:` references *inside resource bodies*: `BundleEntryExecutor` receives `referenceContext` and only null-checks it; the request body is the original `RawJson`. A transaction of `POST Patient (urn:uuid:A)` + `POST Observation {subject: urn:uuid:A}` persists the Observation with the literal `urn:uuid:A` string — a permanently broken reference, violating the FHIR transaction spec. ADR-2509-bundle-processing lists "Reference resolution for urn:uuid" as a delivered positive; it is not. No E2E test covers intra-bundle references.
**Recommendation**: After Phase-2 preprocessing, rewrite references in each entry's parsed resource (FHIRPath-driven or JSON scan for `"reference"` values) and regenerate `RawJson`, or resolve at serialization into the mini-request body. Add an E2E transaction test asserting the stored Observation references `Patient/{assignedId}`.
**Effort**: L

### P0 — Transaction bundles are not atomic; Phase-2 response plumbing is broken
**Location**: `src/Application/Ignixa.Application/Features/Bundle/BundleProcessor.cs:60-266`, `BundleChannelExecutor.cs:412-431`
**Issue**: Three compounding defects in `ProcessAsync`:
1. **Split commit**: a transaction whose entries are split across Phase 1 (plain) and Phase 2 (urn:uuid/conditional) gets **two coordinators with two transaction IDs and two commits** (lines 84-161 vs 191-229). Phase 1 is committed before Phase 2 runs; if Phase 2 fails, Phase 1 stays committed (`BundleProcessor.cs:359` even logs "Transaction rollback not implemented"). This is partial execution of a FHIR transaction. The application-level transaction model (CLAUDE.md) does not excuse this — the Application layer chooses to commit early.
2. **Phase-2 ordered-yield deadlock/drop**: `ExecuteTransactionStreamingAsync`/`ExecuteBatchStreamingAsync` yield responses in consecutive index order starting at `nextIndex = 0` (`BundleChannelExecutor.cs:413-428`). Phase-2 buffered entries have original indices starting at k>0 (Phase 1 consumed 0..k-1), so **no response is ever yielded** — everything accumulates in `completedResponses` and is discarded when the channel completes; the positional mapping `responses[bufferedEntries[i].Index] = phase2ResponseList[i]` (`BundleProcessor.cs:219-222`) then throws IndexOutOfRange. Additionally, `bufferedEntries` is reordered by verb (line 186) while the executor yields in index order, so even with the offset fixed, positional mapping misassigns responses to entries.
3. **Batch-write errors dropped before commit**: `StartBatchProcessor` collects errors from `ProcessBatchAsync` into `allErrors` and never surfaces them (`BundleProcessor.cs:494-500`); `CommitAsync` is then called unconditionally (line 160), committing the successful subset of a transaction whose other entries failed.
**Recommendation**: For transactions, drop the two-phase split: buffer the whole transaction (transactions must be atomic anyway), use one coordinator/transaction ID, refuse to commit if any entry failed, and map responses by `entry.Index` (dictionary), never by position. Keep two-phase only for batch.
**Effort**: L

### P0 — Streaming batch path rejects legal batch entries containing query strings
**Location**: `src/Application/Ignixa.Application/Features/Bundle/BundleChannelExecutor.cs:437-462`, `BundleProcessor.cs:586-659`, routed from `Ignixa.Api/Endpoints/FhirEndpoints.cs:1143-1153`
**Issue**: All batch bundles route to `ProcessBatchStreamingAsync`, which runs `ValidateStreamingEntry` on every entry and **throws for any `request.url` containing `?`**. `GET Patient?name=Smith`, `PUT Patient?identifier=...`, `DELETE Patient?identifier=...` are all legal, common batch entries; any one of them aborts the entire batch (the producer faults, and the serializer appends a single 500 error entry, dropping legitimate per-entry results). The endpoint comment "Batch bundle with no urn:uuid references" is aspirational — nothing routes batches with such entries to the buffered path.
**Recommendation**: In the batch streaming producer, treat entries that fail streaming validation as individually-buffered work items (or fall back to `ProcessAsync` for the whole batch), never as fatal. FHIR batch semantics require per-entry independence.
**Effort**: M

### P0 — PATCH: legal patches on `identifier` rejected by substring immutability check
**Location**: `src/Application/Ignixa.Application/Features/Patch/Validation/ImmutablePathChecker.cs:17-45`
**Issue**: Immutability is checked with `upperPath.Contains(".ID")`. `"PATIENT.IDENTIFIER"` contains `".ID"`, so `delete Patient.identifier[0]` / `replace Patient.identifier...` — among the most common PATCH operations in the wild — throw "Cannot delete immutable property". Any path segment starting with "id" (`identifier`, element `.id` at nested levels which are legitimately mutable) is blocked.
**Recommendation**: Tokenize the path and compare segments exactly: root-level `id`, `meta.versionId`, `meta.lastUpdated`. The post-patch `ImmutablePropertyValidator` already backstops the real invariants, so the pre-check can be strict-exact.
**Effort**: S

### P0 — PATCH persists without search indices and stamps every resource as FHIR 4.0
**Location**: `src/Application/Ignixa.Application/Features/Patch/PatchResourceHandler.cs:136-171`
**Issue**: The updated `ResourceWrapper` is built without `SearchIndices` (contrast `CreateOrUpdateResourceHandler.CreateResourceWrapper`, which treats index extraction as mandatory and fails the request if it errors). The data layer does not re-extract (`FileBasedFhirRepository.cs:163` stores `resource.SearchIndices?...` verbatim; SQL row generators consume the provided list). A patched resource's current version therefore has **no search index rows** — it disappears from or goes stale in search. Additionally `FhirVersion = "4.0", // Default to R4` is hard-coded on both wrappers (lines 147, 170) in a server that supports R4/R4B/R5/R6, silently rewriting the stored version of R5/R6 resources. The handler also bypasses `IPartitionStrategy` (uses `request.TenantId` raw) unlike every other write handler.
**Recommendation**: Extract search indices exactly as the create/update handler does (share the helper — see Architectural Observations), take FHIR version from `IFhirRequestContextAccessor`, and route partitioning through `IPartitionStrategy`.
**Effort**: M

### P0 — Streaming bundle parser fails on any JSON string token larger than ~8KB, and leaks PHI in the error
**Location**: `src/Application/Ignixa.Application/Features/Bundle/Serialization/StreamingBundleParser.cs:21, 253-291, 608-687`
**Issue**: `SharedStreamBuffer` uses a fixed 8KB buffer that can never grow. A single JSON token larger than the buffer (base64 `Binary.data`, `DocumentReference` attachments, large narratives — routine in FHIR) means `Utf8JsonReader` can never complete the token, `HasSpaceForMoreData()` goes false, and after 3 no-progress iterations the parser throws "Parser stuck in infinite loop" — **the whole bundle fails**. The exception message embeds the raw unconsumed buffer (`Content: '{remainingStr}'`, line 271-274) — patient data straight into exception messages and logs.
**Recommendation**: Grow the buffer (double up to a cap) when a token spans it — the standard `Utf8JsonReader` streaming pattern — and remove payload content from the exception text. Also: the rented `ArrayPool` buffer is never returned (`Dispose()` exists but is never called by `ParseStreamAsync`).
**Effort**: M

### P0 — Streamed batch bundles: transaction is never committed
**Location**: `src/Application/Ignixa.Application/Features/Bundle/StreamingBundleContext.cs:43-55`, `BundleProcessor.cs:586-659`
**Issue**: `ProcessBatchStreamingAsync` allocates a transaction ID via `DeferredWriteCoordinator.CreateAsync` and batch-writes under it, but `StreamingBundleContext.CompleteAsync` (the only cleanup the API layer calls, `FhirEndpoints.cs:1179`) only calls `CompleteWrites()` and awaits the batch processor — **`CommitAsync` is never invoked** on this path. Every non-streaming path commits (lock-file → committed rename / `dbo.Transactions` visibility per CLAUDE.md). Depending on the backend's treatment of uncommitted transactions, resources written via the streaming batch path are invisible or reaped as orphans.
**Recommendation**: Call `coordinator.CommitAsync` inside `CompleteAsync` (it holds the coordinator already). Add an E2E test that a resource POSTed via a batch bundle is readable after server restart / against the SQL backend.
**Effort**: S

### P1 — Bundle response bodies carry wrong version metadata on the deferred path
**Location**: `src/Application/Ignixa.Application/Features/Resource/CreateOrUpdateResourceHandler.cs:119-131, 259-260`
**Issue**: On the coordinator path, `UpdateResult.ResourceBytes` is serialized from the pre-write `JsonNode`, whose `meta.versionId` was force-set to `"1"` and `meta.lastUpdated` to queue-time `UtcNow` (line 259-260). For an update that actually produced version 5, the bundle entry response body says `versionId: "1"` with a fabricated timestamp. The `ResourceKey` from the coordinator has the real version; the body does not.
**Recommendation**: Patch the JSON's meta from the returned `ResourceKey`/write result before serializing, or return only key+location on the deferred path (Prefer: return=minimal semantics).
**Effort**: S

### P1 — Debug leftovers logged at Warning per bundle entry
**Location**: `src/Application/Ignixa.Application/Features/Resource/CreateOrUpdateResourceHandler.cs:112-116` ("HANDLER: Retrieved entry index..."), `Features/Bundle/DeferredWriteCoordinator.cs:151-155` ("QUEUE: Entry ...")
**Issue**: Two `LogWarning` calls with ALL-CAPS debug prefixes fire for every write in every bundle — pure development scaffolding polluting production logs and alerting.
**Recommendation**: Delete or demote to `LogTrace`.
**Effort**: S

### P1 — Exception classification by message substring
**Location**: `src/Application/Ignixa.Application/Features/Bundle/BundleEntryExecutor.cs:261-265`
**Issue**: `ex.Message.Contains("conflict")` / `"recently updated"` / `"constraint violation"` decides between 409 and 500. Any unrelated exception whose message contains "conflict" becomes a 409; a reworded exception message silently changes HTTP semantics.
**Recommendation**: Match on exception types (`ResourceVersionConflictException` is already handled above — extend the typed catch set) and let everything else be 500.
**Effort**: S

### P1 — RecyclableMemoryStream instances never disposed in bundle entry execution
**Location**: `src/Application/Ignixa.Application/Features/Bundle/BundleEntryExecutor.cs:124, 345-355`
**Issue**: `responseBodyStream` and the request-body stream from `SerializeResourceToStream` are rented from `RecyclableMemoryStreamManager` and never disposed, defeating the pooling the manager exists for (and its debug-mode leak detection will scream).
**Recommendation**: `await using` both streams for the lifetime of the entry execution.
**Effort**: S

### P1 — Hand-rolled JSON re-assembly in the parser produces invalid JSON for edge inputs
**Location**: `src/Application/Ignixa.Application/Features/Bundle/Serialization/StreamingBundleParser.cs:363-367, 563-602`
**Issue**: Entry resources are rebuilt token-by-token into a `StringBuilder`. `EscapeJsonString` handles only `\ " \n \r \t` — control characters U+0000–U+001F (legal in JSON strings via `\uXXXX`, e.g. `\b`, `\f`) are re-emitted raw, producing invalid `RawJson` that is then parsed/persisted. Property names are re-quoted **without any escaping** (line 365), so a crafted property name containing `"` breaks or injects into the reconstructed JSON. This is attacker-reachable input on the bundle endpoint.
**Recommendation**: Replace the whole string-rebuild with `JsonDocument.ParseValue(ref reader)` + `WriteRawValue` per entry resource (correct by construction), or at minimum escape via `JsonEncodedText`. This also deletes ~200 lines of comma/flag state machinery in `BundleParserState`.
**Effort**: M

### P1 — `_elements` filtering: nested paths silently drop elements; decimals lose precision; numbers can vanish
**Location**: `src/Application/Ignixa.Application/Features/Bundle/Serialization/ResourceElementsSerializer.cs:34-59, 183-201, 254-263`
**Issue**: (1) The doc claims "dot notation supported for nested", but filtering is root-level only: `_elements=name.family` puts `"name.family"` in the allowlist, the root property `name` doesn't match, and the entire `name` element is dropped — silently wrong results for a documented feature. (2) Numbers are round-tripped via `TryGetInt64`/`TryGetDouble`: FHIR decimals lose precision/trailing zeros (`1.50` → `1.5`), which the FHIR spec explicitly forbids. (3) In the array branch (line 254-263) there is no fallback: a number fitting neither long nor double is **silently omitted** from output. (4) No `SUBSETTED` meta tag is added (spec SHOULD). Also, `SkipValue`/`SkipObject`/`SkipArray` reimplement `Utf8JsonReader.Skip()`.
**Recommendation**: Copy numbers via `reader.ValueSpan` raw always; either implement nested paths (include root when a dotted path is requested) or reject dotted `_elements` with 400; add `SUBSETTED`; use `reader.Skip()`.
**Effort**: M

### P1 — Serializer flush threshold is 50 MB; empty `entry` arrays violate FHIR JSON rules
**Location**: `src/Application/Ignixa.Application/Features/Bundle/Serialization/StreamingBundleSerializer.cs:97, 169, 219, 304, 399`
**Issue**: (1) `FlushThresholdBytes = 50 * 1024 * 1024` — the "streaming" search serializer buffers up to 50 MB in the `Utf8JsonWriter` before first flush; most responses are fully buffered, killing TTFB and memory-bounding claims (comment says "prevents unbounded memory growth" — 50 MB per concurrent request is the growth). Meanwhile `SerializeAsync`/`SerializeHistoryAsync` flush per entry (a syscall each). (2) Every method writes `WriteStartArray("entry")` unconditionally: a zero-match search emits `"entry": []`, which the FHIR JSON representation forbids (arrays must not be empty) — validators flag every empty search result.
**Recommendation**: Threshold ~64 KB; defer `entry` array start until the first entry.
**Effort**: S

### P1 — Transaction silently downgraded to batch when `type` follows `entry` in the JSON
**Location**: `src/Application/Ignixa.Application/Features/Bundle/Serialization/StreamingBundleParser.cs:80-131`, `Ignixa.Api/Endpoints/FhirEndpoints.cs:1096-1103`
**Issue**: Header parsing stops at the `entry` array. JSON property order is not guaranteed; if a client emits `entry` before `type` (legal), `BundleType` is null and the endpoint defaults to **Batch** — a transaction executes with batch semantics (no atomicity, no verb ordering) with no error to the client.
**Recommendation**: If `type` was not seen before `entry`, reject with 400 (cheap) or buffer until `type` is found. Silently guessing transaction-vs-batch is not acceptable.
**Effort**: S

### P1 — Conditional create in bundles ignores the pre-assigned resource ID
**Location**: `src/Application/Ignixa.Application/Features/ConditionalOperations/ConditionalCreate/ConditionalCreateHandler.cs:215`, vs `Features/Bundle/BundleEntryExecutor.cs:100`
**Issue**: `BundleEntryExecutor` sets the typed property `entryContext.BundleAssignedResourceId`; `ConditionalCreateHandler` looks for `context.Properties["BundleAssignedResourceId"]` (dictionary), which is never populated. A bundle conditional-create with a `urn:uuid` fullUrl gets a fresh GUID instead of the pre-assigned one, so the reference map entry points at an ID that doesn't exist. (Standalone `FhirEndpoints.cs:916` reads the typed property correctly.)
**Recommendation**: Read `context.BundleAssignedResourceId` (the typed property). Delete the Properties-dictionary convention.
**Effort**: S

### P1 — Conditional create/update race: search-then-write with no uniqueness guard
**Location**: `src/Application/Ignixa.Application/Features/ConditionalOperations/ConditionalCreate/ConditionalCreateHandler.cs:99-134, 150-171`, `ConditionalUpdate/ConditionalUpdateHandler.cs:84-119`
**Issue**: Match counting and the subsequent write are not atomic. Two concurrent `If-None-Exist` creates both observe 0 matches and both create — duplicates, the exact thing conditional create exists to prevent. The "deleted between search and get → create new" branch (line 150-171) widens the window. Since If-Match is also unenforced (see P0), conditional update has the same lost-update exposure.
**Recommendation**: At minimum document the level of guarantee in the CapabilityStatement; properly, push the match-check into the repository write (compare-and-swap style) or serialize conditional ops per (tenant, type, criteria-hash).
**Effort**: L

### P1 — `_total=accurate` for history enumerates the entire history stream
**Location**: `src/Application/Ignixa.Application/Features/History/HistoryCountHelper.cs:23-96`
**Issue**: Counting is done by `await foreach` over the full result set with `Count = int.MaxValue` — for system-level history that is every version of every resource in the tenant, per request. This is a COUNT query done in application memory.
**Recommendation**: Add `Count*HistoryAsync` methods to `IFhirRepository` and push the count to the store. Also: all three methods name the token `ct`, violating the repo's explicit `cancellationToken` rule.
**Effort**: M

### P1 — $includes pagination re-executes the search and skip-scans per page
**Location**: `src/Application/Ignixa.Application/Features/Resource/IncludesResourceHandler.cs:62-112`
**Issue**: Each `$includes` page re-runs the original search with `MaxItemCount = min(pageSize*10, 10000)` and client-side skips `offset` include entries (`FilterIncludesWithPaginationAsync`). Page N costs O(N·pageSize) repository work — O(n²) across a full pagination walk — and any includes beyond the 10k fetch cap are silently unreachable. The decoded token's pageSize is discarded (`out _`, line 67) even though `Encode` stores it.
**Recommendation**: Push include-offset pagination into the execution strategy, or persist the match-set keys in the continuation token instead of re-searching.
**Effort**: M

### P1 — SearchCompartmentHandler mutates the request and skips the pagination contract
**Location**: `src/Application/Ignixa.Application/Features/Compartment/SearchCompartmentHandler.cs:83-146`
**Issue**: (1) The handler mutates `request.SearchOptions.Expression` and `.ResourceType` in place — command records are supposed to be immutable, and a pipeline retry would AND the compartment expression twice. (2) It does not apply the pageSize+1 count-as-render pattern used by `SearchResourcesHandler`, so `hasMore`/next-link detection is inconsistent for compartment searches; `Total` and `ContinuationToken` are `TODO` (lines 131-144) in a production path. (3) `SearchCompartmentQuery.GetCapabilityRequirementExpression()` with `ResourceType == "*"` emits `type = '*'`, which no CapabilityStatement advertises — wildcard compartment search (`GET /Patient/123/*`) likely fails capability enforcement (mirror the `null → "true"` handling of `SearchResourcesQuery`).
**Recommendation**: Build a new `SearchOptions` (see the clone problem below), reuse the +1 pattern, fix the wildcard capability expression.
**Effort**: M

### P1 — SearchOptions hand-copying: three divergent copies of a fragile clone
**Location**: `src/Application/Ignixa.Application/Features/Resource/SearchResourcesHandler.cs:94-107`, `Features/Resource/IncludesResourceHandler.cs:73-86`, (contrast: `SearchCompartmentHandler` mutates instead)
**Issue**: Because `SearchOptions` is a mutable class, handlers clone it by hand-listing every property. The two existing copies already diverge (Includes handler zeroes `Total` and `ContinuationToken`), and neither copies `Elements`, `IncludesMaxItemCount`, `IncludesContinuationToken`, or `BundleIssues` — properties the serializer reads. Any new `SearchOptions` property is silently dropped at these seams.
**Recommendation**: Give `SearchOptions` a `Clone()`/`With(maxItemCount:)` method (or make it a record with `with`), and use it in all three handlers.
**Effort**: S

### P1 — Sync-over-async and poll-waiting in search parameter initialization
**Location**: `src/Application/Ignixa.Application/Features/Search/FhirVersionContext.cs:338`, `Features/Search/CompositeSearchParameterDefinitionManager.cs:116-135`
**Issue**: `Task.Run(async () => ...).GetAwaiter().GetResult()` inside a `ConcurrentDictionary.GetOrAdd` factory blocks a request thread; the awaited `InitializeAsync` itself poll-waits for `ConformanceState.IsInitialized` in a 100ms × 50 `Task.Delay` loop — so first-touch of a tenant's search parameters can block a thread for 5 seconds. `GetOrAdd` also offers no single-execution guarantee, so multiple threads can run this concurrently (and `GetSchemaProvider`'s factory registers with the provider registry — a side effect that can fire twice).
**Recommendation**: Expose an `Initialization` Task on `ConformanceState` to await; make the manager acquisition path async (or use `Lazy<Task<T>>` per key).
**Effort**: M

### P1 — Dead code: two unused 90-line processing paths, duplicated batch processor, dead PaginationResult
**Location**: `src/Application/Ignixa.Application/Features/Bundle/BundleProcessor.cs:282-458` (`ProcessBufferedAsync`, `ProcessStreamingAsync` — private, zero callers), `BundleProcessor.cs:610-642` (verbatim copy of `StartBatchProcessor`), `Serialization/StreamingBundleSerializer.cs:920` (`PaginationResult` — zero usages)
**Issue**: ~220 lines of dead or copy-pasted orchestration logic that near-duplicates the live path. Anyone reading or modifying bundle processing must reverse-engineer which of four paths actually executes (answer: `ProcessAsync` for transactions, `ProcessBatchStreamingAsync` for batches).
**Recommendation**: Delete the two private methods and `PaginationResult`; have `ProcessBatchStreamingAsync` call `StartBatchProcessor`.
**Effort**: S

### P1 — X-Provenance handling duplicates the write path with weaker guarantees
**Location**: `src/Application/Ignixa.Application/Features/Resource/CreateOrUpdateResourceHandler.cs:295-427`
**Issue**: `ProcessProvenanceAsync` re-implements wrapper construction + index extraction (a near-copy of `CreateResourceWrapper`) and its own validation (`ValidateProvenance`) instead of dispatching a `CreateOrUpdateResourceCommand`. If no Provenance schema is found it logs a warning and **skips validation entirely** (line 401-405). The Provenance write is also a second, non-atomic write: main resource committed, Provenance write fails → 500 returned for a request whose primary effect succeeded.
**Recommendation**: Dispatch through the mediator (gets pipeline validation for free once the P0 is fixed), or extract a shared wrapper-builder; decide and document the failure semantics for the Provenance side-write.
**Effort**: M

### P1 — FHIR Patch wire format deviates from the spec for add/move
**Location**: `src/Application/Ignixa.Application/Features/Patch/FhirPatchParametersParser.cs:91-108`, `Executors/MoveOperationExecutor.cs:23-68`
**Issue**: Spec-conformant FHIRPath Patch `add` uses `path` (parent) + `name` + `value`; this parser has no `name` part at all — conformant clients (HAPI, firely) sending `name` will have it ignored and the operation misapplied or rejected. Spec `move` is `path` + `source` (integer index) + `destination` (integer index) within one collection; this implementation treats source/destination as string FHIRPaths and implements move as delete+append — destination *position* is not honored, so reordering (the entire point of move) doesn't work.
**Recommendation**: Implement the spec shapes (`name` for add; integer indices for move) — this is a conformance-suite-visible deviation. Also note the same required-part validation is written three times (parser, `FhirPatchValidator`, each executor); keep one.
**Effort**: M

### P2 — ONE TYPE PER FILE violations
**Location**: `Features/Bundle/BundleProcessingOptions.cs` (record + `BundleType` enum), `Features/Patch/FhirPatchOperation.cs` (record + enum), `Features/Patch/FhirPatchParametersParser.cs` (+`FhirPatchException`), `Features/Resource/IncludesResourceHandler.cs` (+`IncludesContinuationToken`), `Features/Bundle/Serialization/StreamingBundleSerializer.cs` (+`PaginationResult`)
**Issue**: Explicit repo rule violated in five files. Additionally two distinct classes are both named `StreamingBundleContext` (`Features/Bundle/` vs `Features/Bundle/Serialization/`) — one is the parse-side context, the other the response-side; same name, different meaning, regular source of confusion.
**Recommendation**: Split files; rename one context (e.g. `ParsedBundleStream` / `StreamingBundleResponseContext`).
**Effort**: S

### P2 — CancellationToken naming and constructor-parameter style violations
**Location**: `Features/History/HistoryCountHelper.cs:27,54,80` (`ct`), `Features/Bundle/Serialization/StreamingBundleParser.cs:40,217,637` (`ct`), `Features/History/GetResourceHistoryHandler.cs:25` (constructor parameter named `_logger`)
**Issue**: CLAUDE.md marks `ct` as a critical violation; `ILogger<...> _logger` as a parameter name with `this._logger = _logger` is plainly generated-and-unreviewed.
**Recommendation**: Rename.
**Effort**: S

### P2 — Nonsense boolean in ValidationBehavior
**Location**: `src/Application/Ignixa.Application/Infrastructure/Behaviors/ValidationBehavior.cs:79`
**Issue**: `if (depth != Minimal || depth == Spec || depth == Full)` — the ORs are unreachable; it reduces to `depth != Minimal`. Harmless but a marker that the file was never reviewed (consistent with it never executing).
**Recommendation**: `if (validationDepth != ValidationDepth.Minimal)`.
**Effort**: S

### P2 — Per-get dictionary allocation in package features
**Location**: `Features/Resource/IncludesOperationFeature.cs:24-28`, `Features/Export/BulkDataExportFeature.cs:24-29`
**Issue**: `ResourceOperations` allocates a fresh `Dictionary` on every property read.
**Recommendation**: `static readonly FrozenDictionary`.
**Effort**: S

### P2 — Swallowed exceptions and poll patterns in Search infrastructure
**Location**: `Features/Search/CompositeSearchParameterDefinitionManager.cs:311-323` (`catch { return false; }` over `TryGetSearchParameters`), `CompositeSearchParameterDefinitionManager.cs:149-154` (dead local `baseByResourceTypeAndCode` — built with a custom comparer and never used)
**Issue**: The bare catch violates the no-silent-failures rule (a schema-provider bug would surface as "resource type has no search parameters"); the dead lookup is leftover from a removed merge strategy.
**Recommendation**: Catch nothing (Try-pattern should only convert *not-found*, not all failures); delete the dead local and `ResourceTypeCodeComparer` if then unused.
**Effort**: S

### P2 — Style drift markers across the subsystem
**Location**: throughout
**Issue**: Three copyright-header styles (banner, XML `<copyright>`, none — Patch and ConditionalDelete/Read files have none); explicit `using System;` blocks only in Patch/ConditionalRead files; `Nullable<int>` instead of `int?` (`FhirVersionContext.cs:80,178,279`); primary constructors used in exactly two handlers (`IncludesResourceHandler`, patch executors) while a dozen others use full ctor + `ArgumentNullException` boilerplate; comment density far beyond the "no inline comments" rule (most handlers narrate every step). Each inconsistency maps to a different generation of agent authorship.
**Recommendation**: One `.editorconfig`-enforced header, primary constructors as the standard, a comment-stripping pass when files are next touched. Not worth a dedicated PR per file.
**Effort**: M (amortized)

### P1 — Bundle entries bypass the HTTP middleware pipeline, including authorization, and run with an anonymous principal (verified)
**Location**: `src/Application/Ignixa.Api/Infrastructure/AspNetCorePipelineExecutor.cs:47-114`, `Features/Bundle/BundleEntryExecutor.cs:123-134`
**Issue**: Verified mechanism: `AspNetCorePipelineExecutor.ExecuteAsync` does route matching itself and then invokes the matched endpoint's `RequestDelegate` directly (line 108). It never runs the middleware pipeline — so `AuthorizationMiddleware` (which is what enforces `RequireAuthorization`/endpoint authorization metadata for Minimal APIs) does not execute for bundle entries. Compounding this, `BundleEntryExecutor` builds a bare `DefaultHttpContext` and never copies `parentHttpContext.User`, so `context.User` is an anonymous principal inside every entry. Net: any per-endpoint authorization enforced via endpoint metadata + middleware is silently skipped for operations executed through a bundle; only Medino pipeline behaviors (capability enforcement, and whatever the Authorization slice registers as behaviors) apply. Whether an Application-layer authorization behavior fully covers the gap is owned by the Authorization slice review — but at minimum `httpContext.User = parentHttpContext.User` must be copied, and the security model for bundle-entry execution needs to be stated explicitly rather than emergent.
**Recommendation**: Copy `User` (and relevant auth-result features) onto the mini context; document that middleware does not run for bundle entries and enumerate which protections must therefore live in Medino behaviors; add a test that a bundle entry against a protected endpoint is rejected for an unauthorized caller.
**Effort**: M

### P2 — Created-vs-updated is inferred by string-comparing versionId at the API boundary
**Location**: `src/Application/Ignixa.Api/Endpoints/FhirEndpoints.cs:516`, `src/Application/Ignixa.Domain` (`UpdateResult`)
**Issue**: The API decides 201-vs-200 via `result.Key.VersionId == "1"`. Functionally correct today (creates are always v1), but it's a stringly-typed contract: the repository knows whether it created or updated and should say so. `ConditionalCreateResult.WasCreated` already sets the precedent. Any future versioning change (imports, version-preserving restores) silently breaks HTTP semantics. (Raised by the API/HTTP review; the fix belongs on the Application/Domain result type.)
**Recommendation**: Add `WasCreated` (or an enum) to `UpdateResult`, populated by the repository write; drop the versionId comparison.
**Effort**: S

### P2 — Misc correctness nits
- `DeferredWriteCoordinator.cs:339` uses `TrySetException` but line 355 uses `SetException` — the latter throws if the TCS was already completed (possible when a batch is split across partition groups after a prior failure). Use `TrySetException` consistently. Also `QueueWriteAsync`'s `await tcs.Task` ignores the cancellation token — a cancelled request hangs until the batch processor completes the TCS.
- `BundleParserState.CompleteEntry` calls `_resourceJsonBuilder.ToString()` twice (lines 163, 184) — two full string materializations per entry; it also eagerly `ResourceJsonNode.Parse`s every entry resource even though `BundleEntryExecutor` only uses `RawJson`.
- `BundleParserState.ExtractIdFromUrl` returns null for `Patient/123/_history/2` style URLs (versioned bundle requests lose their ID).
- `ConditionalDeleteHandler.cs:138-143` deletes matches one-by-one through the mediator (N partition resolutions + N repository round trips) and ignores the per-delete `bool` result — a concurrent delete is still counted in `DeletedIds`.
- `ConditionalReadHandler.cs:41-50` doesn't handle `If-None-Match: *` and compares raw versionId (correct only if API layer strips `W/"..."` — it does today, but the contract is implicit).
- `GetResourceHandler`/`SearchResourcesHandler` pass empty `queryParams` to `DetermineReadPartition` with a `// TODO: Extract from SearchOptions` (`SearchResourcesHandler.cs:64`) — if partition selection ever depends on query params, this is a silent wrong-partition bug lying in wait.
- `FhirJsonWriter.WriteString` throws on empty values, and `WriteBundleLinks` passes `link.Url ?? string.Empty` (`StreamingBundleSerializer.cs:545`) — a link with an empty URL crashes serialization mid-stream.
- `SerializeWithPaginationAsync` sets the next-link continuation parameter as `after` (`StreamingBundleSerializer.cs:234`) — verify this matches the parameter the search endpoint actually parses; the token itself is offset-based, so pages skew under concurrent writes.

## Architectural Observations

1. **The write path has no shared spine.** `CreateOrUpdateResourceHandler`, `PatchResourceHandler`, `ProcessProvenanceAsync`, and the two conditional handlers each assemble `ResourceWrapper`s with their own rules for meta, FHIR version, search indices, and partitioning — and each gets a different subset right. PATCH loses indices and version; Provenance skips pipeline validation; conditional handlers convert `UpdateResult → SearchEntryResult → ResourceWrapper` through two copy-paste `ConvertSearchEntryToWrapper` helpers (`ConditionalCreateHandler.cs:263`, `ConditionalUpdateHandler.cs:302`). A single `ResourceWrapperFactory` (version from context, mandatory index extraction, partition via strategy) would eliminate the whole defect class. This is consistent with vertical-slice architecture — slices may share infrastructure.

2. **Concurrency control is documentation-only.** `IfMatch` flows API → command → nowhere, in three separate features, each with comments asserting it works. This is the signature early-agent failure mode: the seam was built on both sides but the middle was never wired, and no test exercises it.

3. **Bundle processing has four orchestration paths, two alive.** The live transaction path violates its own ADR (atomicity, reference resolution both advertised, both absent), and the live batch path can't process legal batch entries with query strings. The channel/verb-group machinery in `BundleChannelExecutor` (channel-recreation per verb group, consumer restarts) is sophisticated code solving the wrong problem first — correctness of the simple cases (references, atomicity, response mapping) was never established. Recommend: make transactions boring (fully buffered, one coordinator, one commit, dictionary-indexed responses) and keep streaming cleverness for batches only.

4. **The Serialization subfolder is a hand-rolled JSON stack** (parser state machine, string re-assembly, custom escaper, custom Skip). Roughly 2,600 lines where `JsonDocument.ParseValue`/`WriteRawValue`/`reader.Skip()` would cover most of it. The bugs found (8KB token limit, control-char escaping, decimal precision, dropped numbers) are all consequences of reimplementing primitives the BCL provides. This directly contradicts the CLAUDE.md preference for robust, declarative data access.

5. **Layering per ADR-2509 is mostly respected** — no `Hl7.Fhir.*` packages in Application (verified in `Ignixa.Application.csproj`), Minimal API only, dependency direction clean. Two soft violations: `HistoryQueryParametersParser` and `BundleEntryExecutor` take direct dependencies on `Microsoft.AspNetCore.Http` types inside Application (the latter is intrinsic to the pipeline-routing design; the former is avoidable). The FHIRPath-over-JSON-navigation rule is moot in most of this subsystem because the read path is deliberately zero-copy bytes, but the Patch mutators appropriately use the FHIRPath mutator.

6. **Tenancy plumbing is inconsistent across verbs**: Get/Search/Delete/CreateOrUpdate resolve partitions via `IPartitionStrategy`; Patch and all three History handlers use raw `TenantId`; `DeleteResourceHandler` builds its `ResourceKey` with `context.TenantId` while writing to the strategy-resolved partition (`DeleteResourceHandler.cs:80-91`) — fine while they're equal in Isolated mode, wrong the day they aren't.

## Recommendations Summary

| Priority | Recommendation | Effort | Files affected |
|----------|---------------|--------|-----------------|
| P0 | Fix ValidationBehavior generic response type + registration; add validation E2E test | S | ValidationBehavior.cs, ApplicationServicesRegistration.cs |
| P0 | Enforce If-Match end-to-end (412 on mismatch) or remove the parameter | M | CreateOrUpdateResourceHandler, PatchResourceHandler, repository interface |
| P0 | Implement urn:uuid body-reference rewriting for transactions | L | BundleReferencePreProcessor, BundleEntryExecutor, BundleEntryContext |
| P0 | Single-coordinator atomic transactions; fix Phase-2 index/response mapping; fail commit on batch-write errors | L | BundleProcessor, BundleChannelExecutor |
| P0 | Batch streaming: buffer (not reject) entries with query strings | M | BundleProcessor, BundleChannelExecutor |
| P0 | Exact-segment immutable path check (unblock `identifier` patches) | S | ImmutablePathChecker.cs |
| P0 | PATCH: extract search indices, use context FHIR version, partition strategy | M | PatchResourceHandler.cs |
| P0 | Growable parse buffer; strip PHI from parser exceptions; return pooled buffer | M | StreamingBundleParser.cs |
| P0 | Commit transaction on streamed-batch completion | S | StreamingBundleContext.cs |
| P1 | Correct meta in deferred-path response bodies | S | CreateOrUpdateResourceHandler.cs |
| P1 | Remove HANDLER:/QUEUE: warning-level debug logs | S | CreateOrUpdateResourceHandler, DeferredWriteCoordinator |
| P1 | Type-based (not message-substring) exception→status mapping | S | BundleEntryExecutor.cs |
| P1 | Dispose RecyclableMemoryStreams per bundle entry | S | BundleEntryExecutor.cs |
| P1 | Replace string-rebuild with JsonDocument.ParseValue raw copy | M | StreamingBundleParser, BundleParserState |
| P1 | Fix `_elements` nested paths, decimal precision, SUBSETTED tag | M | ResourceElementsSerializer.cs |
| P1 | 64KB flush threshold; omit empty `entry` arrays | S | StreamingBundleSerializer.cs |
| P1 | 400 (or buffer) when bundle `type` not seen before `entry` | S | StreamingBundleParser, FhirEndpoints |
| P1 | Read typed BundleAssignedResourceId in ConditionalCreate | S | ConditionalCreateHandler.cs |
| P1 | Repository-level COUNT for history `_total=accurate` | M | HistoryCountHelper, IFhirRepository |
| P1 | Store-side $includes pagination (kill re-search + skip-scan) | M | IncludesResourceHandler, execution strategy |
| P1 | Compartment: stop mutating request, add +1 pagination, fix `*` capability expr | M | SearchCompartmentHandler, SearchCompartmentQuery |
| P1 | SearchOptions.Clone(); use at all three copy sites | S | SearchOptions, 3 handlers |
| P1 | Async init for search-param managers (no GetResult, no poll loop) | M | FhirVersionContext, CompositeSearchParameterDefinitionManager |
| P1 | Delete dead bundle paths + PaginationResult; dedupe StartBatchProcessor | S | BundleProcessor, StreamingBundleSerializer |
| P1 | Shared ResourceWrapperFactory for all writers (incl. Provenance) | M | Resource/Patch/ConditionalOperations handlers |
| P1 | Spec-conformant Patch add (`name`) and move (integer indices) | M | FhirPatchParametersParser, Move/Add executors |
| P1 | Copy `User` to bundle-entry contexts; define bundle-entry security model (middleware bypassed) | M | BundleEntryExecutor, AspNetCorePipelineExecutor |
| P2 | `WasCreated` on UpdateResult; drop `VersionId == "1"` heuristic | S | UpdateResult, FhirEndpoints |
| P2 | File splits, renames (`StreamingBundleContext` collision), `ct`→`cancellationToken`, header/style unification, frozen dictionaries, remove swallowed catches | M | ~15 files |
