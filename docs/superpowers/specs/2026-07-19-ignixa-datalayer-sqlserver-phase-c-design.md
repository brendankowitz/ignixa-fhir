# Ignixa.DataLayer.SqlServer — Phase C Design: Schema-Version Compatibility Layer

**Status:** Approved, ready for implementation planning.

**Parent design:** `docs/superpowers/specs/2026-07-18-ignixa-datalayer-sqlserver-design.md` §6 — read that first for the original framing (spatial vs. fhir-server's temporal problem, per-tenant `SchemaVersion` table, expand/contract discipline). This document supersedes §6's specifics where Phase B's actual shipped implementation forced real design decisions §6 couldn't have anticipated.

**Branch:** continues directly on `worktree-ignixa-datalayer-sqlserver` (Phase A + Phase B's branch — not merged anywhere, pushed standalone to origin). Phase C does not get its own branch, matching the pattern already established across two prior phases.

## 1. Scope

Phase A and Phase B (both complete) built: a tenant-scoped raw-ADO.NET execution layer (`ISqlExecutionService`); a complete SQL Database Project (`Ignixa.DataLayer.SqlServer.Database`, 148 objects) as the schema source of truth; `SchemaDeployer`, which deploys that project's `.dacpac` to **brand-new, empty** tenant databases only (`DacServices.Deploy(..., upgradeExisting: true, ...)`, safety enforced solely by an `IsDatabaseEmptyAsync` check run immediately before — this was itself a real, empirically-discovered correction mid-Phase-B, since `upgradeExisting` doesn't actually distinguish "empty" from "populated," only "exists" from "doesn't").

Phase C builds the other half `SchemaDeployer` explicitly doesn't handle: bringing an **existing, already-populated** tenant database's schema up to date, safely, plus the version-tracking and version-gating primitives that make "safely" meaningful across a rolling multi-instance deployment.

**Ground truth from Phase B's actual shipped reality** (concrete constraints this design reconciles with, not abstractions):
- `Script.PostDeployment.sql`'s partition-splitting `WHILE` loop (770 boundaries) and its `ResourceChangeType` seed `INSERT`s are not idempotent. A real re-publish against an already-populated database re-executes the DDL portion successfully, then fails loudly (`SQL72014`, duplicate key) on the seed inserts. This must be fixed before any auto-upgrade path exists (§4).
- Every fresh `DeployReport`, even in a pure self-consistency comparison, contains known-benign noise: unnamed default-constraint canonicalization (Categories B and E), a hex-literal check-constraint canonicalization (Category C), and a partition-function/scheme rebuild cascade into `ResourceChangeData` plus ~4 dependent stored-procedure refreshes (Category D). None of this is destructive; all of it reappears on every comparison regardless of real schema drift.
- The SSDT project has 12 real FK constraints (new in Phase B — `97.sql`'s legacy tables have none), including one self-referencing FK on `TermConcept`.
- No `SchemaVersion` table or version-tracking mechanism exists anywhere yet.

## 2. What "schema version" means, given how Phase B actually built deployment

fhir-server classifies expand/contract per discrete numbered migration script (`91.sql`, `92.sql`, ...), each independently authored and reviewed. Phase B deliberately did not build that — it built one current-state SSDT project deployed via DacFx's diff engine (`SqlPackage`/`DacServices` compare the dacpac's *declared* state against the target's *live* state and compute one diff, however far behind the target is). There is no natural per-version boundary to classify.

**Decision: classify the single computed diff, not per-version snapshots.** No stored migration scripts, no versioned dacpac history. `SchemaVersion` becomes a simple, manually-bumped integer — a version constant baked into the SSDT project at build time (e.g. an extended property or a small version-manifest file), incremented by whoever authors a schema change, each bump tagged `expand` or `contract` in that same manifest. Each tenant's `SchemaVersion` table row stores the integer version it was last successfully brought to. The app's compiled-in window is `CurrentVersion` (what this build's dacpac represents — there is only ever one "latest," since there's no future state to look ahead to) and `MinSupportedReadVersion` (how far back this build tolerates reading an un-upgraded tenant — the input to Phase D/E's future version-gated code, §5).

On connect, if a tenant's stored version is behind `CurrentVersion`: generate one `DeployReport` between the tenant's live state and the current dacpac, classify it (§3), and either auto-apply + stamp the new version, or refuse and point at the operator path (§4).

This trades fhir-server's finer-grained "step through versions one at a time" model for something coarser but much simpler to build on top of what Phase B already shipped: one contract-phase change anywhere in the accumulated diff blocks the *entire* diff from auto-applying, even if most of it is safe expand-only content. Given contract phases are meant to be rare, deliberate cleanup steps (not routine), this coarseness is an acceptable, explicit trade — not an oversight.

## 3. Classifying a diff as auto-safe

A naive "any `Drop` operation blocks auto-upgrade" rule would block *every* upgrade forever — Categories B, C, D, and E from Phase B's own findings all present as `Drop`+`Create` operation pairs on every `DeployReport` run, and are already proven benign (self-consistency-tested, independently re-verified by multiple task reviewers across Phase B).

The classifier maintains an explicit allow-list of expected-benign operation patterns, seeded directly from Phase B's own documented findings:
- Any unnamed default-constraint drop+recreate (Category B/E — `CURRENT_TIMESTAMP`/`CAST(...)` canonicalization noise).
- The `CH_Resource_RawResource_Length` check-constraint drop+recreate (Category C — hex-literal canonicalization).
- `PartitionFunction_ResourceChangeData_Timestamp`/`PartitionScheme_ResourceChangeData_Timestamp` drop+recreate, `ResourceChangeData` unbind+rebuild, and the refresh of its known dependent procedures (Category D).

Anything in a `DeployReport`'s operation list that does **not** match the allow-list — any `Drop` of a table/column/index/FK not on the list, any data-narrowing `Alter`, any unexpected `TableRebuild` — marks the whole diff as not-auto-safe. This allow-list is a concrete, maintained artifact this phase produces (a real file/data structure, not a vague heuristic), and is expected to need small additions over time as new benign DacFx comparison quirks are discovered — exactly the same discovery process Phase B went through for Categories B through E.

## 4. Fixing the two blockers Phase B left for this phase

Before any auto-upgrade path can safely run against a real tenant database, two things Phase B explicitly deferred must be fixed:

- **`Script.PostDeployment.sql`'s seed `INSERT`s become idempotent** (`IF NOT EXISTS`/`MERGE` guards around the `ResourceChangeType` seed rows) — otherwise every upgrade after the first fails loudly on a duplicate-key error, which the classifier would have no way to distinguish from a real problem.
- **The partition-splitting loop's behavior on re-run is made explicit and safe** — since Category D's rebuild cascade is already proven benign (no data loss, confirmed via Phase B's self-consistency testing), the fix here is ensuring the post-deployment script doesn't re-run the full 770-iteration split loop redundantly on every publish (wasted work, not a correctness bug, but worth avoiding on a live database) — likely an idempotency guard checking existing boundary count before looping, mirroring the reasoning already used elsewhere in this project for avoiding redundant work.

## 5. The version-gating primitive (mechanism only, no real gate yet)

EF Core still owns 100% of actual FHIR read/write traffic — nothing in `Ignixa.DataLayer.SqlServer` varies its SQL shape by schema version yet, since Phase D/E (write/read-path migration) haven't happened. Per your explicit choice, Phase C builds the mechanism anyway, so Phase D/E plug into something already proven rather than inventing it under deadline:

- `ISchemaVersionResolver` (or equivalent), exposing `Task<int> GetCurrentVersionAsync(int tenantId, CancellationToken cancellationToken)` — reads the tenant's `SchemaVersion` row.
- A `SchemaVersionConstants`-style static class for declaring named per-feature minimum-version requirements, mirroring fhir-server's ~30-constant pattern structurally, but starting empty (or with one demonstrative constant) since there is no real feature to gate yet.

This ships with a demonstrative test proving the resolver and a constant-based check compose correctly — not a fabricated feature-gate wired into production code paths.

## 6. Operator path for blocked upgrades

A tenant whose diff is classified not-auto-safe needs a way to ever become unblocked — this is in scope for Phase C, not deferred, since without it a blocked tenant has no path forward at all. A minimal CLI command or admin endpoint: generate the `DeployReport` for that tenant, display the diff for human review, and on explicit confirmation, run `Deploy` (or `sqlpackage /Action:Publish`) against that tenant's database. This is the concrete form "explicit operator action" takes — matching the "always generate-then-review, never auto-publish" rule for anything beyond a provably-safe auto-upgrade.

## 7. Explicitly out of scope

- Any real version-dependent read/write behavior — Phase D/E's job, once version-varying code paths actually exist.
- Cross-instance locking to prevent two app instances racing to auto-upgrade the same tenant concurrently. SQL Server's own DDL concurrency will serialize or fail one attempt loudly (not corrupt data), matching the same risk-acceptance precedent already carried forward from Phase B's empty-case `SchemaDeployer` TOCTOU finding (accepted, not fixed, not a regression).
- Historical backfill/import of fhir-server's own schema-version history — irrelevant, Ignixa's versioning starts fresh from Phase C.
- Any change to `SchemaDeployer`'s existing empty-database path (§Phase B) — Phase C only adds the existing-database case alongside it.

## 8. Testing

- Unit tests for the destructive-operation classifier against canned `DeployReport` XML fixtures: each known-benign Category B/C/D/E pattern → classified safe; a synthetic genuinely-destructive operation (a dropped column, a narrowed type) → classified unsafe.
- An integration test exercising a real "N versions behind" upgrade: build an *older* commit's `.sqlproj` (e.g. Phase A's pre-decomposition state, or any earlier tagged point in this branch's own history) as the "old" dacpac, deploy it to a test database, then run the Phase C upgrade path against the current dacpac — real old-vs-new content, sourced from git history that already exists, without requiring any new production-facing snapshot mechanism.
- An integration test proving auto-upgrade correctly refuses on a genuinely destructive diff (a scratch dacpac copy with an injected destructive change) and surfaces the operator-path remediation clearly.
- An integration test for the post-deployment script's idempotency fix (§4) — running it twice against the same database succeeds both times, no duplicate-key failure.
