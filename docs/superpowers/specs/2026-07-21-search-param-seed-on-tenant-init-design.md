# Seed Search Parameters on Tenant Factory Construction — Design

**Status:** Approved by user, 2026-07-21. Supersedes `docs/superpowers/specs/2026-07-21-e2e-fixture-seed-ordering-fix-design.md`, whose premise was invalidated by a failed implementation attempt.

## Background

This session's cache-race-fix plan (commit `7c495484`) closed a real concurrency bug in `SqlServerSearchIndexReferenceDataCache`. A follow-up investigation into why `Ignixa.Api.E2ETests` still showed 437/620 failures found the true root cause: `IgnixaApiFixture.SyncBaseSearchParametersAsync()` seeds `dbo.SearchParam` *after* something else has already eagerly warmed the SqlServer write-side cache against an empty database, permanently locking it empty (see the superseded design doc for the full mechanism).

A first fix attempt reordered calls entirely inside the E2E test fixture. It made things worse (21 failed/1 passed vs. a 20 failed/2 passed baseline, reproduced twice with full logging), because the real trigger point is not in the test fixture at all — it's in production startup code, and merely accessing the app's `Services` (needed to read search-parameter definitions) already requires that startup code to have run.

## Root Cause, Precisely

`SqlEntityFrameworkRepositoryFactory.CreateServiceFactory` (`src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlEntityFrameworkRepositoryFactory.cs:271-...`), called once per tenant on first access (cached forever after in `_factoryCache`), does this in order:

1. Deploy schema if the database is empty (`_schemaDeployer.DeployIfEmptyAsync`/`UpgradeIfNeededAsync`, lines 296-316).
2. Construct the EF-based **read-side** reference-data cache (`_multiTenantCache.GetOrCreateCacheForTenant`, line 328) — which eagerly and synchronously loads the entire search-parameter catalog from the database via its own `InitializeAsync()` call, as part of construction.
3. Construct the SqlServer **write-side** reference-data cache and eagerly warm it (lines 339-350, this session's Task 3 addition).

Nothing seeds `dbo.SearchParam` between step 1 and steps 2/3. For any tenant whose database was just freshly deployed — a real production tenant being onboarded for the first time, or an E2E test's scratch database — both caches warm to empty and (per this session's own completion-flag fix) never retry. This is not an E2E-only risk: `CreateServiceFactory` is the single construction path for every caller of `GetRepositoryAsync`/`GetSearchServiceAsync` (confirmed during this session's Task 3 review — all four factory entry points route through `GetOrCreateFactoryAsync`, which calls this method), so real tenant onboarding has the identical structural risk.

`Ignixa.Web/Program.cs`'s `InitializeDatabasesAsync` (called during host startup, before `app.RunAsync()`) calls `GetRepositoryAsync` for every active tenant — which is what `IgnixaApiFixture`'s `Client = CreateClient()` triggers, before `SyncBaseSearchParametersAsync()` ever gets a chance to run. That's why the first fix attempt, confined to the test fixture, was structurally unable to succeed.

## Fix

Insert search-parameter seeding into `CreateServiceFactory` itself, between step 2 and step 3 — the single construction path shared by every tenant, every environment, production and test alike:

```csharp
// Get tenant-specific cache instance (reused across all requests for this tenant)
var searchIndexCache = _multiTenantCache.GetOrCreateCacheForTenant(tenantId, dbContextOptions);

// Seed the search-parameter catalog for this tenant's FHIR version before either reference-data
// cache is trusted for reads. SyncSearchParametersToDatabase is idempotent per-URL (checks for an
// existing row before inserting) and updates searchIndexCache's own in-memory dictionary as it
// goes, so this both seeds the database AND leaves the read-side cache correctly warm -- one call
// covers both. Cheap on a restart of an already-seeded tenant (one SELECT, no writes). Without
// this, a freshly-deployed database's caches (this one and the SqlServer write-side cache
// constructed below) would warm to empty and never recover -- see
// docs/superpowers/specs/2026-07-21-search-param-seed-on-tenant-init-design.md.
var searchParamUrls = parameterManager.AllSearchParameters
    .Where(sp => sp.Url is not null)
    .Select(sp => sp.Url!.ToString())
    .Distinct()
    .ToList();
searchIndexCache.SyncSearchParametersToDatabase(searchParamUrls, parameterManager).GetAwaiter().GetResult();

// Tenant-scoped raw-ADO.NET reference data cache backing the SqlServer write path
// (SqlServerFhirRepository)...
```

`parameterManager` is already available at this point (constructed at line 325 via `GetOrCreateDefinitionManagers(fhirSpec, schemaProvider)`, derived from `tenantConfig.FhirVersion` — the correct FHIR version per tenant, not a hardcoded value). No new dependencies, no new fields, no signature changes to any public method.

**Idempotency, not a conditional gate:** `SyncSearchParametersToDatabase` (`SearchIndexReferenceDataCache.cs:620-...`) already fetches the existing `SearchParams` rows once and skips inserting any URL already present. Calling it unconditionally on every `CreateServiceFactory` invocation is correct and cheap — real work happens exactly once per tenant (its first-ever construction against an empty database); every later restart of a populated tenant costs one `SELECT`, no writes. No "only if empty" gate is needed at the call site.

**Test-fixture cleanup**: `IgnixaApiFixture.SyncBaseSearchParametersAsync()` (`test/Ignixa.Api.E2ETests/_Infrastructure/IgnixaApiFixture.cs:249-275`) and its call site in `InitializeAsync()` (lines 200-203) become fully redundant — by the time `Client = CreateClient()` returns, `CreateServiceFactory` has already seeded tenant 1's catalog correctly. Delete both (explicit user decision — YAGNI, and keeping dead test-only seeding logic around invites confusion about which code path actually does the seeding).

## Explicitly Out of Scope

- Any change to `SqlServerSearchIndexReferenceDataCache`, `SqlServerMergeRepository`, or any other file from this session's earlier cache-race-fix plan — that fix's own correctness (verified twice, by its own final review and re-review) is unaffected by this bug and unaffected by this fix.
- Conditionally gating the seed call on whether `dbo.SearchParam` is empty — the method's own per-URL idempotency already makes this unnecessary complexity.
- Any change to how `Program.cs`'s `InitializeDatabasesAsync` iterates tenants — the fix lives one layer lower, inside the shared factory construction path, so it doesn't need to touch `Program.cs` at all.

## Testing

1. Existing `SqlEntityFrameworkRepositoryFactory`-related unit/integration tests — confirm no regression, since this touches shared production tenant-construction code, not test-only code.
2. Targeted `SortTests` E2E re-run against a fresh scratch database. Two baselines to beat: the true pre-any-fix baseline (20 failed/2 passed/22 total) and the failed first attempt (21 failed/1 passed/22 total, worse than baseline) — this fix should substantially beat both.
3. Full E2E suite re-run (baseline 163 passed/437 failed/20 skipped/620 total) — the real acid test for whether this diagnosis is correct. Record exact, unrounded numbers.
4. Confirm `IgnixaApiFixture.cs` still compiles and the E2E suite's other setup behavior (fetching `/metadata`, `SearchTestHarness` construction) is unaffected by removing `SyncBaseSearchParametersAsync`.

## Process

This is production code shared by every tenant construction path (not test-infrastructure-only, unlike the superseded design) — same risk class as this session's earlier write-path fixes. Plan-level Fable review before execution, task-scoped review per task, final whole-branch review before considering this done.

## Files Touched

- `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlEntityFrameworkRepositoryFactory.cs` — the seeding insertion.
- `test/Ignixa.Api.E2ETests/_Infrastructure/IgnixaApiFixture.cs` — delete the now-redundant method and call site.
