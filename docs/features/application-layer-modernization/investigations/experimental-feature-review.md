# Investigation: Features/Experimental Review

**Feature**: application-layer-modernization
**Status**: Complete
**Created**: 2026-07-11
**Scope**: Ignixa.Application/Features/Experimental (103 .cs files, not 97 as commonly quoted)

## Summary

`Features/Experimental` is a deliberate, documented mechanism — a config-toggled staging area (master switch + per-feature flags, `docs/features/experimental-library/`, ADR-2512) — not an agent dumping ground. Its size is explained by five unrelated subsystems sharing only a feature flag: GraphQL (33 files, newest, highest quality), MCP (31), IPS $summary (18), Transform (9), Terminology (6). However, the original plan (a separate `Ignixa.Application.Experimental` project) was half-executed: the code landed as a folder inside `Ignixa.Application`, the "remove old code" migration phase was never finished (13 byte-identical dead twins remain in `Ignixa.Application.Operations`), and the folder carries serious latent defects — MCP authorization is entirely dead wiring (fail-open), and IPS package-driven strategies are silently discarded by a broken handler→service handoff.

## Contents Inventory

| Subsystem | Files | What it is | Reachable in production? |
|---|---|---|---|
| `Configuration/` | 2 | `ExperimentalOptions` (master + per-feature flags), `GraphQlExperimentalOptions` | Yes — bound at startup |
| `Infrastructure/` | 2 | Autofac + IServiceCollection registration, gated per feature flag | Yes — called from `Ignixa.Api/Extensions/ServiceCollectionExtensions.cs:60,115` |
| `GraphQl/` | 33 | FHIR `$graphql` operation: HotChocolate type module generated from FHIR schema, resolvers, directives (`@flatten`/`@first`/`@singleton`/`@slice`), scalars, error mapping, schema warmup | Yes — `GraphQlEndpoints.cs`; enabled by default |
| `Ips/` | 18 | Patient `$summary` (International Patient Summary): strategies, registry, StructureDefinition-driven section parsing, bundle assembly | Yes — `SummaryEndpoints.cs`; enabled by default |
| `Mcp/` | 31 | Model Context Protocol server tools: FHIR read/search/history/patch, package install/uninstall/search, tenant listing, diagnostic spike, authorization service, 10 DTOs | Yes — `McpEndpoints.cs` maps `/mcp` |
| `Terminology/` | 6 | `$expand`, `$translate`, `$subsumes` handlers (thin wrappers over `ITerminologyService`) | Yes — `TerminologyEndpoints.cs` |
| `Transform/` | 9 | StructureMap `$transform` via FHIR Mapping Language: handler, map/expression caches, ConceptMap resolver, timeout-guarded FHIRPath | Yes — `TransformEndpoints.cs` |

The folder is cohesive in *mechanism* (everything is flag-gated and registered through the two Infrastructure files) but not in *content* — the five subsystems share nothing else. Endpoints live correctly in `Ignixa.Api/Endpoints/Experimental/`.

**Cross-project leakage**: `Ignixa.Application.BackgroundOperations/JobManagement/*Tool.cs` consumes `Experimental/Mcp/Dtos/JobStatusDto.cs` and `JobSummaryDto.cs` — MCP job-management tools live *outside* the Experimental folder but depend on DTOs *inside* it. "Experimental" is not actually isolated.

**Not deleted after migration**: `Ignixa.Application.Operations/Features/Transform/` (7 files) and `Ignixa.Application.Operations/Features/Terminology/` (6 files) are byte-identical (modulo namespace/usings) to the Experimental copies. Nothing in `src` references the old namespaces except the files themselves; production DI registers only the Experimental copies (`ExperimentalAutofacRegistration.cs`). The old copies are kept compiled solely by `test/Ignixa.Application.Tests/Features/Transform/*` — meaning the *live* Transform/Terminology code has no direct tests, and the *tested* code is dead.

## Strengths

- **GraphQL subsystem is genuinely good.** `GraphQlExecutionService.cs`, `FlattenResultProcessor.cs`, `ReferenceResolver.cs`, `FhirGraphQlErrorMapping.cs` show careful error taxonomy (coded errors mapped to FHIR OperationOutcome issue types in `FhirGraphQlErrorFilter.cs`), deliberate `OperationCanceledException` pass-through in every resolver catch, comments that explain *why* (e.g., `CreateResultWithData` documenting the `IsDataSet` trap, directive types documenting why post-processing is used instead of HC middleware), and thorough tests (10 test files in `test/Ignixa.Application.Tests/Features/Experimental/GraphQl/`).
- **The feature-flag registration pattern works.** `ExperimentalAutofacRegistration.cs` / `ExperimentalServicesRegistration.cs` are clean, master-switch-first, per-feature gated — exactly what the library proposal specified.
- **Security-conscious defaults in GraphQL options**: `EnableGraphQlIde` and `IncludeExceptionDetails` default to `false` with doc comments explaining the exposure (`GraphQlExperimentalOptions.cs:16-31`).
- **IPS FHIRPath usage**: `StructureDefinitionStrategyFactory.cs` and `SectionMetadataParser.cs` use FHIRPath (`Select`/`Scalar`) for StructureDefinition navigation per the project convention.
- **`FlattenResultProcessor` ordering invariant** (@slice before @flatten) is documented and tested.

## Findings

### P0 — MCP authorization is dead wiring; all MCP tools run without authorization
**Location**: `Features/Experimental/Mcp/Tools/TenantAwareMcpTool.cs:35-83`, every tool constructor (e.g. `GetResourceTool.cs:33`, `PatchResourceTool.cs:35`, `UninstallPackageTool.cs:29`), `Ignixa.Api/Endpoints/Experimental/McpEndpoints.cs:27`
**Issue**: A complete authorization layer exists (`IMcpAuthorizationService`, `McpAuthorizationService` — registered in `ExperimentalAutofacRegistration.cs:112-114`) but is never invoked. `TenantAwareMcpTool` takes the service as an *optional* constructor parameter "for backward compatibility" and silently skips checks when it is null (`EnsureMcpAccessAsync` is a no-op). Grep confirms: **no tool passes it to the base, and no tool calls `EnsureMcpAccessAsync`/`EnsureOperationAuthorizedAsync`.** `McpEndpoints.MapMcp("/mcp")` adds no `RequireAuthorization` either. Tools include writes (`patch_fhir_resource`, `patch_resource_field`), admin operations (`install_fhir_package`, `uninstall_fhir_package`), and tenant enumeration (`list_tenants_info`). This is fail-open by construction: a missing registration silently disables security rather than failing.
**Recommendation**: Make `IMcpAuthorizationService` a required base-class dependency; call `EnsureOperationAuthorizedAsync` with the correct `McpOperationType` at the top of every tool method; add `RequireAuthorization` to the MCP endpoint group. Delete the null-tolerant "backward compatibility" path — nothing depends on it.
**Effort**: M

### P0 — IPS package-registered strategies are silently discarded (broken handler→service handoff)
**Location**: `Features/Experimental/Ips/Generator/IpsGeneratorService.cs:48-50,132-142` vs `Ips/Generator/IpsGeneratorHandler.cs:68-109` and `Ips/Events/PackageInstalledStrategyRegistrationHandler.cs`
**Issue**: The whole point of `PackageInstalledStrategyRegistrationHandler` + `IpsGenerationStrategyRegistry` is to add StructureDefinition-driven strategies at runtime when packages install. The handler correctly resolves such a strategy from the registry, then passes only `strategy.BundleProfile` (a string) to `IpsGeneratorService.GenerateIpsAsync`. The service re-resolves the strategy — but from `IEnumerable<IIpsGenerationStrategy>` (constructor-injected DI enumerable, which contains **only** `DefaultIpsGenerationStrategy`), never from the registry. `_strategyByProfile.TryGetValue` misses and falls back to `_defaultStrategy`. Net effect: every dynamically registered strategy is registered, selected, logged — and then ignored. The strategy-selection logic is also duplicated across handler and service.
**Recommendation**: Inject `IIpsGenerationStrategyRegistry` into `IpsGeneratorService` (or pass the resolved `IIpsGenerationStrategy` instance instead of a profile string) and delete one of the two `SelectStrategy` implementations.
**Effort**: S

### P0 — PatchResourceTool numeric values are always emitted as `valueString`
**Location**: `Features/Experimental/Mcp/Tools/FhirOperations/PatchResourceTool.cs:206` vs `:281-306`
**Issue**: `ParseOperationsJson` converts JSON numbers to `decimal` (`valueProp.GetDecimal()`), but `BuildPatchParameters` only pattern-matches `bool`/`int`/`double`/`string`/`JsonNode`. A `decimal` matches none of these and falls into the fallback `valuePart["valueString"] = JsonSerializer.Serialize(op.Value)` — so every numeric patch value is sent to the patch handler as `valueString` instead of `valueDecimal`/`valueInteger`. Arrays/objects likewise degrade to raw-text strings. `PatchResourceFieldTool.cs:263-296` duplicates the same `CreateValuePart` logic with the same gap, and its `object value` parameter will typically arrive as `JsonElement` from the MCP SDK, matching *no* branch.
**Recommendation**: Handle `decimal` (and `JsonElement`) explicitly; add a unit test that patches a numeric field through the MCP tool. Extract the duplicated `BuildPatchParameters`/`CreateValuePart` into one shared helper.
**Effort**: S

### P0 — 13 dead byte-identical files in Ignixa.Application.Operations; live code untested, dead code tested
**Location**: `src/Application/Ignixa.Application.Operations/Features/Transform/` (7 files), `.../Features/Terminology/` (6 files); tests at `test/Ignixa.Application.Tests/Features/Transform/*.cs`
**Issue**: Migration Phase 5 ("remove old code from source projects", `docs/features/experimental-library/investigations/library-proposal.md:599-604`) was never executed. Production DI and endpoints reference only the `Experimental` namespaces; the Operations copies are unreachable, but their unit tests (`FhirPathEvaluatorWithTimeoutTests`, `MapRegistryCacheTests`, `TransformResourceHandlerTests`, `ConceptMapResolverServiceTests`, `FhirPathExpressionCacheTests`) still target the dead twins. Any fix applied to the live copy will not be covered — and a fix applied only to the tested copy does nothing. This is the classic divergence trap; today the copies are identical only by luck.
**Recommendation**: Delete the Operations `Transform/` and `Terminology/` folders; retarget the five test files at the `Experimental` namespaces. Verify `Ignixa.Application.Operations` still builds (nothing else references them).
**Effort**: S

### P1 — MapRegistryCache is per-request: the "performance cache" never caches, and invalidation is a no-op
**Location**: `Infrastructure/ExperimentalAutofacRegistration.cs:143-146` (`.InstancePerLifetimeScope()`), `Transform/MapRegistryCache.cs:36-49` (claims "200-500 transforms/sec (cached)"), `Transform/Events/PackageLoadedMapCacheInvalidationHandler.cs`
**Issue**: `MapRegistryCache` was written as a long-lived cache (ConcurrentDictionary, TTL, hit/miss statistics, package-aware invalidation) but is registered per-lifetime-scope because it depends on the request-scoped `StructureMapParser`. Every request gets a fresh empty cache, so every `$transform` by canonical URL re-loads and re-parses the StructureMap; the statistics, TTL, and `InvalidatePackage` machinery are inert. Worse, `PackageLoadedMapCacheInvalidationHandler` (registered `InstancePerDependency`) resolves its *own* fresh `MapRegistryCache`, invalidates it, and logs success — a complete no-op that reads as if it works.
**Recommendation**: Either make the cache a true singleton keyed by (FhirVersion, url) with the parser resolved per-call, or delete the cache/statistics/invalidation machinery and load maps directly. Half-singleton semantics with per-request lifetime is the worst of both.
**Effort**: M

### P1 — Sync-over-async in the Transform pipeline
**Location**: `Transform/FhirPathEvaluatorWithTimeout.cs:102-108` (`.GetAwaiter().GetResult()`), `Transform/ConceptMapResolverService.cs:122-148` (same), `Transform/FhirPathEvaluatorWithTimeout.cs:64` (`Task.Run`)
**Issue**: The synchronous `MappingEvaluator` forces blocking wrappers. `Evaluate()` blocks on `EvaluateAsync` and drops the caller's `CancellationToken` entirely (`CancellationToken.None`); the `Task.Run` "timeout" cannot actually stop a runaway FHIRPath evaluation — the thread-pool thread keeps burning after `TimeoutException` is thrown. `ConceptMapResolverService.Translate` additionally wraps the block in `catch (Exception) → return null`, silently converting terminology-service failures into "no translation found" — directly contradicting the `ErrorMode.Strict` the handler configures (`TransformResourceHandler.cs:110`) and the async version's deliberate rethrow.
**Recommendation**: Short term: remove the swallow-to-null in `Translate` (let it throw; Strict mode wants that) and document the timeout limitation. Long term: give `MappingEvaluator` async callbacks so tokens propagate.
**Effort**: M (short-term S)

### P1 — Empty catch blocks swallowing everything, including cancellation
**Location**: `Mcp/Tools/FhirOperations/SearchResourcesTool.cs:184-187`, `Mcp/Tools/FhirOperations/FhirPathQueryCapabilityStatementTool.cs:142-145`
**Issue**: `catch { /* If tenant resolution fails, use default R4 */ }` — bare catch, no logging, swallows `OperationCanceledException` and turns any store failure into a silent R4 fallback. Searching an R5 tenant whose config lookup transiently fails would build R4 search expressions against R5 data with no trace. CLAUDE.md is explicit: "empty catch blocks are bugs". The identical `ResolveFhirVersionAsync` helper is copy-pasted into both tools.
**Recommendation**: Extract one helper (on `TenantAwareMcpTool`), catch specific exceptions, log the fallback, and let cancellation propagate.
**Effort**: S

### P1 — Dead configuration knobs: appsettings values that nothing reads
**Location**: `Configuration/ExperimentalOptions.cs:75` (`Mcp.Transport`), `:91` (`Transform.TimeoutSeconds`), `:124-129` (`Summary.MaxResources`, `Summary.AllowedResourceTypes`); `Ignixa.Web/appsettings.json:211,221-222`
**Issue**: `Transform.TimeoutSeconds` (default 30, set to 30 in appsettings) is never read — the FHIRPath timeout is hardcoded to 5 seconds in `ExperimentalAutofacRegistration.cs:157`. `Summary.MaxResources` is never read — `IpsGeneratorService.cs:46` hardcodes `DefaultMaxIpsResources = 1000`. `Summary.AllowedResourceTypes` and `Mcp.Transport` are read by nothing. Operators tuning these values are being lied to.
**Recommendation**: Wire each option through (pass `IOptions<ExperimentalOptions>` where the hardcoded values live) or delete the properties and the appsettings entries.
**Effort**: S

### P1 — Registration-time config binding prevents test/host config overrides
**Location**: `Infrastructure/ExperimentalAutofacRegistration.cs:61-63`, `Infrastructure/ExperimentalServicesRegistration.cs:36-38`, `Ignixa.Api/Endpoints/Experimental/ExperimentalEndpointExtensions.cs`
**Issue**: Feature enablement is decided by `configuration.Get<ExperimentalOptions>()` executed during `ConfigureServices`/container build, in three separate places (services, Autofac, endpoints). Config sources added after host build, reloadable providers, and `WebApplicationFactory` overrides applied late don't take effect, and the three read points can disagree if configuration changes between them. This is the documented blocker on PR #277 (GraphQL off-by-default) — the E2E config-bind timing split.
**Recommendation**: Bind once via `IOptions<ExperimentalOptions>` and gate endpoints/handlers at request time (or a startup filter), not at registration time. At minimum, read the config exactly once and share the snapshot.
**Effort**: M

### P1 — Dead code: public `StructureDefinitionBasedStrategy` shadowed by a private nested duplicate
**Location**: `Ips/Strategy/StructureDefinitionBasedStrategy.cs:18` (dead), `Ips/Strategy/StructureDefinitionStrategyFactory.cs:336-384` (live private nested class of the same name)
**Issue**: Two classes named `StructureDefinitionBasedStrategy` exist. The factory's `new StructureDefinitionBasedStrategy(sections, bundleProfile)` at line 56 binds to its private nested copy; the public top-level class (different constructor: takes `StructureDefinitionJsonNode`) is referenced by nothing in src or test. It also carries two fields (`_compositionProfile`, `_compositionProfileUrl`) that are assigned and never read. Classic copy-then-abandon.
**Recommendation**: Delete `Strategy/StructureDefinitionBasedStrategy.cs`; if a public type is wanted, promote the nested one instead of keeping both.
**Effort**: S

### P1 — `SectionMetadataParser` duplicates ~200 lines of the factory and is production-dead
**Location**: `Ips/Metadata/SectionMetadataParser.cs:20-251` vs `Ips/Strategy/StructureDefinitionStrategyFactory.cs:126-334`
**Issue**: `ParseSections`/`ParseSection`/`ExtractLoincCode`/`ExtractTitle`/`DetermineCardinality`/`ExtractEntryProfiles`/`ExtractResourceTypeFromProfile` exist twice, near-identically. `SectionMetadataParser` is not registered in DI and is referenced only by its own unit tests (`SectionMetadataParserTests.cs`) — so the tests exercise the copy production doesn't use, while the factory's live copy is untested (same pattern as the Transform twins). ADR-2512 names `SectionMetadataParser` as the intended component.
**Recommendation**: Make `StructureDefinitionStrategyFactory` delegate to `SectionMetadataParser` (register it in DI) and delete the inline duplicate — this honors the ADR and fixes the coverage inversion.
**Effort**: S

### P1 — `StructureMapTransformFeature` is never registered: $transform absent from CapabilityStatement
**Location**: `Transform/StructureMapTransformFeature.cs:14`; registrations at `ExperimentalAutofacRegistration.cs:120-171` (no `IPackageFeature` registration for Transform) vs `:101-103` (GraphQL registers `GraphQlFeature`)
**Issue**: The GraphQL feature registers an `IPackageFeature` so `$graphql` is advertised in the CapabilityStatement; Transform has the equivalent class but never registers it (in either the live or dead copy). Consumers inspecting `/metadata` won't discover `$transform` even when it is enabled. The class is instantiated only by `OperationsSegmentTests`.
**Recommendation**: Register it inside `RegisterTransformHandlers()` (mirroring GraphQL), or delete it if non-advertisement is intentional.
**Effort**: S

### P1 — Half-finished IPS surface: identifier lookup always throws, dead TODO block
**Location**: `Ips/Generator/IpsGeneratorService.cs:112-130` (`GenerateIpsByIdentifierAsync` throws `NotSupportedException` after a `logger.LogWarning`), `Ips/Generator/IpsGeneratorHandler.cs:28-31,46-50` (validates and routes to it anyway), `:87-100` (13-line commented-out CapabilityStatement lookup block)
**Issue**: The handler accepts `PatientIdentifier`, validates it, splits system|value — then calls a method whose only behavior is to throw. The interface, endpoint parameter, and handler plumbing all advertise a capability that doesn't exist. The commented-out "Priority 2" block is dead planning residue.
**Recommendation**: Either implement identifier-based lookup (token search on `identifier`) or remove the parameter from `IpsGeneratorQuery`, the interface method, and the endpoint until it exists. Delete the commented block.
**Effort**: M (implement) / S (remove)

### P1 — Inconsistent tenant plumbing and duplicate dependency injection across MCP tools
**Location**: `Mcp/Tools/FhirOperations/GetResourceTool.cs:28-37` and `SearchResourcesTool.cs:37-51` (inject `IFhirRequestContextAccessor` twice under two parameter names; mutate `requestContext.TenantId` at `GetResourceTool.cs:72`, `SearchResourcesTool.cs:132`); `PatchResourceTool.cs:114` (passes `TenantId` in the command instead); `GetResourceHistoryTool.cs:90` (hardcodes `baseUrl = "https://localhost"`)
**Issue**: Three different tenant-propagation mechanisms across sibling tools: mutate the ambient request context, pass tenant in the command, or ignore it. Two tools take the same service as two constructor parameters (`fhirRequestContextAccessor` for the base, `contextAccessor` for the field) — an obvious generated-code artifact. Mutating the ambient `IFhirRequestContext.TenantId` from a tool is a side effect on shared per-request state that other components read.
**Recommendation**: Standardize on command-carried tenant IDs (the `PatchResourceCommand` pattern); remove the duplicate constructor parameters.
**Effort**: M

### P1 — Error-contract inconsistency across MCP tools; broad catches eat cancellation
**Location**: `Mcp/Tools/FhirOperations/PatchResourceTool.cs:82-106,140-148`, `PatchResourceFieldTool.cs:83-91,139-147` (return `Success=false` DTOs from `catch (Exception)`) vs `GetResourceTool.cs:61`, `SearchPackagesTool.cs:47`, `InstallPackageTool.cs:58` (throw)
**Issue**: Half the tools throw on bad input; the other half return result-object errors — an LLM client gets two different failure shapes from sibling tools. The patch tools' `catch (Exception)` also converts `OperationCanceledException` into `Success=false, ErrorMessage="Patch operation failed: The operation was canceled."`.
**Recommendation**: Pick one contract (result DTOs are reasonable for MCP), apply it uniformly, and add `catch (OperationCanceledException) { throw; }` before the broad catches (the GraphQL resolvers already model this correctly).
**Effort**: M

### P1 — DiagnosticTool: a phase-1 spike exposed as a production MCP tool
**Location**: `Mcp/Tools/DiagnosticTool.cs:13-15` ("Phase 1: Spike to validate MapGroup tenant parameter accessibility"), `Mcp/Tools/DiagnosticResult.cs`
**Issue**: A self-described spike that dumps HTTP route values, request path, and internal context items to any MCP client. Combined with the missing authorization (first P0), this is internal-state disclosure with zero remaining diagnostic purpose — the MapGroup question it validated is long settled.
**Recommendation**: Delete both files.
**Effort**: S

### P1 — GetResourceTool documentation promises parameters that don't exist
**Location**: `Mcp/Tools/FhirOperations/GetResourceTool.cs:40-44` vs signature `:45-55`; `:84-85` ("TODO: Phase 2 add filtering")
**Issue**: The `[Description]` — which is exactly what an LLM reads to call the tool — instructs `Use elements='id,field1,field2'` and `Use summary='true'`, but the method has no `elements` or `summary` parameters. Every model following the docs will produce invalid tool calls. A `TODO` in the body confirms the feature was never built.
**Recommendation**: Remove the claims from the description (or implement the parameters, as `SearchResourcesTool` did). No TODOs left behind.
**Effort**: S

### P1 — Test placement is split across three projects with no rule
**Location**: `test/Ignixa.Application.Experimental.Tests/` (Ips + options only, 4 files), `test/Ignixa.Application.Tests/Features/Experimental/GraphQl/` (10 files), `test/Ignixa.Application.Tests/Features/Transform/` (5 files targeting the dead Operations twins), `test/Ignixa.Api.Tests/Mcp/` (1 file)
**Issue**: A test project named for the never-created `Ignixa.Application.Experimental` library exists but holds only IPS tests; GraphQL tests live in the main test project; Transform tests target dead code; MCP tools (including both patch tools with the P0 value bug) have essentially no coverage. Nobody can answer "where do tests for an Experimental feature go?"
**Recommendation**: Consolidate all Experimental tests into `Ignixa.Application.Experimental.Tests` (or fold that project away and use `Features/Experimental/` inside the main test project — either, but one).
**Effort**: M

### P2 — One-type-per-file violations
**Location**: `Configuration/ExperimentalOptions.cs` (6 types), `Mcp/Tools/PackageManagement/SearchPackagesTool.cs:125-224` (5 types), `Mcp/Tools/PackageManagement/ListPackagesTool.cs:75-112` (3), `Ips/Generator/IpsGeneratorQuery.cs:17-42` (3), `Transform/MapRegistryCache.cs:28,341` (3), `Transform/FhirPathExpressionCache.cs:138` (2), `Ips/Api/Section.cs:52` (2), Terminology query files (2 each: query + result)
**Issue**: CLAUDE.md mandates one type per file. Query+Result pairs are arguably tolerable; a tool class trailing four DTO records is not.
**Recommendation**: Split at least the tool files and `ExperimentalOptions.cs`.
**Effort**: S

### P2 — Copyright-header archaeology marks authorship generations
**Location**: "Microsoft Corporation" headers on all of Mcp/, Transform/, `ExperimentalOptions.cs`, `ExperimentalServicesRegistration.cs`; "Ignixa Contributors" on GraphQl/, Ips/, `GraphQlExperimentalOptions.cs`, `ExperimentalAutofacRegistration.cs`; *no header at all* on all six Terminology files
**Issue**: Three header conventions in one folder. The Microsoft headers are copy-paste residue from the microsoft/fhir-server codebase and are simply wrong for this repository.
**Recommendation**: Normalize to the Ignixa header (a mechanical sweep; consider a repo-guard test).
**Effort**: S

### P2 — Mixed constructor styles and redundant null checks
**Location**: Old-style ctors with manual `?? throw new ArgumentNullException` throughout Mcp/ and Terminology (e.g. `McpAuthorizationService.cs:28-41`, `ExpandValueSetHandler.cs:23-29`) vs primary constructors in GraphQl/, Ips/, `InstallPackageTool.cs:22-34` (which does *both*: primary ctor + null-check-into-field boilerplate)
**Issue**: The codebase standard is primary constructors; DI-injected dependencies don't need null guards. The `InstallPackageTool` hybrid is pure noise.
**Recommendation**: Convert during any touch of these files; don't do a big-bang sweep.
**Effort**: S (opportunistic)

### P2 — Unused injected loggers in Terminology handlers
**Location**: `Terminology/Expand/ExpandValueSetHandler.cs:21,28`, `Terminology/Subsumes/SubsumesHandler.cs:15,22`, `Terminology/Translate/TranslateCodeHandler.cs:15,22`
**Issue**: `_logger` is injected, assigned, never used, in all three handlers (in both the live and dead copies).
**Recommendation**: Delete the parameter or log something useful.
**Effort**: S

### P2 — `TenantId` parameters accepted and ignored in all Terminology queries
**Location**: `Terminology/Expand/ExpandValueSetQuery.cs:11`, `Subsumes/SubsumesQuery.cs:11`, `Translate/TranslateCodeCommand.cs:11`, with identical "Future enhancement" remarks in each handler
**Issue**: Three copies of a doc comment explaining that a parameter does nothing. Honest, but a signature should not carry dead parameters across three operations.
**Recommendation**: Either thread tenant into `ITerminologyService` or drop the parameter until tenant-scoped terminology exists.
**Effort**: S

### P2 — Direct JSON navigation where FHIRPath is the stated convention
**Location**: `Ips/Strategy/DefaultIpsGenerationStrategy.cs:73-138` (`MutableNode["clinicalStatus"]` chains, `GetCodeFromCodeableConcept` taking `coding[0]` only), `Ips/Generator/IpsGeneratorHandler.cs:123-150` (`CountSections` walking `MutableNode` arrays)
**Issue**: CLAUDE.md explicitly prefers FHIRPath over `MutableNode` navigation. `GetCodeFromCodeableConcept` also only inspects the first coding — a clinicalStatus with a non-primary coding order would mis-classify.
**Recommendation**: Move status checks to cached FHIRPath (`element.IsTrue("clinicalStatus.coding.where(code='inactive' or code='resolved').exists()")` style) or at least scan all codings.
**Effort**: S

### P2 — `JsonDocument` instances stored in DTOs and never disposed
**Location**: `Mcp/Tools/FhirOperations/GetResourceTool.cs:86`, `SearchResourcesTool.cs:144`, `GetResourceHistoryTool.cs:107`; `Mcp/Dtos/ResourceDto.cs`, `ResourceEntryDto.cs`
**Issue**: `JsonDocument.Parse` rents pooled buffers returned only on `Dispose`; parking them in DTOs handed to the MCP serializer leaks pool arrays under load. `JsonElement` (clone) or raw `JsonNode` would be the right DTO currency.
**Recommendation**: Parse to `JsonElement` via `JsonSerializer.Deserialize<JsonElement>` (as GraphQL's `FieldResolver.ParseResourceBytes` already does) or dispose after cloning the root element.
**Effort**: S

### P2 — Broken indentation block in FhirTypeModule
**Location**: `GraphQl/Schema/FhirTypeModule.cs:373-388`
**Issue**: The `connectionField.Resolve` lambda body is indented one level too deep relative to its siblings — cosmetic, but it's the kind of artifact `dotnet format` should have caught.
**Recommendation**: Reformat.
**Effort**: S

### P2 — GraphQL still enabled by default despite the off-by-default decision
**Location**: `Configuration/GraphQlExperimentalOptions.cs:12` (`Enabled = true`)
**Issue**: The project decision (PR #277) is GraphQL off-by-default; it remains on because #277 is blocked on the config-bind timing split (see the registration-time binding finding above, which is the actual root cause to fix first).
**Recommendation**: Land the request-time gating fix, then flip the default with #277.
**Effort**: S (after the P1 config finding)

## Graduation / Deletion Candidates

| Item | Recommendation | Rationale |
|------|-----|-----|
| `Ignixa.Application.Operations/Features/Transform` + `Terminology` (13 files) | **Delete** (retarget tests first) | Byte-identical dead twins of the live Experimental copies; migration Phase 5 never finished |
| `Ips/Strategy/StructureDefinitionBasedStrategy.cs` (public class) | **Delete** | Never referenced; shadowed by the factory's private nested duplicate |
| `Ips/Metadata/SectionMetadataParser.cs` | **Keep + wire in** (or delete) | ADR-2512's named component; currently test-only while the factory carries an inline duplicate — make the factory use it, or delete it and its tests |
| `Mcp/Tools/DiagnosticTool.cs` + `DiagnosticResult.cs` | **Delete** | Self-described phase-1 spike; discloses internal HTTP state; purpose long served |
| `Transform/StructureMapTransformFeature.cs` | **Register or delete** | Dead in both copies; $transform silently missing from CapabilityStatement |
| GraphQl/ (33 files) | **Graduate** (after #277) | Highest-quality, best-tested subsystem in the folder; blocked only by the off-by-default config work |
| Terminology/ (6 files) | **Graduate** | Thin, stable wrappers over `ITerminologyService`; nothing experimental about them once the dead twins are removed |
| Transform/ (9 files) | **Keep-Experimental** | Sync-over-async pipeline, dead cache lifetime, unreached config — needs the P1 fixes before promotion |
| Ips/ (18 files) | **Keep-Experimental** | Broken strategy handoff and half-finished identifier lookup; the feature demonstrably doesn't do what its event handlers claim |
| Mcp/ (31 files) | **Keep-Experimental** | Must not graduate until authorization is actually enforced (P0) |
| `Mcp/Dtos/JobStatusDto.cs`, `JobSummaryDto.cs` | **Relocate** | Consumed by `Ignixa.Application.BackgroundOperations/JobManagement` — Experimental is not an isolation boundary while other projects import from it |

## Recommendations Summary

| Priority | Recommendation | Effort | Files affected |
|----------|---------------|--------|-----------------|
| P0 | Enforce MCP authorization: required dep in `TenantAwareMcpTool`, `Ensure*` calls in every tool, `RequireAuthorization` on `/mcp` | M | ~12 (base + 9 tools + endpoint) |
| P0 | Fix IPS strategy handoff — service must consult the registry | S | 2 |
| P0 | Fix patch value type mapping (decimal/JsonElement) + shared helper + tests | S | 2-3 |
| P0 | Delete 13 dead Operations Transform/Terminology files; retarget 5 test files | S | 18 |
| P1 | Fix or remove `MapRegistryCache` lifetime/invalidation contradiction | M | 3 |
| P1 | Remove swallow-to-null in `ConceptMapResolverService.Translate`; document timeout limits | S | 2 |
| P1 | Replace empty catches with logged, specific handling; dedupe `ResolveFhirVersionAsync` | S | 3 |
| P1 | Wire or delete dead config knobs (Transform timeout, Summary limits, Mcp transport) | S | 4 |
| P1 | Move Experimental gating from registration time to request time (unblocks #277) | M | 3-4 |
| P1 | Delete dead `StructureDefinitionBasedStrategy`; wire `SectionMetadataParser` into the factory | S | 3 |
| P1 | Register `StructureMapTransformFeature` (or delete) | S | 2 |
| P1 | Implement or remove IPS identifier lookup; delete commented TODO block | S-M | 3 |
| P1 | Standardize MCP tenant plumbing + error contract; remove duplicate ctor params; delete DiagnosticTool; fix GetResourceTool docs | M | ~10 |
| P1 | Consolidate Experimental tests into one project; add MCP tool coverage | M | test tree |
| P2 | One-type-per-file splits, header normalization, primary ctors, unused loggers, dead TenantId params, FHIRPath adoption in IPS, JsonDocument disposal, formatting, GraphQL default flip | S each | ~25 |
