# Task 2 — Exact LastN Code Graph Maintenance Report

## Implementation

- Added `dbo.MaintainLastNCodeGroups @Mode varchar(8), @Resources
  dbo.LastNResourceScopeList READONLY` with the exact `Remove` and `Add` modes
  and error `50400` for every other value.
- Materialized distinct current, non-deleted coding identities from
  `TokenSearchParam`. Identity hashes use SHA-256 over a binary serialization
  with null markers and length prefixes. Every hash lookup also applies
  scope plus full null-safe, case-sensitive equality for `SystemId`, `Code`,
  and `CodeOverflow`.
- Kept hash collisions safe by selecting the matching full identity rather
  than the first scoped hash row. New identities start as self-labeled and
  orphan identities are retained.
- Added distinct observation membership and unordered edge maintenance.
  Repeated `Add` calls do not double-count existing observation pairs.
  Batch removal aggregates one decrement per observation pair, rejects
  missing or insufficient support with `50401`, deletes exhausted edges, and
  leaves already-removed observations unchanged.
- Added text-only grouping from current `TokenText` rows under
  `Latin1_General_100_CS_AS`. Coding membership takes precedence over text.
  Multiple case-distinct text values for one observation fail atomically with
  `50402`; an observation with neither coding nor text has no group.
- Added localized component repair. It seeds the repair set from saved old
  labels, removed endpoints, desired identities, and their current labels;
  expands the affected labeled components; propagates the minimum identity in
  both edge directions; updates identity labels; and repairs every coded
  observation group that references an affected identity.
- The procedure owns a transaction when called directly and uses a savepoint
  when called inside the transaction-owning wrappers delivered by later
  tasks. Errors roll back its own work without committing an outer
  transaction.

## Required Design Decision

- **Required design:** Decrement support and delete zero-support edges.
- **Cost:** `CH_LastNCodeEdge_Support` rejects a transient
  `SupportCount = 0`, so the illustrative update-then-delete sequence cannot
  execute against the deployed Task 1 schema.
- **Simpler design:** Remove the positive-support check and permit transient
  zero rows.
- **Outcome:** Preserved the approved schema invariant. The procedure validates
  the full batch decrement, directly deletes edges whose support is exhausted,
  and decrements only edges that remain positive. The observable result is
  identical without violating the table constraint.

## TDD Evidence

1. Added 13 live behavior tests before the procedure existed. The exact
   focused command failed 13/13 with SQL Server error
   `Could not find stored procedure 'dbo.MaintainLastNCodeGroups'`.
2. Added the minimum complete procedure and reran the focused live suite:
   13/13 passed.
3. Added a batch-removal test after identifying that collapsing equal edges
   across resources would under-decrement support. It failed with one edge
   remaining, then passed after retaining the observation key and aggregating
   the decrement count.
4. Added a current-resource guard with the current/history/deleted predicates
   removed. It failed because all three resources materialized, then passed
   after restoring both predicates.
5. Added an idempotent-removal guard against a deliberate missing-membership
   rejection. It failed with `50401`, then passed after restoring no-op removal
   semantics.
6. Mutated `SupportToRemove` from `COUNT(*)` to `COUNT(*) + 1`. The required
   one-removal support test failed, proving the support-count assertion catches
   an over-decrement. The mutation was reverted.
7. Removed `DISTINCT` from `#removedPairs` exactly as requested. The specified
   one-removal test still passed because `#oldMembership` has a unique primary
   key and the self-join uses `LeftCodeIdentityId < RightCodeIdentityId`; each
   observation therefore already emits one row per unordered pair. The
   `DISTINCT` is retained to match the brief, and the executable count mutation
   above proves the intended behavior.

## Verification

| Command | Result |
|---|---|
| Required focused live suite before implementation | Failed 13/13 with the expected missing-procedure error |
| Focused live suite after initial implementation | Passed 13/13 |
| Batch-removal test before count fix | Failed: expected zero edges, found one |
| Batch-removal test after count fix | Passed 1/1 |
| Required `DISTINCT` removal mutation | Survived 1/1 because `DISTINCT` is redundant under the source keys and ordered self-join |
| `SupportToRemove = COUNT(*) + 1` mutation | Killed by `GivenTwoObservationsSupportingOneEdge_WhenOneIsRemoved_ThenSupportRemainsOne` |
| Current-resource predicate mutation | Killed by `GivenCurrentHistoricalAndDeletedResources_WhenContributionsAreAdded_ThenOnlyCurrentDataIsStored` |
| Idempotent-removal mutation | Killed by `GivenAContributionWasAlreadyRemoved_WhenRemoveRunsAgain_ThenTheOperationIsIdempotent` |
| Final required focused live suite | Passed 16/16 on SQL Server 2022, 0 failed, 0 skipped |
| `LastNSchemaDeploymentTests` | Passed 1/1 |
| SQL database project build | Succeeded; existing unresolved-reference warnings remain |
| `git diff --check` | Passed |

The live tests used the isolated SQL Server 2022 instance on
`localhost,1434`. Every test deployed a fresh database and disposed it after
the assertion.

## Self-Review

- Compared the procedure and tests line by line with `task-2-brief.md`,
  `global-constraints.md`, and the preflight rulings in `progress.md`.
- Verified exact error numbers, mode spelling, procedure signature, case
  sensitivity, null-safe equality, overflow handling, text precedence, and
  current-resource filtering.
- Verified each edge contribution is counted per observation, including
  multi-resource batches, while duplicate coding rows cannot duplicate
  memberships or edges.
- Verified component propagation is bidirectional, monotonic, and terminates
  only when no label can decrease. Component labels and coded groups both use
  the minimum identity id.
- Verified removal validates all decrements before deleting groups,
  memberships, or edges, and both `50401` and `50402` paths roll back partial
  work.
- Verified the procedure does not modify any existing procedure, TVP, EF
  path, writer, or production route.
- Verified the final diff contains only the required procedure, integration
  tests, and this report.

## Weakest Link and Failure Semantics

The weakest component is SQL Server lock scheduling during concurrent
same-scope identity creation. This procedure uses transaction-scoped
`UPDLOCK, HOLDLOCK` range protection and retries the exact lookup after a
duplicate-key race; later wrappers add ordered scope application locks.
Unexpected SQL errors remain visible and roll back the owned transaction or
savepoint. No best-effort or success-shaped fallback exists.

## Concerns

The brief's prescribed `DISTINCT` mutation is not killable by the prescribed
test because the deployed membership key and ordered self-join make that
`DISTINCT` semantically redundant. A non-redundant support-count mutation was
killed instead. The SQL database project continues to emit the pre-existing
unresolved-reference warnings recorded by Task 1.

---

## Review Round 1 Fix

### Findings Addressed

1. Added `UPDLOCK, HOLDLOCK` to the update-first `LastNCodeEdge` probe. An
   absent edge is now protected by a transaction-duration key-range lock before
   the insert-if-absent statement. This makes edge support serialization local
   to the edge algorithm instead of relying on the identity lookup's current
   locking behavior.
2. Added public-procedure behavioral coverage for repeated `Add`, merging two
   existing components and repairing all prior group rows, splitting an
   existing component and repairing each surviving group's minimum label,
   concurrent same-edge additions over independent SQL connections, outer
   transaction rollback, and failure rollback to the procedure savepoint while
   leaving the outer transaction committable.
3. Added `LastNTestDatabase.OpenConnectionAsync` so concurrency tests use the
   original credential-bearing connection string. `SqlConnection.ConnectionString`
   intentionally omits the password after opening and therefore cannot safely
   create independent test connections.

### Root Cause and Failure Semantics

The edge update previously used no locking hints. When the target key was
absent, the update retained no serializable range protection for the subsequent
insert-if-absent decision. The narrow fix applies the same transaction-owned
`UPDLOCK, HOLDLOCK` discipline already used by the insert probe. Calls that own
their transaction retain the lock through commit; calls inside a wrapper retain
it in the outer transaction. The procedure's existing savepoint catch path
continues to roll back only its own work when the outer transaction remains
committable.

The weakest component remains SQL Server lock scheduling for competing writes
to one scope. Later wrappers add ordered scope application locks; this primitive
now also protects its edge update/insert invariant independently.

### TDD and Mutation Evidence

1. Added the six review-requested live SQL tests before changing production SQL.
   Five existing graph/transaction behaviors passed immediately. The
   independent-connection test first exposed a test-fixture defect (`Login
   failed for user 'sa'`) because it reused the sanitized opened connection
   string; adding `OpenConnectionAsync` corrected the fixture without changing
   production behavior.
2. The concurrent public-procedure test passed on the prior procedure because
   current identity resolution already serializes same-identity writers with
   transaction-owned update locks. The review finding is nevertheless valid as
   a local edge-algorithm invariant: correctness must not depend on an upstream
   lock that later refactoring could remove.
3. Added the edge range lock as the only production change. The six new live
   tests then passed.
4. Mutated the edge update to omit `SupportToAdd`. The independent-connection
   test failed with final support `1` instead of `2`. Restored the increment and
   reran the complete focused suite green. This proves the concurrency
   assertion observes the exact final support count rather than task completion
   or mock behavior.

### Verification

| Command | Result |
|---|---|
| New review tests against live SQL Server | Passed 6/6 after correcting the independent-connection fixture |
| Concurrent support-count mutation | Failed as expected: support `1`, expected `2` |
| SQL database project build | Succeeded, 0 errors; 325 pre-existing unresolved-reference/casing warnings |
| Final focused live SQL Server suite | Passed 22/22, 0 failed, 0 skipped |
| `git diff --check` | Passed |

The live suite used SQL Server at `localhost,1433`; every test deployed and
disposed an isolated database. The final focused run took 9 minutes 59 seconds.

### Self-Review

- Re-read the original brief, approved investigation, implementation plan,
  global constraints, prior report, review package, and round-1 findings.
- Verified the production diff is one lock-hint change and does not modify a
  base procedure, TVP, EF path, writer, route, or schema.
- Verified all added tests call `dbo.MaintainLastNCodeGroups` and assert stored
  graph/group state rather than mocks or SQL source text.
- Verified the merge test starts with two materialized components and checks
  every old and bridge group row after repair.
- Verified the split test checks both minimum component labels and prior group
  rows on each surviving side.
- Verified the concurrency test uses separate physical connections and checks
  exact support `2`.
- Verified outer rollback removes all maintenance rows, while the error-path
  test proves `50402` rolls back to the savepoint and permits an unrelated outer
  write to commit.
- Verified transaction ownership, cancellation propagation in new async test
  helpers, and deterministic resource cleanup.

### Concerns

- The requested concurrency test does not fail against the prior production
  procedure because same-identity writers are already serialized during exact
  identity lookup. The explicit edge lock removes that hidden dependency, but
  the test is a concurrency guard rather than a base-commit regression. The
  support-count mutation is killed.
- The SQL database build still emits the 325 pre-existing warnings recorded in
  earlier Task 2 evidence.
