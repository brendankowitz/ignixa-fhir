# ADR 2611: Delta Engine for the Native Fabric Silver Writer

## Status

Proposed. The Phase 2 MERGE sink implements this decision for the local filesystem. It awaits owner
ratification, then `/accept-adr`. **It is not production-ready at scale until B3 is resolved.**

## Date

2026-11-24

## Context

Issue #550 asks Ignixa to write Delta tables natively, without Spark at runtime, whose schema, table
properties and row semantics match the Silver layer of Healthcare data solutions (HDS) in Microsoft Fabric
v1.4.0, for FHIR R4 `Patient` and `Observation`. Phase 1 is the native writer and Phase 2 is a local-filesystem
MERGE sink. OneLake (Phase 3) and CLI/server wiring (Phase 4) are out of scope here.

**Table shape the engine must honour**, read from the HDS-deployed version-0 commits:

- protocol reader 1 / writer 4, with no table features, and no partition columns;
- configuration with exactly two keys, `delta.enableChangeDataFeed` (HDS value `True`) and
  `delta.parquet.vorder.enabled`;
- a schemaString that fixes field names, order, types and nullability. Patient has 51 top-level fields and
  Observation 89. `msftCreatedDatetime` is the last column, and struct and array columns have `*_string`
  companions. Column order comes from the contract and is never re-derived;
- HDS Silver ids are lowercase hex SHA-256 values (source-derived, see the normalization investigation), so keys
  are uniformly spread.

**Evidence limits:**

- **No HDS-captured row oracle exists.** Only schema parity is checkable. This ADR makes no row-parity claim.
- **The HDS package is licensed.** Contracts are loaded at runtime from a caller-supplied local path. No HDS
  schema text, sample data or code is committed or redistributed.
- **The Fabric runtime was never run.** Local Apache Spark 3.5.6 with Delta 3.2.1 (Docker) is the only Spark
  reader exercised.

**Evidence labels** used below: **Measured** (run locally on Windows x64 unless stated), **Inferred** (from
source reading, not profiled), **Paper** (documentation or source only), **Estimate** (judgment),
**Unverified**, and **Policy** (an Ignixa choice where HDS behaviour is undefined).

## Options Considered

| Option | Assessment | Evidence |
|---|---|---|
| **(a)** DeltaLake.Net 0.33.3 alone (binding over delta-rs `deltalake-core` 0.32.1) | Not sufficient. Table creation rejects `delta.parquet.vorder.enabled`, and CDF is silently off when the value is `True`. | Measured |
| **(b)** Managed Delta engine over Parquet.Net | Not chosen. About 27–45 engineer-days beyond the managed v0 writer, covering log replay, statistics, MERGE as file rewrite, CDF, optimistic concurrency, conditional put and checkpoints. It is the largest correctness surface, and it pays the same copy-on-write cost under B3. | Estimate |
| **(c)** Hybrid: Ignixa-owned v0, DeltaLake.Net for data | **Chosen.** Every local correctness experiment passed: creation, nested types, MERGE rules, CDF with `true`, DuckDB and raw-log interop, concurrent writers, commit-window fault injection and checkpoints. The Spark read is partial (B4) and scale is bounded (B3). | Measured (local) |
| **(d)** EngineeredWood 0.4.0 (pure managed, Apache-2.0, pre-1.0) | Not run. It claims MERGE, CDF and checkpoints. Its deletion vectors need protocol 3/7, so they cannot help on 1/4 tables. It is the cheapest candidate to spike if B3 rules out delta-rs. | Paper |

## Decision

### 1. Hybrid engine behind an Ignixa seam

- **Ignixa owns version 0.** A managed writer emits the protocol and metaData actions and publishes the commit
  create-if-absent. The schemaString is copied byte-exact from the contract. Any future metaData change is also
  Ignixa-written.
- **DeltaLake.Net 0.33.3 owns everything after version 0:** data files, the conditional MERGE, CDF emission,
  conflict detection, later commits and checkpoints. Measured: no later commit rewrote protocol or metaData,
  through v105 including a checkpoint.
- **The seam.** The package's public surface (table create/validate, the batch writer, options, results and
  typed errors) exposes no DeltaLake.Net type. Swapping to (b), (d) or a fixed delta-rs replaces package
  internals. The contract tests verify through an independent Delta-log and Parquet reader, not through the
  engine.
- **Isolation.** The engine lives only in the alpha package `Ignixa.SqlOnFhir.Fabric.Delta`. Repository guard
  tests enforce two rules:
  - only that project references DeltaLake.Net;
  - no shippable project (Web, CLI, Application, API, the Parquet writers, tools) references that project.

### 2. Ruling B1: the CDF property value

- **Tables Ignixa creates write `delta.enableChangeDataFeed = "true"`.** This is a documented deviation from
  HDS's `"True"`.
- **Why (Measured).**
  - delta-rs 0.32.1 reads `"True"` as CDF-off and writes no `cdc` actions.
  - Spark treats the same table as CDF-on. It finds no change files, so it reports every update as delete+insert,
    and it reports rows that a merge only copied as phantom delete+insert pairs.
  - With `"true"`, the raw log, the kernel CDF read and Spark agree, and copied rows are absent.

  With this engine, the exact HDS value and a working CDF cannot both hold.
- **Existing tables whose value is not lowercase `"true"` fail closed** at validation. CDF is never lost silently.
- **Follow-up (Phase 3, opt-in):** a normalizing Ignixa-written metaData commit for HDS-deployed tables.

### 3. Ruling B2: licence and native footprint

- **Licence.** The DeltaLake.Net nuspec declares MIT; the upstream repository's LICENSE and file headers declare
  Apache-2.0. Ignixa treats it as Apache-2.0, the stricter of the two. The repository LICENSE does not change,
  because it lists source included in or derived into this repository, and this is a package dependency.
- **Footprint (Measured from the package).** Natives ship for win-x64, linux-x64, linux-arm64 (glibc),
  osx-x64 and osx-arm64, at 113–142 MiB per RID extracted. The package is about 233 MB. There is no musl and no
  win-arm64 build. Only win-x64 was executed.
- This footprint is why the isolation rule in section 1 exists.

### 4. B3: MERGE cost with SHA-256 ids (production-scale blocker)

**Measured in the spike.** The workload was synthetic Observation rows of about 220 bytes of Parquet each: a
50,000-row table of 10.4 MiB, with 5,000-row merges. Memory is in MiB/GiB, because the harness divides by 2^20.

- **Default engine settings:** 9,317–10,736 MiB peak working set and 31–89 s per merge. Clustered ids at the same
  scale: 1,389 MiB and about 2 s.
- **No pruning:** every merge scans every file (0 skipped), and scattered updates rewrite the whole table.
- **Native memory:** the memory is native, and the .NET GC heap stayed at or below 93 MiB. A 50-row source costs
  about the same as a 5,000-row source.
- **Bounded settings** (peak private bytes):
  - 1 target partition: 3,537 MiB;
  - 1 partition plus batch size 1024: 2,766 MiB in 18 s;
  - 100,000 rows at 1 partition and the default batch size: 5,358 MiB.
- **What did not work:**
  - a 1 GiB spill pool makes the merge fail rather than fit;
  - splitting a source into several merges is slower;
  - at default settings a 100-file table ran out of memory.

**Root cause.**

- **Inferred** from the delta-rs 0.32.1 merge source and DataFusion's error text, not profiled:
  - The early file filter is one min/max range over the source keys. SHA-256 keys span the whole key space, so
    nothing is pruned.
  - The whole decoded target then passes through a full outer join and two `row_number()` windows. Each window
    is hash-repartitioned and sorted per partition; DataFusion names `ExternalSorter` and `RepartitionExec`.
  - So peak memory follows target rows and partition count.
- **Inherent to any engine on protocol 1/4,** which has no deletion vectors: copy-on-write rewrites every touched
  file, and random keys touch nearly every file.
- **Upstream:** delta-io/delta-rs#2802 and #3786. No fix that bounds this was found in 0.32.x.

### 5. B3 as implemented in the Phase 2 MERGE sink

**Fail-closed ceilings.** All are configurable at the caller's risk, and all are checked before anything is
written:

| Ceiling | Default | Checked | When exceeded or unknown |
|---|---:|---|---|
| `MaxSourceResourcesPerTable` | 50,000 | Before projection | Typed limit error; nothing projected or created |
| `MaxTargetRows` | 50,000 | Existing table, `COUNT(*)` fully drained | Typed limit error. An unknown count fails closed |
| `MaxTargetFiles` | 100 | Existing table, live data-file count | Typed limit error telling the operator to compact (OPTIMIZE) the table. An unknown count fails closed |

The target ceilings are advisory under concurrent writers, because they are read before the MERGE. A table can
reach about `MaxTargetRows + MaxSourceResourcesPerTable` rows; after that it refuses writes until the caller
raises the limit.

**Engine discipline:**

- MERGE only. There is no append route.
- `DataFusionExecutionTargetPartitions = 1` and `DataFusionExecutionBatchSize = 1024`, as internal constants. No
  spill size or memory pool is set.
- One MERGE at a time per process, through a process-wide gate held around each table's merge and its retries.
- One merge per table per batch; a source is never split.
- One engine per batch call, then disposed. A fresh table handle per attempt. Every binding query stream is fully
  drained.

**Measured memory (Phase 2).** An opt-in test, gated by an environment variable, ran on a single Windows host
(28 logical cores, 63.7 GiB RAM).

- **Budget:** 4.5 GiB (4,831,838,208 bytes). The test asserts two metrics against it:
  - **peak private bytes**, the Windows peak commit charge (`Process.PeakPagedMemorySize64`);
  - **peak working set** (`Process.PeakWorkingSet64`), as a second assertion.
- **Non-Windows:** the cases skip, because no equivalent private-bytes metric is defined there.
- **Workload:** each case merges 5,000 rows (half updates, half inserts) into a 50,000-row table, with
  1 partition and batch size 1024.
- **Rows:** realistic width, with populated `*_string` companions and nested structs.

| Case | Bytes per row (Parquet) | Merge wall | Lifetime peak private bytes | Lifetime peak working set | Merge-window sample, private / working set | Test wall, incl. seeding |
|---|---:|---:|---:|---:|---:|---:|
| Observation, 10 files | 846 | 9.9 s | 2,354 MiB (2.30 GiB) | 2,382 MiB (2.33 GiB) | 2,320 / 2,348 MiB | 32 s |
| Observation, 100 files | 1,309 | 10.4 s | 2,431 MiB (2.37 GiB) | 2,467 MiB (2.41 GiB) | 2,427 / 2,463 MiB | 35 s |
| Patient, 10 files | 525 | 7.0 s | 1,733 MiB (1.69 GiB) | 1,777 MiB (1.74 GiB) | 1,698 / 1,743 MiB | 24 s |
| Patient, 100 files | 787 | 7.7 s | 1,772 MiB (1.73 GiB) | 1,814 MiB (1.77 GiB) | 1,772 / 1,814 MiB | 28 s |

How to read the table:

- **Lifetime columns** are the process lifetime peak, including seeding.
- **Seeding** uses the writer's engine settings, so seeding is unlikely to set the peak; the merge-window sample is
  within 2% of the lifetime peak in every case.
- **Test wall** includes seeding; merge wall is the merge alone.
- **Comparability:** these are single-host Windows measurements of a different workload and harness from the spike
  probe in section 4. The two sets of figures are not the same measurement and are not directly comparable.

All four cases pass both assertions; the worst case, Observation in 100 files, leaves about 47% headroom. The
defaults stay at 50,000 rows and 100 files. Figures are in MiB/GiB (2^20 bytes).

**Measured envelope versus what the ceilings permit:**

- **Measured:** a 50,000-row target plus a 5,000-row source, up to 100 files.
- **Permitted, not measured:** a source of up to 50,000 resources per table, and a result of up to about 100,000
  rows (a target at the row ceiling plus a full source).

**Compaction gates any sustained Phase 2 use.**

- **MERGE only never compacts.** An insert adds a file and removes none, and a small update rewrites only the
  files it touches. With small insert-heavy batches, the 100-file ceiling is reached after about 90 commits, long
  before 50,000 rows.
- **The binding offers compaction tools, all untested.** It exposes `ITable.OptimizeAsync(OptimizeOptions, ct)`
  (bin-packing or Z-order, with a target file size), plus `VacuumAsync` and `CheckpointAsync`. None is tested:
  - whether they keep protocol 1/4, the configuration and the schemaString unchanged is unknown;
  - their memory on SHA-id tables is unknown.
- **Decision:** until a compaction policy is chosen and tested, the sink is fit only for bounded loads that stay
  under the file ceiling. This is a gate, not an optional improvement.

**Deferred: pre-read then append for new keys.**

- Measured: 179 MiB in 0.7 s, against 10,722 MiB in 28.5 s for the same insert as a MERGE.
- It is correct only under a per-table single-writer lease, because a plain append never conflicts with a
  concurrent append of the same key.
- It is deferred to Phase 3.

**What lifts B3.** Each route needs code the binding does not provide, so the engine stays swappable behind the
seam:

- key-aware file skipping, for example a key-to-file index;
- clustering the target by key together with skipping that can use it. The current early filter is a single
  source-key range, which random source keys still span (Inferred);
- an upstream fix to the delta-rs memory amplification;
- another engine, (b) or (d).

**Risk: `FilesAsync` crash.**

- **Measured:** in DeltaLake.Net 0.33.3, the latest release, `ITable.FilesAsync` and `FileUrisAsync` corrupt the
  native heap (0xC0000374) and crash the process. From the binding source, the bridge frees the returned string
  array that the managed side then reads and frees again.
- **Workaround:** the writer counts live files by replaying `_delta_log` itself: the newest complete checkpoint,
  then the JSON commits. It fails closed on anything it cannot read. That slice of the Delta protocol is
  Ignixa-owned until the binding is fixed.
- **Why isolation matters:** any other code that calls those APIs will crash the process, which the isolation
  guard helps contain.
- **Follow-up:** report it upstream to delta-incubator/delta-dotnet.

### 6. Writer contracts

| Decision | Status | Rule |
|---|---|---|
| Projector input | Proposed (feature readme decision 1; be97f72b) | The projector walks the raw `ResourceJsonNode` against the target schema, not the element tree. Silver leaves follow Spark `from_json` token semantics, which the element tree normalizes away. |
| Existing-table validation | Proposed, implemented (1ccb6426) | The schemaString is compared structurally, not byte-for-byte, because DeltaLake.Net re-serializes it. The contract loader requires exact Delta member sets, so every loadable contract can be created and later validated. There is no `format` check on existing tables. |
| Batch reduction | Policy, implemented | A row with a null `meta.lastUpdated` is dropped. The latest row per Silver `id` wins. A tie keeps the **last occurrence in batch order**, because HDS is nondeterministic for ties. Reduction is mandatory: a duplicate source key would either fail the MERGE or, when unmatched, insert twice (Inferred from delta-rs MERGE semantics; the writer pre-reduces, so it is not exercised). |
| Merge rule | Proposed, implemented | Strict newer-wins: `target.meta_lastUpdated < source.meta_lastUpdated`. An equal or older row changes nothing and writes no version. `msftCreatedDatetime` is preserved on update. |
| Batch atomicity | Proposed, implemented | Atomicity across tables is impossible, because Delta commits are per table. Order: project everything, validate everything, create the missing v0s, then merge table by table. A partial failure raises a typed error listing which tables committed and at which version. Replaying the batch is safe and repairs the rest. An empty v0 left behind is acceptable. |
| Merge failure and cancellation | Proposed, implemented (7711fddf) | A started engine merge is never canceled: it runs with `CancellationToken.None`, because the binding reports cancellation even when the native merge committed. The caller's token is honoured before each attempt and during backoff. Cancellation once merging has started throws `FabricSilverWriteCanceledException` (an `OperationCanceledException`) carrying `CompletedTables`. A failed merge does **not** imply the table is unchanged. On a non-conflict failure after the engine started, `FabricSilverWriteException.MayHaveCommitted` is set when the reloaded version advanced or cannot be read. A conflict that exhausts its retries reports false. The message states the table may or may not be committed and that replaying is safe. Measured through test seams; a real post-commit hook failure (checkpoint or log cleanup) is untested, and its handling is Inferred. |
| Conflict handling | Proposed, implemented; known fragility | Conflicts arrive only as an untyped `DeltaRuntimeException` and are classified by its message. They are retried with bounded jittered backoff (default 5 attempts) on a fresh handle. A wording change in the binding turns retries into failures, which fails safe. |

## Other Risks

| Risk | Evidence | Mitigation or owner |
|---|---|---|
| **B4: Spark rebase.** delta-rs Parquet carries no Spark version key. Spark's default `datetimeRebaseModeInRead=EXCEPTION` then fails on pre-1582 dates and timestamps; `CORRECTED` decodes exactly. Fabric's default is unknown | Measured (local Spark); Fabric Unverified | Owner chooses between writer-stamped rebase metadata and reader configuration, then confirms on a Fabric runtime |
| **Spark read coverage.** Spark 3.5.6 with Delta 3.2.1 read sink-written tables built from the local HDS contracts: table details, history, a full decode and the change feed. This does not close B4 | Measured (local OSS Spark, not Fabric) | Fabric read check before GA |
| **Package licence metadata** disagrees with upstream (nuspec MIT, upstream Apache-2.0); about 233 MB of natives | Measured | B2 ruling; isolation guard |
| **Unconfirmed native heap corruption** after a query stream was abandoned early (seen once in the spike) | Measured once; cause Unverified | The writer always drains streams |
| **Orphan files.** Aborted attempts leave data and `_change_data` files that readers ignore | Measured | Vacuum policy (Phase 3) |
| **OneLake auth.** Credentials are string options only; a static bearer token is never refreshed; there is no `TokenCredential` hook. Whether OneLake honours `If-None-Match: *` is unknown | Paper | Phase 3 |
| **Crash safety.** Commit-window fault injection passed on Windows only; the Unix path is untested | Measured (Windows) | Linux run before GA |
| **Platforms.** Linux and macOS natives were never exercised at runtime | Unverified | Linux container run before GA |
| **Protocol pin.** 79 of the 285 version-0 logs in the HDS v1.4.0 package (other tables) are protocol 1/2; the writer pins 1/4, which Patient and Observation use | Measured (local package) | Revisit when scope widens |

## Consequences

### Positive

- Ignixa produces Delta tables that match the HDS Silver contract on protocol, configuration keys and schema
  (except the B1 value). It needs no Spark at runtime and does not redistribute HDS material.
- The correctness-heavy parts (MERGE, CDF, conflict detection, checkpoints) come from a maintained engine, so
  Ignixa owns only the v0 writer, validation and a read-only log replay.
- The engine is contained in one alpha package behind a type-level seam, so a swap is reversible.

### Negative

- **Scale.** Memory is measured only up to a 50,000-row target, a 5,000-row source and 100 files per table. The
  default ceilings permit up to a 50,000-resource source and a result of about 100,000 rows, which are unmeasured.
  Sustained use is gated on compaction.
- **Owned workarounds.** Ignixa owns a slice of the Delta protocol (live-file replay) and a message-text conflict
  classifier until the binding improves.
- **Visible deviation.** Ignixa-created tables differ from HDS in one configuration value (B1).
- **Native weight.** The package carries 113–142 MiB of natives per RID, with no musl support.

### Follow-ups

- **Gating for sustained use:** a compaction policy, with OPTIMIZE tested for protocol/metaData preservation and
  for memory on SHA-id tables.
- **Phase 3:**
  - OneLake authentication and token refresh;
  - the opt-in CDF normalization commit;
  - a vacuum policy;
  - a per-table single-writer lease, which would unlock the append route.
- **Phase 4:** CLI and server wiring.
- **B3 exit**, through one of the routes in section 5.
- **B4 owner decision.**
- **Upstream:** report the `FilesAsync` double free to delta-incubator/delta-dotnet.
- **Missing evidence:**
  - a Fabric runtime read, including CDF and the rebase default;
  - an HDS-captured row oracle, which any parity claim depends on;
  - Linux and macOS runtime runs.

## References

- Issue #550: native Fabric Silver writer.
- [Investigation: Delta engine for the native Fabric Silver writer](investigations/delta-writer-options.md)
- [Investigation: HDS Silver normalization semantics](investigations/hds-silver-normalization.md)
- [Feature readme](readme.md)
- delta-io/delta-rs#2802 ("Large Memory Spike on Merge") and delta-io/delta-rs#3786 ("Lower memory usage during
  merge when streamed_exec=False").
- Commits: be97f72b (raw-JSON projector), 1ccb6426 (strict contract member sets), 3cb585bb (Phase 2 MERGE sink), 7711fddf (possibly-committed merge reporting and the
  private-bytes budget assertion).
