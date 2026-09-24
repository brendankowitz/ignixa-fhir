# Investigation: API/HTTP Layer Architecture & Technical Review

**Feature**: application-layer-modernization
**Status**: Complete
**Created**: 2026-07-11
**Scope**: Ignixa.Api, Ignixa.Api.OpenIddict, Ignixa.Web

## Summary

The layer respects ADR 2509's macro structure — Minimal API only, no MVC controllers, no `Hl7.Fhir.*` dependencies, mediator-based dispatch, `Ignixa.Web` as a thin host. Below that, the layer has one systemic security defect and heavy copy-paste debt. Authorization is enforced *only* by per-file endpoint-filter registration (`RequireAuthorization` appears nowhere), and roughly a third of endpoint files forgot the filters — `$export`, `$import`, admin package management, system `/_history`, and all agnostic operation routes run with no authorization or audit. Separately, `FhirAuthorizationFilter` wraps the entire downstream pipeline in a `catch (Exception)` that converts every intentional FHIR error (400/404/412) into a generic 500 whenever authorization is enabled. Error response shapes are inconsistent across five different patterns, and the tenant/agnostic route duplication roughly doubles the endpoint code.

## Strengths

- **Layering discipline holds**: zero `Hl7.Fhir.*` usings, zero MVC controllers, zero `[ApiController]` in all three projects; endpoints dispatch via `IMediator.SendAsync` to Application-layer handlers.
- **Streaming-first response design**: search/history/$everything stream bundles directly to `Response.Body` via `StreamingBundleSerializer` (`Endpoints/FhirEndpoints.cs:627`, `Endpoints/HistoryEndpoints.cs:158`), and `FhirResult` does zero-copy byte writes (`Results/FhirResult.cs:112`). `RecyclableMemoryStreamManager` is used consistently for body buffering.
- **`Infrastructure/ProvenanceHeaderHelper.cs`** is a model helper: 16KB size cap (`:29-74`), specific exception types, actionable error messages, correct CT propagation.
- **`Ignixa.Api.OpenIddict/Services/SmartScopeGenerator.cs`** is clean, `sealed`/static, source-generated regexes, correct SMART v2 CRUDS-order validation (`:217-237`).
- **TenantResolutionMiddleware partition-0 block** works as specified in CLAUDE.md: `/tenant/0/` rejected with 400 + OperationOutcome (`Middleware/TenantResolutionMiddleware.cs:59-80`).
- Experimental endpoints are properly config-gated per feature (`Endpoints/Experimental/ExperimentalEndpointExtensions.cs:38-68`) and the tenant group receives the standard filter stack via callback (`Extensions/EndpointRouteBuilderExtensions.cs:76-83`).

## Findings

### P0 — FhirAuthorizationFilter converts all downstream FHIR errors into generic 500s
**Location**: `src/Application/Ignixa.Api/Filters/FhirAuthorizationFilter.cs:68-121`
**Issue**: `return await next(context)` (line 101) sits *inside* the filter's `try` block, and the trailing `catch (Exception ex)` (line 108) returns `CreateErrorResponse("An error occurred during authorization")` — a 500. Endpoint handlers deliberately throw `BadRequestException`, `NotAcceptableException`, etc. (all `FhirException` subclasses, e.g. `Endpoints/FhirEndpoints.cs:437,448,457`) expecting `FhirExceptionMiddleware` to map them to 400/404/406/412. With `Authorization:Enabled=true` the filter intercepts every one of these first and returns a misleading 500 "authorization error". Because tests and the dev profile run with authorization disabled (the disabled path at line 61-64 returns *outside* the `try`), this bug is invisible until auth is turned on.
**Recommendation**: Move `return await next(context)` out of the `try`, or narrow the `catch` to exceptions thrown by `BuildAuthorizationContextAsync`/`AuthorizeAsync` only. Add an E2E test suite that runs with authorization enabled.
**Effort**: S (fix), M (test coverage)

### P0 — Systemic authorization/audit bypass: many endpoints never receive the filter stack
**Location**:
- `src/Application/Ignixa.Api/Endpoints/ExportEndpoints.cs:28-45` — system `$export`, tenant `$export`, `Group/{id}/$export`, status, cancel: no filters
- `src/Application/Ignixa.Api/Endpoints/ImportEndpoints.cs:33-42` — `$import`, status, cancel: no filters
- `src/Application/Ignixa.Api/Endpoints/AdminPackageEndpoints.cs:19-36` — admin package load/list/unload: no filters
- `src/Application/Ignixa.Api/Endpoints/HistoryEndpoints.cs:70,112` — system-level `/_history` (both tenant and agnostic) registered directly on `endpoints`, not the filtered group; the comment "no filter needed" confuses resource-type validation with authorization
- `src/Application/Ignixa.Api/Endpoints/OperationEndpoints.cs:118-166` — *all* agnostic operation routes (`/$validate`, `/{resourceType}/$validate`, `/Patient/{id}/$everything`, `/Patient/$member-match`, `/{resourceType}/$includes`) registered with no filters, while their tenant-prefixed twins (`:66-71`) get all four
- `src/Application/Ignixa.Api/Endpoints/DeIdOperationEndpoints.cs:55` — agnostic `/$de-identify` registered outside the filtered group

**Issue**: `RequireAuthorization()` is used nowhere in the codebase and `UseAuthorization()` has no fallback policy, so `FhirAuthorizationFilter` is the *only* authorization enforcement — and it must be re-attached by hand in every endpoint file. The files above forgot it. Concretely: with authorization enabled, an unauthenticated caller can still run `GET /Patient/123/$everything` (full patient record), `POST /$export` (bulk export of everything), `POST /$import` (bulk write), and admin package mutation — and none of these produce audit events either, because `FhirAuditFilter` is missing too.
**Recommendation**: Stop copy-pasting the filter stack: create one shared `MapFhirGroup(...)` helper that every endpoint file must use, and add a conformance test that walks `EndpointDataSource` asserting every non-allow-listed endpoint has the authorization filter. A global `FallbackPolicy` is *not* a sufficient alternative: `AspNetCorePipelineExecutor` invokes endpoint `RequestDelegate`s directly for bundle entries (`Infrastructure/AspNetCorePipelineExecutor.cs:108`), bypassing `AuthorizationMiddleware`, so policy-based enforcement would silently not apply inside bundles (see Architectural Observation 2).
**Effort**: M

### P0 — OpenIddict authorize endpoint auto-approves every request; dev certificates unconditional
**Location**: `src/Application/Ignixa.Api.OpenIddict/Endpoints/AuthorizationEndpoints.cs:39-57`; `Extensions/OpenIddictServiceExtensions.cs:97-98`
**Issue**: `/connect/authorize` signs in a hardcoded `"dev-user"` with *whatever scopes were requested* — no login, no consent, no credential check. The only gate is the `OpenIddict:Enabled` config flag; there is no `IHostEnvironment.IsDevelopment()` check anywhere in the project. `AddDevelopmentEncryptionCertificate()/AddDevelopmentSigningCertificate()` are always used — there is no code path for production certificates, so any deployment that flips `Enabled: true` (e.g. copying `appsettings.Development.json` patterns, or the checked-in `appsettings.openiddict.json` which has `"Enabled": true` plus `admin/admin123`-style users) becomes an open token mint for the FHIR API. Password comparison is also plaintext, non-constant-time (`Services/DevelopmentUserService.cs:26`).
**Recommendation**: Hard-gate the whole `AddIgnixaOpenIddict` registration (or at minimum the auto-approve authorize handler, password flow, and dev certificates) on `IHostEnvironment.IsDevelopment()`, or fail startup in Production unless an explicit `AllowInsecureDevelopmentIdentityProvider=true` flag is set. Log a prominent warning when active.
**Effort**: S

### P1 — Server ships default-open: authentication and authorization disabled in production config
**Location**: `src/Application/Ignixa.Web/appsettings.json` (`Authorization: { "Enabled": false, "RequireAuthentication": false }`); `src/Application/Ignixa.Api/Registrations/MiddlewareRegistration.cs:41`
**Issue**: The checked-in production `appsettings.json` disables both authentication middleware and FHIR authorization. The code default in `MiddlewareRegistration.cs:41` is `true`, but the shipped config overrides it to `false`, so a default deployment of a healthcare data server accepts anonymous, unaudited access to everything. `MiddlewareRegistration` also conflates authentication and authorization behind the single `Authorization:Enabled` flag (`:41-46`) — you cannot have authenticated-but-unrestricted access.
**Recommendation**: The F5-experience goal (ADR 2509) justifies open *Development* defaults, not open production defaults. Fail or loudly warn at startup when `Authorization:Enabled=false` outside Development; split the authentication toggle from the RBAC toggle.
**Effort**: S

### P1 — Internal exception messages leaked to clients in 500 responses
**Location**: `src/Application/Ignixa.Api/Middleware/FhirExceptionMiddleware.cs:89`; `src/Application/Ignixa.Api/Endpoints/ImportEndpoints.cs:68-71`
**Issue**: For any unhandled exception, `Diagnostics = exception.Message` puts raw internal detail (SQL errors, file paths, connection info) into the client-facing OperationOutcome. `ImportEndpoints.cs:70-71` does the same in a `catch (Exception)` that additionally misclassifies all failures as 400. Also, `FhirExceptionMiddleware.cs:78-82` maps every `InvalidOperationException` to 400 — that exception type is overwhelmingly a *server* bug and this masks 500s as client errors.
**Recommendation**: Return a generic diagnostic plus the correlation ID for non-`FhirException` errors; log full detail server-side only. Drop the `InvalidOperationException`→400 mapping. In ImportEndpoints, narrow the catch to parse-specific exceptions.
**Effort**: S

### P1 — Authorization interaction parser defaults to `Read` (default-permit semantics)
**Location**: `src/Application/Ignixa.Api/Filters/FhirAuthorizationFilter.cs:292-321`
**Issue**: `ParseRoute`'s pattern match falls through to `_ => FhirInteraction.Read` (line 320). Concrete miss: `POST /tenant/1` (no trailing slash) matches the bundle route (`MapPost("/")` on the group tolerates the missing trailing slash) but the tuple arm at line 314 requires `path.EndsWith("/")`, so a *transaction* is authorized as a *read*. Any future route this parser doesn't recognize is likewise authorized under read semantics. Entry-level re-filtering does **not** mitigate this: bundle entries run the authorization filter against a synthetic HttpContext carrying an anonymous principal (verified — see Architectural Observation 2), so the transaction-level check is the only one that ever sees the real caller.
**Recommendation**: Default-deny (throw or return 405/403) on unrecognized method/route combinations; derive the bundle case from the endpoint metadata (`WithName("Bundle")`) rather than string-matching the path.
**Effort**: S

### P1 — Denied and unauthenticated requests are never audited
**Location**: `src/Application/Ignixa.Api/Endpoints/FhirEndpoints.cs:83-86` (filter order), `src/Application/Ignixa.Api/Filters/FhirAuditFilter.cs:14`
**Issue**: `FhirAuthorizationFilter` runs before `FhirAuditFilter` and returns 403 by short-circuiting, so authorization denials — the events a healthcare audit trail most needs (ATNA) — produce no `AuditEvent`. The audit filter's own doc comment ("only audit authorized requests") codifies the gap.
**Recommendation**: Swap the order (audit outermost) or have the authorization filter emit an audit event on denial.
**Effort**: S

### P1 — "Fire-and-forget" audit/metrics is fake async and races with the request lifetime
**Location**: `src/Application/Ignixa.Api/Filters/FhirAuditFilter.cs:52-54,108`; `src/Application/Ignixa.Api/Filters/FhirMetricsFilter.cs:54-56,92`
**Issue**: `CreateAuditEventAsync` contains no real await (`await Task.CompletedTask` at line 108; `auditLogger.LogHttpRequest` is synchronous), so the `#pragma warning disable CS4014` fire-and-forget ceremony currently runs fully synchronously — cargo cult that becomes a genuine bug the moment `IAuditLogger` gains a real async implementation, because the unobserved task captures `HttpContext` and would touch it after the request completes (undefined behavior; contexts are pooled). `FhirMetricsFilter` already has a real await (`RecordMetricAsync`, line 92) and its `catch` block reads `httpContext.Request` after that await (`:97-98`). Additionally `ResponseSizeBytes = httpContext.Response.ContentLength ?? 0` (line 88) is always 0 for streamed (chunked) responses — i.e., for every search result.
**Recommendation**: Snapshot all needed HttpContext data into a DTO synchronously, then hand the DTO to a background channel/queue. Delete the pragmas.
**Effort**: M

### P1 — Single-tenant auto-detect cache never invalidates
**Location**: `src/Application/Ignixa.Api/Middleware/TenantResolutionMiddleware.cs:213-254`
**Issue**: `GetSingleTenantIdAsync` caches either the single tenant ID or the `-1` "multiple tenants" sentinel *forever* in middleware instance state. Adding a second tenant at runtime leaves agnostic routes silently pinned to the original tenant (requests that should now be ambiguous/400 keep writing to tenant 1); deactivating down to one tenant leaves agnostic routes permanently blocked. Also line 216's `_cachedSingleTenantId.HasValue || _cachedSingleTenantId == -1` is redundant — `== -1` implies `HasValue` — evidence the sentinel logic wasn't reasoned through.
**Recommendation**: TTL the cache (seconds-scale) or invalidate via an `ITenantConfigurationStore` change event. Simplify the sentinel to a dedicated enum/record.
**Effort**: S

### P1 — Conditional delete corrupts search criteria when `_count` is present
**Location**: `src/Application/Ignixa.Api/Endpoints/FhirEndpoints.cs:1406-1415`
**Issue**: To strip `_count`, the code rebuilds the query string with `$"{kvp.Key}={kvp.Value}"` over `QueryHelpers.ParseQuery` output. `kvp.Value` is `StringValues`, so repeated parameters collapse to a comma-joined string (`name=a&name=b` → `name=a,b`), and values decoded by `ParseQuery` are not re-encoded (a value containing `&`/`=`/`+` corrupts the criteria passed to `ConditionalDeleteCommand`). A conditional delete can therefore target the wrong resource set.
**Recommendation**: Filter the already-parsed parameter list instead of round-tripping through a string, or re-encode with `QueryString.Create(...)`.
**Effort**: S

### P1 — Unknown or unseen bundle types silently executed as batch
**Location**: `src/Application/Ignixa.Api/Endpoints/FhirEndpoints.cs:1097-1103`
**Issue**: `bundleType switch { "TRANSACTION" => ..., "BATCH" => ..., _ => BundleType.Batch }` means a POSTed `collection`, `searchset`, `document`, or garbage/absent `type` is processed as a batch instead of being rejected. Worse (per the CRUD-slices review, `crud-vertical-slices-review.md`): `BundleContext.BundleType` is also null when the streaming parser hasn't encountered `type` before the `entry` array — legal JSON property ordering — so a well-formed *transaction* bundle whose `type` follows `entry` is silently downgraded to batch semantics (no atomicity). FHIR requires servers to process only batch/transaction at the base endpoint.
**Recommendation**: Reject anything not affirmatively identified as `batch`/`transaction` with 400/422 + OperationOutcome; if the streaming parser cannot guarantee `type` is known before entries stream, buffer until it is.
**Effort**: S (API guard), M (parser ordering)

### P1 — If-Match parsed with the If-None-Match parser; result feeds a dead command parameter
**Location**: `src/Application/Ignixa.Api/Endpoints/FhirEndpoints.cs:478-490,504`
**Issue**: `parsedIfMatch = ConditionalHeaderParser.ParseIfNoneMatch(ifMatchHeader)` (line 482) parses the `If-Match` header with the method written for `If-None-Match` — wrong semantic owner even where weak-ETag syntax overlaps (`*` semantics differ between the two headers). The value flows into `CreateOrUpdateResourceCommand`'s `IfMatch` parameter (line 504), which — per the CRUD-slices review (`crud-vertical-slices-review.md`, filed there as P0) — no Application handler ever reads: optimistic concurrency on PUT is entirely unenforced, so this parse bug is currently masked by the dead parameter. Fix both together.
**Recommendation**: Add a dedicated `ParseIfMatch` (with `*` handling) on the API side; the missing 412 version-check enforcement is tracked in the CRUD review.
**Effort**: S (API side)

### P1 — Five inconsistent error-response shapes; several produce wrong content-type or wrong JSON
**Location** (representative):
- FHIR-correct: `Results/FhirResults.cs:150` and `Filters/FhirAuthorizationFilter.cs:137-140` (serialized OperationOutcome, `application/fhir+json`)
- RFC 7807 problem+json: `Endpoints/FhirEndpoints.cs:392-395` (410 Gone)
- Anonymous objects: `Endpoints/FhirEndpoints.cs:1087` (`new { error = ... }`), `Endpoints/PatchEndpoints.cs:153`, `Endpoints/ExportEndpoints.cs:399`, `Endpoints/ImportEndpoints.cs:285`
- STJ-serialized wrapper types with `application/json`: `Filters/ResourceTypeValidationFilter.cs:94` (`Results.Json(outcome, ...)` serializes the `OperationOutcomeJsonNode` wrapper's CLR properties, likely emitting non-FHIR JSON), `Endpoints/OperationEndpoints.cs:390` (`Results.Ok(result.OperationOutcome)`), `Endpoints/CompartmentEndpoints.cs:140,226,316`, `Endpoints/FhirEndpoints.cs:1726` (`Results.BadRequest(operationOutcome.MutableNode)` — right JSON, wrong content-type, while `FhirResults.BadRequest` exists for exactly this)
- Bodyless: `Endpoints/FhirEndpoints.cs:588,1132,1189` (`Results.StatusCode(500)` with no OperationOutcome)

**Issue**: Clients cannot rely on a single error contract; several of these are FHIR-conformance failures outright.
**Recommendation**: One `FhirResults.Error(status, issueType, diagnostics)` helper; ban `Results.BadRequest`/`Results.Json`/anonymous error objects in this layer via a Roslyn banned-API list.
**Effort**: M

### P1 — Duplicated Prefer-header parsing has already drifted (`mode=` vs `validation=`)
**Location**: `src/Application/Ignixa.Api/Endpoints/OperationEndpoints.cs:398-425` vs `src/Application/Ignixa.Api/Infrastructure/PreferHeaderParser.cs:68-94`
**Issue**: `$validate` parses `Prefer: mode=minimal|spec|full` via its own private `ParseValidationDepthFromPreferHeader`, while every other endpoint uses `PreferHeaderParser.TryParseValidationLevel` which parses `Prefer: validation=...`. Two vocabularies for the same preference, one of them invisible to the shared parser — classic copy-paste drift. `PreferHeaderParser` itself triplicates its split/trim/prefix-match loop across `TryParseValidationLevel`/`TryParseReturnPreference`/`ParseReturnPreferenceStrict`/`IsStrictHandling`.
**Recommendation**: Single tokenizing parser for the Prefer header; pick one preference key for validation depth and document it.
**Effort**: S

### P1 — `/metadata` unreachable in multi-tenant mode; code comments contradict middleware behavior
**Location**: `src/Application/Ignixa.Api/Middleware/TenantResolutionMiddleware.cs:184-206`; `src/Application/Ignixa.Api/Endpoints/MetadataEndpoints.cs:60-90`
**Issue**: `IsResourceEndpoint` only exempts `/health` and `/.well-known`, so `/metadata` is treated as a resource route; in multi-tenant mode the middleware answers it with 400 "Tenant ID is required". The handler's doc comment ("In multi-tenant scenarios, returns system-wide capabilities", line 63) describes unreachable behavior, and its inline comment ("TenantResolutionMiddleware doesn't auto-detect because it's not classified as a resource endpoint", lines 79-81) is factually wrong. `FhirRequestContextMiddleware.cs:20-24` similarly documents a pipeline order (routing *after* the middleware) that contradicts the actual implicit `UseRouting`-first pipeline.
**Recommendation**: Add `/metadata` to the middleware's exemption list and return system-wide capabilities; fix or delete the stale ordering comments.
**Effort**: S

### P1 — Sync-over-async in DI registration
**Location**: `src/Application/Ignixa.Api/Registrations/DataLayerRegistration.cs:242,256`
**Issue**: `factory.CreateClientAsync().GetAwaiter().GetResult()` and `tenantStore.GetTenantConfigurationAsync(1, default).AsTask().GetAwaiter().GetResult()` inside DI factory lambdas block threads and can deadlock; the second also hardcodes tenant 1 and passes `default` cancellation.
**Recommendation**: Restructure to async initialization (hosted service or `IOptions` factory that resolves lazily on first async use).
**Effort**: M

### P1 — Client-application `Roles` config is silently dead
**Location**: `src/Application/Ignixa.Api.OpenIddict/Configuration/ClientApplicationOptions.cs` (`Roles` property); `Endpoints/TokenEndpoints.cs:56-91`
**Issue**: `appsettings.Development.json` assigns `"Roles": ["Admin"]` etc. to client-credentials clients, and `OpenIddictDataSeeder` accepts the config, but `HandleClientCredentialsGrantAsync` never adds role claims to the token — only subject, name, and scopes. Any RBAC rule keyed on roles silently fails for machine clients; the config knob is a no-op.
**Recommendation**: Emit configured roles as role claims in the client-credentials grant, or delete the property.
**Effort**: S

### P1 — Insecure defaults and `EnsureCreated` in OpenIddict setup
**Location**: `src/Application/Ignixa.Api.OpenIddict/Configuration/OpenIddictServerOptions.cs` (`DisableAccessTokenEncryption { get; set; } = true;`); `Extensions/OpenIddictServiceExtensions.cs:149`
**Issue**: Token encryption is off *by default* in the options class (config must opt back in). Database initialization uses `EnsureCreatedAsync`, which is incompatible with EF migrations — fine for the in-memory dev store, wrong for the SQL Server path the same method supports.
**Recommendation**: Default `DisableAccessTokenEncryption` to `false`; use migrations (or restrict SQL support explicitly).
**Effort**: S

### P1 — Log-injection sanitization applied inconsistently
**Location**: sanitized: `Endpoints/FhirEndpoints.cs` (throughout), `Middleware/TenantResolutionMiddleware.cs`; unsanitized route/user values logged: `Endpoints/CompartmentEndpoints.cs:124-128,147`, `Endpoints/PatchEndpoints.cs:147,152`, `Endpoints/OperationEndpoints.cs:649`
**Issue**: Route values can carry percent-encoded CR/LF that ASP.NET decodes, so the unsanitized files are injectable while others aren't. Three files also hand-roll `.Replace('\r',' ').Replace('\n',' ')` (`Filters/FhirAuthorizationFilter.cs:111-113`, `Filters/FhirAuditFilter.cs:85-87,114-115`, `Filters/FhirMetricsFilter.cs:97-98`) instead of using the existing `LogSanitizationExtensions.SanitizeForLog`.
**Recommendation**: Use `SanitizeForLog` everywhere user-influenced values are logged; better, rely on structured-logging placeholders plus a log sink that encodes newlines, and delete the per-call ceremony.
**Effort**: S

### P1 — `FhirEndpoints.cs` is a 1,744-line God-file with ~2x duplicated registration and 250-line handlers
**Location**: `src/Application/Ignixa.Api/Endpoints/FhirEndpoints.cs` (whole file; duplication `:77-178` vs `:186-310`; `HandlePostResource` `:748-1025`)
**Issue**: The tenant and agnostic registration blocks are near-identical ~120-line copies differing only in how tenantId is sourced. `HandlePostResource` is ~280 lines mixing conditional-create protocol, ID generation (`Guid.NewGuid` at line 925 — a business decision living in the API layer), body validation, provenance/TTL/Prefer parsing, and three-way response shaping — far past CLAUDE.md's <25-line guideline. `HandleSearchResource`/`HandlePostSearchResource`/`HandleBaseSearchResource`/`HandlePostBaseSearchResource` are four copies of the same pipeline.
**Recommendation**: One registration routine parameterized by tenant-id source; extract a `ResourceRequestReader` (body + headers → command inputs) and a `WritePreferenceResponder` (result + preference → IResult); move server-ID generation into the Application handler.
**Effort**: L

### P2 — `CancellationToken ct` naming violates the project's own convention
**Location**: `Endpoints/FhirEndpoints.cs` (~22 occurrences, e.g. `:98,323`), `Endpoints/HistoryEndpoints.cs:96-133`, `Endpoints/PatchEndpoints.cs:70-121` registration lambdas
**Issue**: CLAUDE.md mandates `cancellationToken`; `OperationEndpoints.cs`/`CompartmentEndpoints.cs` comply, these files don't — drift between files written at different times.
**Recommendation**: Rename; add an analyzer/naming rule.
**Effort**: S

### P2 — Warnings-as-errors is diluted by a 35-entry NoWarn list
**Location**: `Directory.Build.props` (`<NoWarn>` includes CA1031 catch-general, CA1062 null-validate, CA2201 reserved exceptions, CA1852 seal-types, CA1508 dead-condition, …)
**Issue**: The suppressions disable exactly the analyzers that would have caught several findings above (broad catches, unsealed types, dead conditions).
**Recommendation**: Re-enable at least CA1031, CA1852, CA1508 and fix or locally suppress with justification.
**Effort**: M

### P2 — Sealed-by-default not followed
**Location**: 20+ classes: `Middleware/*.cs` (all three), `Filters/*.cs` (all four), `Services/*.cs`, `Infrastructure/AspNetCorePipelineExecutor.cs:23`, `Infrastructure/DurableTaskHostedService.cs:14`, `Api.OpenIddict/Data/OpenIddictDbContext.cs:8`
**Issue**: None are designed for inheritance; `TenantResolutionMiddleware` even implements the full `protected virtual Dispose(bool)` pattern (`:265-287`) on a conventional middleware ASP.NET Core never disposes.
**Recommendation**: `sealed` everywhere; replace the Dispose ceremony with a plain `Dispose`.
**Effort**: S

### P2 — Dead code and vestigial artifacts
**Location / items**:
- `Endpoints/FhirEndpoints.cs:1732-1742` — `ToAsyncEnumerable<T>` defined, never called
- `Endpoints/FhirEndpoints.cs:8` — `using Azure;` with no apparent Azure SDK usage in the file
- `Registrations/MiddlewareRegistration.cs:92-104` — `UseIgnixaDevelopmentFeatures` never called (duplicates `ApplicationBuilderExtensions:35-38`)
- `Registrations/MiddlewareRegistration.cs:66-87` — dev "ordering validation" middleware whose condition (`RequestContext?.TenantId == 0`) can only fire false positives on non-tenant endpoints; validates nothing
- `Results/FhirResult.cs:21,30` — `_httpContext` stored, threaded through every `With*` copy, never read
- `Endpoints/PatchEndpoints.cs:388-393` — `IsValidResourceType` always returns `true`, with a `TODO` (CLAUDE.md bans leftover TODOs); its 404 path at `:150-154` is unreachable
- `Endpoints/SmartEndpoints.cs:123-126` — `BuildSmartConfigurationResponse` parameters `context` and `tenantId` unused
- `Middleware/FhirRequestContextMiddleware.cs:83` — `ExecutingBatchOrTransaction = fhirContext.DeferredWriteCoordinator != null` on a freshly constructed context is always `false`; the comment admits it
- `Endpoints/FhirEndpoints.cs:508-509,980-981` — manual `Headers.Append("ETag"/"Last-Modified")` immediately overwritten by `FhirResult.ExecuteAsync`'s indexer writes (`Results/FhirResult.cs:94,100`); works only by accident of set-vs-append semantics

**Recommendation**: Delete all of the above.
**Effort**: S

### P2 — One-type-per-file violations
**Location**: `Middleware/FhirExceptionMiddleware.cs:18,102` (middleware + extensions class); `Infrastructure/PreferHeaderParser.cs:18,51` (enum `ReturnPreference` + parser); `Endpoints/SmartEndpoints.cs:20,151` (endpoints + `SmartConfiguration` record); `Services/SqlReferenceDataPreloadService.cs:22` (file named `...Service`, type named `SqlReferenceDataPreloadHandler`)
**Recommendation**: Split per repo rule; rename the mismatched file.
**Effort**: S

### P2 — `CreateOperationOutcome` helper copy-pasted into six endpoint files
**Location**: `Endpoints/OperationEndpoints.cs:171-186`, `Endpoints/ImportEndpoints.cs`, `Endpoints/DeIdOperationEndpoints.cs`, `Endpoints/Experimental/SummaryEndpoints.cs`, `Endpoints/Experimental/TerminologyEndpoints.cs`, `Endpoints/Experimental/TransformEndpoints.cs`
**Issue**: Six private near-duplicates (signatures already drifting — ImportEndpoints' takes only a string). The "TenantId not found…" agnostic-route boilerplate is likewise repeated 4x in OperationEndpoints alone (`:214-220,237-243,277-283,621-627`).
**Recommendation**: One shared factory next to `FhirResults`.
**Effort**: S

### P2 — Copyright headers are wrong or inconsistent
**Location**: most `Ignixa.Api` files claim `Copyright (c) Microsoft Corporation` (e.g. `Endpoints/FhirEndpoints.cs:2`, `Middleware/TenantResolutionMiddleware.cs:2`, `Ignixa.Web/Program.cs:2` — some with missing spaces, "Corporation.All rights reserved"); `Middleware/FhirRequestContextMiddleware.cs:2` says "Ignixa Contributors"; the entire OpenIddict project has no headers. `Directory.Build.props` says the product copyright is "Ignixa Contributors".
**Recommendation**: Pick one header (or none) and apply repo-wide; the Microsoft attribution looks like template residue and is a licensing-hygiene problem.
**Effort**: S

### P2 — Miscellaneous correctness/polish
- `Endpoints/FhirEndpoints.cs:516` — created-vs-updated decided by `result.Key.VersionId == "1"` string compare; brittle (delete/recreate) and a semantic the Application layer already knows.
- `Endpoints/FhirEndpoints.cs:529` — PUT `Location` header ignores the agnostic-route case and omits the `_history/{version}` suffix, unlike POST (`:1001-1005`) and conditional create (`:829-833`); three different Location formats for writes.
- `Endpoints/FhirEndpoints.cs:829,874,1001` — `context.Items.ContainsKey("IsAgnosticRoute") && (bool)context.Items["IsAgnosticRoute"]!` double-lookup + forgiven cast, three copies.
- `Endpoints/FhirEndpoints.cs:1107-1112` — `MaxParallelism = 10, ChannelCapacity = 100` hardcoded in the endpoint; belongs in options.
- `Endpoints/MetadataEndpoints.cs:158-159` — error message literally contains the text "KnownContentTypes.ApplicationFhirJson, KnownContentTypes.ApplicationJson" — constant *names* interpolated as prose instead of values; telltale generated-code artifact.
- `Endpoints/MetadataEndpoints.cs:128-161` — Accept-header validation exists only on metadata endpoints; ignores q-values; resource endpoints do no content negotiation at all.
- `Endpoints/HistoryEndpoints.cs:164` — instance-level history hardcodes `pretty: false` while type/system levels honor `_pretty`.
- `Endpoints/PatchEndpoints.cs:175,316` — `(char)reader.Peek()` on an empty body yields `(char)65535`; also the JSON-Patch detection block is duplicated verbatim across both handlers.
- `Endpoints/PatchEndpoints.cs:339` — `context.RequestServices.GetRequiredService<ILoggerFactory>()` service-location inside a handler; every other handler injects it.
- `Endpoints/CompartmentEndpoints.cs:216` — `validCompartmentTypes` array allocated per request; compartment membership is a spec/business rule hardcoded in the API layer.
- `Filters/FhirAuthorizationFilter.cs:6,103` — `Grpc.Core`/`RpcException` transport concern leaking into an API filter; the Application-layer authorization service should translate sidecar transport failures.
- `Filters/FhirAuthorizationFilter.cs:184-260` — `BuildAuthorizationContextAsync` is sync work wrapped in `Task.FromResult`.
- `Registrations/MiddlewareRegistration.cs:34-37` — when `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, ASP.NET Core already inserts ForwardedHeaders middleware automatically; this adds it a second time.
- `Ignixa.Web/Program.cs:83,150-151` — startup service calls (`GetAllTenantsAsync`, `GetTenantConfigurationAsync`) without cancellation tokens; `:170` assigns `repository` and never uses it.
- `Extensions/ApplicationBuilderExtensions.cs:37` — `((IEndpointRouteBuilder)app).MapOpenApi()` cast hack; take `WebApplication` instead.
- `Api.OpenIddict/Services/SmartScopeGenerator.cs:65-98` — full generation is 4 contexts x ~200 union resource types x 31 permission combos ≈ 25k registered scopes; check the size this imposes on discovery metadata and OpenIddict scope validation.
- `Infrastructure/ProvenanceHeaderHelper.cs:134` — `coordinator` parameter typed `object?`; type it as the coordinator interface.

## Architectural Observations

1. **ADR 2509 macro-compliance, micro-erosion.** Dependency direction is correct (API → Application → Domain; no reverse references; no `Hl7.Fhir.*`; no controllers). But "thin endpoints" has eroded: handlers own protocol parsing *and* orchestration decisions (bundle-type routing, server ID generation, created-vs-updated detection, conditional-create response semantics). The Application layer should receive one command and return one result that already answers those questions.

2. **Authorization architecture is convention-without-enforcement.** The decision to use endpoint filters instead of middleware (documented in `FhirAuthorizationFilter.cs:24-28`) is defensible for bundle-entry re-entry, but it makes security opt-in per endpoint file with no compile-time or startup-time check. The six files that forgot the filters (P0 above) are the predictable outcome. This needs a single choke point: shared group factory + startup assertion over `EndpointDataSource`. **Verified constraint on the fix**: an ASP.NET fallback authorization *policy* is NOT sufficient on its own — `AspNetCorePipelineExecutor.ExecuteAsync` (`Infrastructure/AspNetCorePipelineExecutor.cs:47-114`) hand-matches routes and invokes the selected endpoint's `RequestDelegate` directly (line 108), so `AuthorizationMiddleware` (which enforces authorization metadata for Minimal APIs) never runs for bundle entries; only filters compiled into the RequestDelegate execute. Compounding it, `BundleEntryExecutor` (Application layer, see `crud-vertical-slices-review.md`, verified P1 there) builds the per-entry `DefaultHttpContext` without copying the parent `HttpContext.User`, so even the endpoint filters that do run evaluate an anonymous principal. Net: bundle entries are effectively unauthorized regardless of which enforcement mechanism is chosen, and a caller can reach endpoints through a bundle under weaker checks than a direct request. The fix must therefore be filter-based (or the executor must run the real pipeline) *and* propagate `User` into entry contexts.

3. **Two competing tenancy-flow mechanisms.** Some commands carry `TenantId` explicitly (`ConditionalCreateCommand`, `ConditionalUpdateCommand`, `ConditionalDeleteCommand`, `PatchResourceCommand`), others rely on ambient `IFhirRequestContext` (`GetResourceQuery`, `SearchResourcesQuery`, `DeleteResourceCommand` — see `FhirEndpoints.cs:371,614,1043`). Handlers reading tenant from two places is how cross-tenant bugs are born; pick one (ambient context, given it already exists) and strip tenantId from command signatures.

4. **The tenant/agnostic dual-registration pattern doubles every endpoint file.** Five files (Fhir, Operation, Patch, Compartment, History) each contain two near-identical registration methods plus per-route "TenantId not found" fallbacks. Since `TenantResolutionMiddleware` already normalizes tenant into `HttpContext.Items`/`IFhirRequestContext` for both route shapes, a single registration helper that maps both prefixes onto one handler set would delete ~600 lines and remove the class of "agnostic twin forgot X" bugs (which is exactly how the P0 filter gaps and the PUT-Location inconsistency happened).

5. **Error contract needs an owner.** `FhirExceptionMiddleware` + thrown `FhirException`s is the right backbone, but half the layer bypasses it with ad-hoc try/catch and five different response shapes, and the authorization filter actively breaks it (P0). Rule of thumb this layer should adopt: endpoints throw or return via `FhirResults`; nothing else constructs error bodies.

6. **Generated-code fingerprints are pervasive**: identical XML-doc boilerplate, per-file re-derived helpers, comments that describe intent the code contradicts (`FhirRequestContextMiddleware` ordering, `MetadataEndpoints` auto-detect claim, `HistoryEndpoints` "no filter needed"), constant names pasted into user-facing strings, and pragma-suppressed patterns copied without their rationale. Treat comments in this layer as untrustworthy until re-verified.

7. **Ignixa.Web is appropriately thin** (composition + startup validation only), and the startup fail-fast strategy-validation in `Program.cs:112-135` is good. The OpenIddict project, however, is a development tool packaged like a production feature — it needs either environment gating (P0) or an explicit "dev-only" module boundary.

## Recommendations Summary

| Priority | Recommendation | Effort | Files affected |
|----------|---------------|--------|-----------------|
| P0 | Move `next(context)` out of FhirAuthorizationFilter's catch-all; add auth-enabled E2E suite | S | Filters/FhirAuthorizationFilter.cs |
| P0 | Centralize filter attachment (shared group factory) + startup assertion that every endpoint is authorized or explicitly anonymous | M | Export/Import/AdminPackage/History/Operation/DeId endpoints, Extensions/EndpointRouteBuilderExtensions.cs |
| P0 | Environment-gate OpenIddict dev IdP (auto-approve authorize, password flow, dev certs) | S | Ignixa.Api.OpenIddict (Extensions, Endpoints) |
| P1 | Secure-by-default posture: warn/fail in Production when authorization disabled; split authn/authz toggles | S | appsettings.json, MiddlewareRegistration.cs |
| P1 | Stop leaking `exception.Message` in 500s; drop IOE→400 mapping | S | Middleware/FhirExceptionMiddleware.cs, Endpoints/ImportEndpoints.cs |
| P1 | Default-deny in interaction parser; audit denials (reorder filters) | S | Filters/FhirAuthorizationFilter.cs, group registrations |
| P1 | Replace fake fire-and-forget audit/metrics with snapshot + background channel | M | Filters/FhirAuditFilter.cs, Filters/FhirMetricsFilter.cs |
| P1 | Add invalidation/TTL to single-tenant cache | S | Middleware/TenantResolutionMiddleware.cs |
| P1 | Fix `_count` query-string reconstruction; reject unknown/unseen bundle types | S | Endpoints/FhirEndpoints.cs |
| P1 | Dedicated If-Match parser (pairs with dead-IfMatch P0 in crud-vertical-slices-review.md) | S | Endpoints/FhirEndpoints.cs |
| P1 | Unify error responses behind FhirResults; ban Results.Json/BadRequest for errors | M | ~10 endpoint/filter files |
| P1 | Deduplicate Prefer parsing; single validation-preference key | S | Infrastructure/PreferHeaderParser.cs, Endpoints/OperationEndpoints.cs |
| P1 | Fix `/metadata` in multi-tenant; correct stale pipeline comments | S | Middleware/TenantResolutionMiddleware.cs, Endpoints/MetadataEndpoints.cs |
| P1 | Remove sync-over-async from DI factories | M | Registrations/DataLayerRegistration.cs |
| P1 | Emit client Roles claims or delete the config property; fix insecure OpenIddict defaults; migrations over EnsureCreated | S | Ignixa.Api.OpenIddict |
| P1 | Apply SanitizeForLog uniformly | S | Compartment/Patch/Operation endpoints, Filters |
| P1 | Decompose FhirEndpoints.cs (single registration routine, request-reader + preference-responder helpers, move ID generation to Application) | L | Endpoints/FhirEndpoints.cs |
| P2 | Rename `ct` → `cancellationToken`; seal classes; one type per file; delete dead code/TODOs; shared CreateOperationOutcome; fix copyright headers; re-enable CA1031/CA1852/CA1508 | M | layer-wide |

---
*Reviewed 2026-07-11. Direct file reads covered all of Ignixa.Api (Endpoints, Middleware, Filters, Results, Infrastructure header helpers, Extensions, Registrations/MiddlewareRegistration + DataLayerRegistration excerpts), all of Ignixa.Api.OpenIddict, and Ignixa.Web. Registrations/Services/BackgroundServices internals and Experimental endpoint bodies received a lighter pass (pattern sweeps + targeted reads); findings there are limited to what was verified.*
