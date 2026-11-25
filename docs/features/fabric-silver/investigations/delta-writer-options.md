# Investigation: Delta engine for the native Fabric Silver writer

**Feature**: fabric-silver
**Status**: Complete for local evidence, revised after review round 1. Fabric runtime, OneLake and Linux are unverified.
**Created**: 2026-11-24

## Summary and recommendation

The candidates were:

- **(a)** DeltaLake.Net 0.33.3. This is a .NET binding over delta-rs `deltalake-core` 0.32.1, delta-kernel-rs 0.26.0 and object_store 0.13.2.
- **(b)** A managed Delta writer built on Parquet.Net.
- **(c)** A hybrid of the two.
- **(d)** EngineeredWood, a pure-managed Delta library. It was found during the review round and characterised on paper only.

**Recommendation for Phase 1: (c), the hybrid.**

- **Ignixa owns the version-0 commit.** That covers the protocol and metaData actions, and any later metaData change. It is a small managed JSON writer that publishes the commit with create-if-absent semantics.
- **DeltaLake.Net owns data movement.** That covers appends, the conditional MERGE that implements the HDS upsert, change-data-feed (CDF) emission, conflict detection and the atomic log commit.
- **A seam keeps the engine swappable.** The engine sits behind an Ignixa-owned seam inside `Ignixa.SqlOnFhir.Writers.Delta`. Swapping it later for (b) or (d) is a contained change, and B3 below makes such a swap a real possibility.

| Candidate | Verdict |
|---|---|
| (a) DeltaLake.Net alone | **Not sufficient.** It cannot create a table with the HDS table properties, because delta-rs rejects `delta.parquet.vorder.enabled`. It also silently disables CDF when the property value is HDS's literal `True` (capital T). |
| (b) Managed writer over Parquet.Net | **Not chosen for Phase 1.** The estimate is roughly 27–45 engineer-days for the gaps beyond the managed v0 writer (see experiment 10). That is the largest correctness surface of any option, and (a) already passes it locally. |
| (c) Hybrid | **Recommended for Phase 1.** Every local correctness experiment the hybrid depends on passed. That now includes a real Spark 3.5 / Delta 3.2 reader and checkpoints. The managed part is about 100 lines of spike code. **Scale is not established** (B3). |
| (d) EngineeredWood | **Not evaluated.** It is a pre-1.0, Apache-2.0 managed library that claims MERGE, CDF and checkpoints. It was not run here. It is the cheapest candidate to try if B3 rules out delta-rs MERGE. |

**Blocking decisions.** These need an owner decision; none of them is an engine bug to work around silently.

1. **B1: the `True` value on tables HDS already deployed.** HDS deploys `delta.enableChangeDataFeed` with the value `True`. delta-rs 0.32.1 parses that value case-sensitively and treats the table as CDF-off, so its merges write no `cdc` actions.
   - **Measured downstream effect.** Spark parses `True` case-insensitively, so it treats the table as CDF-on. Finding no `cdc` files, it infers changes from the add and remove actions (experiment 4). Every update appears as a delete plus an insert. Worse, rows the merge only *copied* into a rewritten file appear as **spurious delete-plus-insert pairs**.
   - **Consequence:** with this engine, an exact HDS configuration value and a working CDF cannot both hold.
   - **Proposed ruling.** The coordinator's proposal, to be recorded in an ADR and not decided here, has two parts:
     - Tables Ignixa creates write lowercase `true`.
     - Existing tables whose value is not lowercase `true` fail closed.
   - **Later option:** normalising the value with an Ignixa-written metaData commit is possible as a follow-up.
2. **B2: licensing.** The DeltaLake.Net nuspec and nuget.org declare MIT. The source repository's LICENSE and file headers are Apache-2.0. Both are permissive, but the discrepancy needs resolving before this becomes a production dependency.
3. **B3: MERGE cost at HDS id distribution (new, measured).** HDS ids are lowercase hex SHA-256, which is uniformly spread. With such keys, min/max file pruning never applies:
   - every merge scans the whole table;
   - scattered updates rewrite the whole table.
   - **Measured** on a 50k-row Observation table of 10.4 MB, merging 5k rows at a time:
     - peak working set was 9–10.7 GB;
     - each merge took 31–89 s;
     - insert merge time grew linearly with table size.
   - **Clustered ids** at the same scale peaked at 1.39 GB, at about 2 s per merge.
   - **Production readiness.** Phase 1 (local, small tables) can proceed. Production scale cannot be assumed. Before relying on this engine at scale, a follow-up must do one of the following:
     - find and fix the memory amplification inside delta-rs;
     - change the write path, for example by splitting inserts from updates using an id-only read;
     - move to (b) or (d).

**Not verified.** None of the following was run; do not read them as passing.

- **Fabric itself:** the Fabric runtime reading these tables, and its `datetimeRebaseModeInRead` default. A stock Spark 3.5.6 + Delta 3.2.1 reader *was* run locally; see experiment 5. There is still no HDS-captured row oracle.
- **OneLake:** authentication, token refresh, and whether OneLake honours the conditional create (`If-None-Match: *`). The paper evidence is in experiment 8.
- **Platforms:** Linux and macOS native binaries. Only win-x64 was executed.
- **Root cause of the MERGE memory amplification (B3)**, and throughput beyond 50k rows.
- **Fallback effort:** the sizing of (b) is an estimate, not a measurement. (d) was not run.

## Results by experiment

The setup for every experiment:

- **Schemas.** Each table was created from the real HDS v1.4.0 deployed Silver schemas for Patient and Observation, read at runtime from the caller-supplied package.
- **Environment.** Local filesystem on win-x64, .NET 10.
- **Independent checks.** I checked results without relying on the writer's engine:
  - **Raw-log reader.** A raw replay of the `_delta_log` JSON, reading parquet with pyarrow. It shares no code with delta-rs or delta-kernel.
  - **DuckDB.** DuckDB's `delta_scan`, which runs on delta-kernel-rs.
  - **Spark.** Apache Spark 3.5.6 with delta-spark 3.2.1 on Java 17, run from the official `spark` Docker image. This matches the Spark and Delta versions of Fabric Runtime 1.3. It is not Fabric itself.
- **Not independent.** The Python `deltalake` package is delta-rs itself, so I did not count it as an independent reader.

| # | Experiment | Result | Evidence (one line) |
|---|---|---|---|
| 1 | Exact table creation | **FAIL** for (a). **PASS except CDF value casing** for the hybrid | Library create rejects the vorder property. The managed v0 matches HDS on protocol, format, partitionColumns, metaData key set and configuration keys, and the schema JSON is byte-identical. Exact HDS casing (`True`) and working CDF cannot both hold with this engine (B1). Ignixa-created tables are therefore expected to write `true`. |
| 2 | Nested writes | **PASS** (types differ from Spark; both are valid) | Nested lists and structs, empty vs null arrays, and decimal(36,18) at ±(10^18 − 10^-18) and 10^-18 all round-trip exactly, as do timestamps at 0001-01-01. Ancient timestamps need a Spark reader setting; see 5. |
| 3 | MERGE/upsert semantics | **PASS** | All HDS rules hold on both types (raw-log reader and Spark). Matched duplicate source keys raise an error and nothing is committed. |
| 4 | Change data feed | **PASS** with `true`. **FAIL (silent, wrong in Spark)** with `True` | With `true`: the raw log, the library's kernel CDF read and Spark `readChangeFeed`/`table_changes` all agree, and copied rows are not reported. With `True`: delta-rs writes no CDF, and Spark reports updates as delete+insert, plus spurious delete+insert for copied rows. |
| 5 | Interop reads | **PASS** with DuckDB. **PASS with one setting** in local Spark 3.5/Delta 3.2. **UNVERIFIED** in Fabric | DuckDB and the raw reader agree on every column of every row. Spark reads all four hybrid tables. Under Spark's default `datetimeRebaseModeInRead=EXCEPTION`, Patient fails on a 0001-01-01 value; with `CORRECTED` every column decodes exactly. |
| 6 | Concurrency and crash | **PASS** locally on Windows, including deterministic injection into the data-written/commit-not-written window. Linux and OneLake **UNVERIFIED** | Two writers, 3 runs, 86 conflicts: no lost or duplicate keys. Three fault injections in the commit window (denied commit, staged-commit litter, process frozen between data file and commit): every case left a contiguous log, readers saw only committed rows, and the next writer committed. |
| 7 | Memory/scale | **CONCERN → measured scale blocker (B3)** | Clustered ids: update-MERGE peaks at 1.39 GB. SHA-256 ids (as HDS uses): every merge scans every file, scattered updates rewrite the whole table, and peaks reach 9.3–10.7 GB at 31–89 s per 5k-row merge into a 10 MB table. |
| 8 | OneLake/auth | **UNVERIFIED** (paper evidence below) | Azure support is compiled in. Credentials can be passed only as string storage options, with no .NET `TokenCredential` hook. OAuth flows refresh tokens; a static bearer token does not. The commit uses `If-None-Match: *`. |
| 9 | Packaging/ops | **PARTIAL** | The package is 233 MB with natives for 5 RIDs (no musl, no win-arm64). The license mismatch is B2. The repo's Docker image is glibc (azurelinux3.0), but the linux-x64 natives were not run. |
| 10 | Fallback sizing | **ESTIMATE** (judgment, not measured) | (b): about 27–45 engineer-days beyond the managed v0 writer. (d): characterised from its package and README only. |
| 11 | Checkpoints | **PASS** (local) | A hybrid table driven to v105 got a delta-rs checkpoint at v99. It preserved both properties, protocol 1/4 with no features, and a byte-identical schemaString. DeltaLake.Net, DuckDB and Spark all read the table, including a copy where only the checkpoint could be used. |

## Evidence detail

The spike harness lives outside the repository, so the HDS package never lands in git. It contains:

- a .NET 10 console app;
- Python readers: `compare_log.py`, `raw_read.py`, `duckdb_read.py`, `parquet_types.py` and `spark_read.py`;
- PowerShell drivers: `race.ps1`, `crash.ps1`, `commit-window.ps1`, `mem-ids.ps1` and `checkpoint.ps1`.

The raw outputs of every command are kept in the run directory, which is also not committed. The commands are in the harness README.

### 1. Table creation

**Library create.** `CreateTableAsync`, with a configuration containing `delta.parquet.vorder.enabled`, fails with a delta-kernel `Error parsing property 'delta.parquet.vorder.enabled'`. Changing the value's casing does not help. Two other create variants did succeed, but neither matches HDS:

| Variant | Protocol | Configuration | metaData |
|---|---|---|---|
| CDF property only | 1/4, no features | Stored verbatim, including the `True` casing | Adds `"name": null` and `"description": null`, which HDS does not have |
| No properties | 1/2 | — | — |

In both variants the schemaString *is* byte-identical to HDS for both types: the generic Arrow schema built from the HDS schema JSON round-trips through delta-rs unchanged.

**Managed create.** The managed version-0 writer serialises the same Arrow schema back into Delta schema JSON, writes the protocol and metaData lines, and publishes with create-new semantics. Compared with HDS:

- **Same:**
  - action order;
  - protocol;
  - format;
  - `partitionColumns`;
  - the configuration keys;
  - the metaData key set;
  - the byte-identical schema.
- **Different:**
  - `commitInfo`, which readers do not interpret;
  - the CDF value, when the writer is given `true` instead of HDS's `True`.

  The writer stores whatever value it is given. With `True` it is byte-for-byte HDS, but CDF is then broken (B1). With `true` it differs from HDS in the casing of that one value, which is the expected product behaviour.

DeltaLake.Net loads that table and appends and merges into it. A table holding the exact HDS-written version-0 commit also loads and accepts appends.

### 2. Nested writes and Parquet types

**Mapping code.** Rows go in as Arrow `RecordBatch`es. Two spike components do this:

- a schema-driven converter from Delta schema JSON to an Arrow `Schema` (about 70 lines);
- a strict, recursive, columnar builder from JSON rows to a `RecordBatch` (about 190 lines). It uses explicit validity bitmaps; the 36-digit decimal append works.

The product code needs the same converter fed from typed projection output instead of JSON.

**Parquet comparison.** delta-rs writes Parquet differently from the empty part file HDS ships, which Spark wrote:

| Aspect | delta-rs | HDS (Spark) |
|---|---|---|
| Timestamps | INT64 `TIMESTAMP(MICROS, isAdjustedToUTC=true)` | INT96 |
| `decimal(36,18)` | FIXED_LEN_BYTE_ARRAY(16) with a Decimal(36,18) logical type | — |
| Dates | INT32 DATE (same) | INT32 DATE (same) |
| List encoding | Standard three-level, `list.element` naming; definition and repetition levels match column for column | Same |
| Compression | Snappy (same) | Snappy (same) |
| File key-value metadata | `ARROW:schema` only; **no `org.apache.spark.version`** | Spark row metadata and version |

**Missing Spark version key.** The absence of `org.apache.spark.version` matters for Spark readers; see experiment 5.

**Unrelated HDS stub detail.** HDS's empty stub declares one value field as `decimal(18,5)`. That is irrelevant here, because Delta readers use the log schema rather than an empty stub file.

**V-Order.** delta-rs does not apply V-Order. The property is metadata only, and Fabric readers do not require V-Ordered files. That claim comes from Fabric documentation and was not measured.

### 3. MERGE semantics

**The SQL.** One `MergeAsync(sql, batches, schema)` call does the whole upsert:

```
MERGE INTO target USING source ON target.id = source.id
WHEN MATCHED AND target.meta_lastUpdated < source.meta_lastUpdated THEN UPDATE SET <every column except msftCreatedDatetime>
WHEN NOT MATCHED AND source.meta_lastUpdated IS NOT NULL THEN INSERT <all columns>
```

Camel-case column names work unquoted. The source batch is reduced beforehand to the latest row per `id`, ordered by `meta_lastUpdated`.

**Verified outcomes on both types:**

| Row | Expected | Observed |
|---|---|---|
| Null `meta_lastUpdated` | Not inserted | Not inserted |
| Older and equal rows | No change | No change, and no commit at all: a no-op merge writes no new version |
| Newer row | Updated | Updated, and `msftCreatedDatetime` kept the target value even though the source carried a different one |
| Key duplicated in the source | Latest wins | Latest wins |
| New key | Inserted | Inserted |

Spark's projected read of the merged tables returned the expected ids and `meta_lastUpdated` values.

**Duplicate source keys.** Without the reduction step, a matched key that appears twice in the source makes the merge throw "MERGE matched a target row with multiple source rows", and nothing is committed. An *unmatched* key that appears twice would be inserted twice. **The reduction is therefore mandatory.**

**API limits:**

- `MergeAsync` returns no metrics. They are visible only in the commit's `commitInfo.operationMetrics`, under snake_case keys such as `num_target_files_scanned`, `num_target_files_skipped_during_scan`, `num_target_rows_copied` and `num_target_rows_updated`.
- The binding materialises the whole source batch in memory as a DataFusion in-memory table.
- The target is pruned only by min/max statistics of the join key. With HDS ids that never prunes; see experiment 7.
- The binding has a mapping bug for `WHEN NOT MATCHED BY SOURCE ... UPDATE`, which the HDS upsert does not use.

### 4. Change data feed

**Test tables.** Each type got two hybrid tables, one configured with `true` and one with `True`, each with five versions:

| Version | Operation |
|---|---|
| v0 | Create |
| v1 | Seed merge |
| v2 | Newer-row merge |
| v3 | Append of edge rows |
| v4 | Merge that updates only row `a`, so the rewritten file also carries rows `b` and `d` as *copied* rows |

**With `true`:**

- **Raw log:** the merges write `cdc` actions and `_change_data` files containing insert, `update_preimage` and `update_postimage` rows.
- **Library kernel read:** the library's kernel CDF read returns the same rows, plus `_commit_version` and `_commit_timestamp`.
- **Spark:** Spark `readChangeFeed` from v0 and `table_changes` from v1 return the same changes. At v4 they report only `a`'s pre- and post-image; the copied rows `b` and `d` are correctly absent.

**With `True`:**

- **Raw log:** the same merges write **no** `cdc` actions and no `_change_data` files, and the library's CDF read fails with `ChangeDataFeedUnsupported`.
- **Spark:** Spark treats the table as CDF-enabled and falls back to the add and remove actions:
  - v2 reports `a` and `b` as delete+insert instead of update pre- and post-images;
  - v4 reports `a`, `b` *and* `d` as delete+insert, although `b` and `d` did not change.

  A change consumer would therefore see phantom changes for every row in every file a merge rewrites.

### 5. Interop

**DuckDB.** DuckDB `delta_scan` and the raw reader agree row for row and column for column. They were compared on the edge-case Patient (4 rows) and Observation (5 rows) tables and on the merged tables:

- **Patient (51 columns):** an empty array is read as an empty list, a null array as null, and nested `name[].given[]` with null elements is preserved.
- **Observation (89 columns):** decimal(36,18) values are exact at both extremes and at 10^-18; timestamps keep sub-second precision.

**Spark 3.5.6 + Delta 3.2.1** (Docker; session time zone UTC) on the four hybrid tables. Every value was decoded inside the JVM through the `noop` sink, so Python conversion limits play no part.

- **Metadata:** `DESCRIBE DETAIL` reports minReader 1, minWriter 4, and both properties exactly as written. `DESCRIBE HISTORY` reads every version.
- **Projected reads:** id and `meta_lastUpdated` reads return the expected rows and values.
- **Observation, full decode:** all 89 columns decode under the default settings.
- **Patient, full decode under defaults: fails.** The error is `SparkUpgradeException [INCONSISTENT_BEHAVIOR_CROSS_VERSION.READ_ANCIENT_DATETIME]`, caused by an edge row's `birthDate` of 0001-01-01.
  - **Why:** delta-rs files carry no `org.apache.spark.version` key. Spark cannot rule out that the file was written with the legacy hybrid calendar, so under the default `datetimeRebaseModeInRead=EXCEPTION` it refuses to read values before 1582.
  - **With `CORRECTED`:** every column decodes, and the edge values are 0001-01-01 00:00:00 and 9999-12-31 23:59:59.999999.
- **Not verified:** the Fabric runtime's default for this setting.
- **Mitigation options**, which need an owner decision:
  - document that readers set `CORRECTED`;
  - have the writer stamp Spark-compatible key-value metadata into each Parquet file. The binding does not appear to expose Parquet key-value metadata (unverified);
  - reject or clamp pre-1582 values at projection. This loses data, so it is not recommended.

**Windows-native Spark.** It needs `winutils.exe`, which I did not install, so Docker is the reproducible route.

### 6. Concurrency and crash

**Two concurrent writers.** Two processes each merged 10 shared keys plus one unique key per iteration, with strictly increasing timestamps. A failed merge was retried by reloading the table and re-running the merge, up to 50 times.

- **Result:** in all 3 runs (20, 30 and 30 iterations), every shared key ended at the global latest value, with no duplicates and every unique key present once.
- **Error shape:** the library does not retry MERGE on a logical conflict. It throws `DeltaRuntimeException` with the text "a concurrent transactions added new data", and there is no typed conflict exception. The product retry policy must classify the error by message, or treat any failed commit as retryable a bounded number of times.
- **Orphans:** aborted attempts leave orphan data and `_change_data` files (58 on disk against 40 live after run 1). They are harmless to readers but need periodic vacuum.

**Correction to the first round.** The first round's random-kill test does not support a "no torn commits" claim:

- three of its "kills" landed after the commit;
- the rest landed before any data file existed;
- none landed in the window between data file written and commit published.

The window was therefore re-tested with deterministic fault injection:

| Injection | What happened | After the fault |
|---|---|---|
| **A. Commit denied:** an NTFS deny-write ACE on `_delta_log` | The data file was fully written (1 → 2 parquet files) and the commit failed with an I/O error | Log stayed at v1. The table loads. DuckDB row count equalled the log's row count. The orphan file is a complete, readable parquet file that readers ignore. The next writer committed v2. |
| **B. Staged-commit litter:** a planted `<next>.json#1` file holding a truncated line | — | Readers ignore it: object_store hides `#<digits>` staging names. The next writer committed that version normally. The litter file remains. |
| **C. Process frozen in the window:** suspended the moment the first data file was complete (4,062,290 bytes), then killed | 5 of 6 runs landed in the window: data file present, log not advanced. 1 run landed after the commit. | In every run: the log was contiguous, every commit JSON was complete, DuckDB rows equalled the log's rows, and the next writer committed +1. |

**Commit mechanism (from source).** delta-rs commits with `put_opts(PutMode::Create)`:

| Store | How create-if-absent works |
|---|---|
| object_store local filesystem | Writes a staged `<name>#<n>` file, then `hard_link`s it to the commit name. An existing name fails with AlreadyExists, so a commit appears atomically or not at all |
| object_store Azure | Sends `If-None-Match: *` |

**What the production sink test must do.** It should:

- inject a failure after the data put and before the commit put, through the storage port or an ACL;
- assert that the log is contiguous, that reader counts equal the log's counts, and that the next commit succeeds;
- run the same test against OneLake (UNVERIFIED here).

Orphans from failed attempts need a VACUUM policy.

### 7. Memory and scale

The tables were Observation tables with 89 columns, holding 50k rows written in 5k-row merges: an insert phase, then an update phase with newer timestamps. Each phase ran in its own process, and the per-commit metrics come from `operationMetrics`.

**Earlier workloads:**

| Workload | Peak working set | Notes |
|---|---|---|
| Append, 300k narrow Patient rows, 50k per commit | 256 MB (flat after the second commit) | Managed heap ≤ 73 MB |
| Single 1M-row append (crash harness) | 2.6 GB | Dominated by the harness's managed JSON rows, not by the engine |

**Merge workloads by id distribution:**

| Id distribution | Phase | Files scanned / skipped / removed per merge | Rows copied | Peak working set | Time per merge |
|---|---|---|---|---|---|
| Clustered (`s00000001`…) | Insert | 0 / k / 0 | 0 | 564 MB | 0.7–0.9 s |
| Clustered | Update | 1 / 9 / 1 | 0 | 1.39 GB | 1.9–2.2 s |
| SHA-256 hex | Insert | k / 0 / 0 (all files) | 0 | 0.4 → **9.3 GB**, growing with table size | 0.8 → **28 s**, growing with table size |
| SHA-256 hex, each update batch equal to one insert batch | Update | 10 / 0 / 1 | 0 | **10.7 GB** | 31–89 s |
| SHA-256 hex, scattered (each batch hits every file) | Update | first merge 10 / 0 / 10, then 1 / 0 / 1 | **45,000 per 5k-row merge** | **10.5 GB** | 35–56 s |

(k is the number of files already in the table.)

**Why ids matter.**

- **HDS ids.** An HDS Silver `id` is the lowercase hex SHA-256 of the resource's namespace, type and id. Every data file therefore spans nearly the whole key range, so min/max statistics cannot skip any file.
- **Inserts.** Even inserting new keys scans the whole table.
- **Updates.** Any realistic update batch hits rows in every file, so copy-on-write rewrites the whole table on every merge. This part is inherent to copy-on-write without deletion vectors, whatever the engine.
- **Engine-specific part.** About 10 GB of working set to merge into a table of 10.4 MB on disk (about 50k rows) is amplification beyond copy-on-write. Merge time and memory track table rows, not file count: the scattered case kept its cost after the table collapsed to one file.

**Conclusion.** With HDS-like ids, delta-rs MERGE cost grows with table size on every merge. Extrapolating, which was not measured, the per-merge cost is not viable beyond a few hundred thousand rows per table on ordinary hardware. This is B3.

**Mitigations to evaluate, none of them tested:**

- profile the delta-rs MERGE plan, including whether the full target is materialised in the join;
- split each batch with an id-only projection read: new keys go through append, and only existing keys go through MERGE. This needs a guard against a concurrent insert of the same key;
- bound table size per merge;
- an engine with a different merge strategy, such as (b) or (d). Deletion vectors are not an option on 1/4 tables; see experiment 10.

### 8. OneLake and authentication (paper only)

No Azure calls were made. The sources are the DeltaLake.Net 0.33.3 source and the exact crate versions it pins.

- **Backends.** The native bridge is built with the `azure`, `gcs`, `s3` and `datafusion` features, and it registers the Azure, AWS and GCP handlers at startup.
- **Credentials:**
  - **Only string options.** Credentials reach the engine only as a string dictionary (`StorageOptions`).
  - **No .NET credential hook.** There is no hook for a .NET `TokenCredential` or `DefaultAzureCredential`. A .NET credential can be used only by acquiring a token in .NET and passing it as `bearer_token`.
  - **A second store.** The kernel read path builds a second object store from the same options.
- **object_store 0.13.2 Azure options:**
  - **OneLake endpoint.** `use_fabric_endpoint` exists, and URLs on `dfs.fabric.microsoft.com` or `blob.fabric.microsoft.com` turn it on automatically.
  - **Credential flows:**
    - client secret;
    - workload identity (federated token file);
    - managed identity (IMDS, the default when nothing else is set);
    - Azure CLI;
    - a Fabric token-service flow for code running inside Fabric;
    - a static bearer token.
- **Token refresh:**
  - The OAuth flows cache tokens and refresh them when less than 300 s of validity remains. The scope is `https://storage.azure.com/.default`.
  - A static bearer token is never refreshed. The product must reload the table with a fresh token before expiry, which fits the "fresh table handle per commit attempt" rule below.
- **Conditional put.** The Azure client maps `PutMode::Create` to `If-None-Match: *`. Whether OneLake honours that header is **unverified**.
- **Needs a real lakehouse.** These need a manual or nightly job against a real lakehouse:
  - each auth flow;
  - token expiry during a long job;
  - concurrent writers, checking that the conditional create is honoured;
  - a Fabric Spark read and CDF read of an Ignixa-written table;
  - Fabric's rebase default.

### 10. Fallback sizing

**(b) Managed writer over Parquet.Net.** The managed v0 and metaData writer already exists in the hybrid. The remaining gaps are below. The figures are judgment-based engineer-days for production quality including tests; they are not measured.

| Gap | Days | Main correctness risk |
|---|---|---|
| Log replay and snapshot (JSON commits plus checkpoint reading) | 3–5 | Missing a remove or a protocol change; checkpoint edge cases |
| Parquet data files from Arrow-shaped rows, with `add` statistics for the first 32 columns | 3–5 | Nested null and empty encodings; statistics that break other readers' pruning |
| Parquet control: INT96 or INT64 timestamps, and Spark key-value metadata (needed for the rebase issue above) | 1–2 | Parquet.Net's INT96 and custom-metadata support is unverified |
| MERGE as file rewrite: find candidate files, read them, apply the HDS rules, write new files, emit remove and add | 5–8 | Lost or duplicated rows; copy-on-write cost (B3 applies to any engine without deletion vectors) |
| CDF emission: `_change_data` files and `cdc` actions with insert, pre-image and post-image | 2–3 | Wrong change types, the same failure as with `True` |
| Optimistic concurrency: re-validate against commits that landed since the snapshot, then retry | 3–5 | Silent lost updates |
| Conditional put on local storage and on ADLS/OneLake (`If-None-Match: *`) | 1–2 | OneLake semantics unverified |
| Checkpoints and log cleanup | 3–4 | Readers slow down without checkpoints; a bad checkpoint breaks every reader |
| Cross-engine contract tests (Spark, DuckDB, raw log) | 5–8 | — |
| **Total** | **≈ 27–45** | The largest correctness surface of any option |

**(d) EngineeredWood** (characterised from nuget.org and its README; not run):

- **Packaging and licence.** Version 0.4.0 was published 2026-11-08, the fourth release since 2026-07. It is Apache-2.0 and pure managed, targeting netstandard2.0, net8.0 and net10.0, with no native dependency.
- **Claimed features:**
  - reader protocol v3 and writer protocol v7;
  - MERGE, UPDATE and DELETE;
  - CDF and deletion vectors;
  - column mapping;
  - V2 checkpoints;
  - optimistic concurrency, committing through a single conditional create on Azure, GCS and S3.
- **Validation and maturity.** It claims CI cross-validation against PySpark and delta-rs. The project describes itself as an experiment built with an AI coding agent, and its API is unstable before 1.0.
- **Relevance.** Deletion vectors would avoid full-file rewrites under B3, but the Delta protocol makes them a reader 3 / writer 7 table feature. HDS tables are 1/4, so they cannot help without a protocol upgrade, which would break exact HDS parity.
- **Next step:** a bounded spike of 1–2 days running this document's contract experiments against it is the cheapest next step if B3 rules out delta-rs.

### 11. Checkpoints

A hybrid table (Ignixa v0 with the vorder property and CDF `true`) was driven to v105 with single-row appends.

- **Checkpoint written.** delta-rs wrote `00000000000000000099.checkpoint.parquet` and `_last_checkpoint` (version 99, 101 actions, 99 add files). Its post-commit hook checkpoints when `(version + 1)` is a multiple of `delta.checkpointInterval`, which defaults to 100.
- **Contents:**
  - protocol 1/4 with null reader and writer features;
  - configuration with both `delta.enableChangeDataFeed: true` and `delta.parquet.vorder.enabled: true`;
  - empty `partitionColumns`;
  - a schemaString byte-identical to v0.
- **Readers.** DeltaLake.Net reloads at v105. DuckDB reads 105 rows. Spark reads all 51 columns, and its CDF from v0 returns 105 inserts.
- **Checkpoint-only copy.** A copy with commits v0–v99 deleted forces checkpoint use, and it still loads in DeltaLake.Net, DuckDB and Spark, all at 105 rows. CDF over the deleted versions fails, as expected.
- **Log cleanup.** All 106 commit JSON files were retained. delta-rs's post-commit cleanup (`cleanup_expired_logs_for`) deletes only log files older than `delta.logRetentionDuration`, which defaults to 30 days, and every file here was minutes old. Cleanup past the retention window was not exercised.
- **Post-commit failure.** The checkpoint is written in a post-commit hook. A hook failure surfaces as an error *after* the commit has landed, so retry logic must tolerate re-running an already-committed, idempotent upsert.

## Protocol and configuration diff vs HDS (in words)

**HDS version 0:**

- commitInfo, protocol and metaData actions, in that order;
- protocol reader 1 / writer 4 with no table features;
- metaData has exactly id, format (parquet, empty options), schemaString, empty partitionColumns, configuration and createdTime;
- configuration has the CDF property set to `True` and the V-Order property set to `true`;
- no data-file actions.

**How each create path differs:**

- **delta-rs, CDF property only:** the same, except the V-Order key is missing and metaData carries extra null `name` and `description` keys.
- **delta-rs, no properties:** writer version 2.
- **Managed version-0 writer:** identical except for `commitInfo`, which is informational, and the CDF value's casing when it is given `true` (B1).

**Commits after creation.** Every later commit is written by delta-rs and has these properties:

- **Statistics.** `add` actions carry statistics for the first 32 leaf columns and some null optional fields. Those fields are tolerated by protocol-1/4 readers but are not present in Spark output.
- **No protocol upgrade.** No commit raises the protocol version, and no commit rewrites protocol or metaData. Checked up to v105:
  - each later commit holds only commitInfo, add, remove and cdc actions;
  - the v99 checkpoint carries protocol 1/4 with no features and the original configuration.
- **Not exercised:** compaction, vacuum and schema evolution.

## Risks

| Risk | Impact | Mitigation |
|---|---|---|
| MERGE cost with SHA-256 ids (B3) | Merges scan and rewrite the whole table, and use about 10 GB for a 10 MB table; out-of-memory and hours-long merges at production size | Phase 1 only at small scale; profile; id-only pre-read to route inserts to append; evaluate (b) or (d) before production scale |
| CDF on `True` tables (B1) | Missing CDF in delta-rs, and phantom delete+insert changes in Spark | Ignixa-created tables write `true`; fail closed on any other value; startup check |
| Spark ancient-timestamp rebase | Fabric notebooks fail on pre-1582 dates under the Spark default | Verify the Fabric default; document `CORRECTED`, or stamp Spark metadata in the Parquet files |
| Fabric runtime unverified | Fabric-specific behaviour, such as V-Order or the rebase default, may differ from stock Spark | Manual or nightly Fabric job before GA |
| Native binary (≈150 MB per RID; 233 MB package) | Image size; no musl or win-arm64 | The repo image is glibc. Document the supported RIDs and fail fast when the native library cannot be loaded |
| 0.x binding; author-maintained; dependabot-driven cadence | API churn; slow fixes | Pin the version; keep it behind the Ignixa seam; the managed v0 writer already removes one dependency |
| Untyped conflict exceptions, and post-commit hook errors | Wrong retry classification | Bounded retries on any commit failure; the upsert is idempotent by `meta_lastUpdated`, so re-running is safe |
| Unix stack overflow (documented by the binding) | Process crash on Linux | Set `DOTNET_DefaultStackSize` in the container; verify on Linux |
| OneLake token refresh and conditional put | Long jobs fail with a static token, or a commit race corrupts the log | Use a refreshing OAuth flow or reload per commit; real-lakehouse test required |

## Recommended Phase-2 architecture for the Delta sink

`Ignixa.SqlOnFhir.Writers.Delta` should expose a small, engine-neutral surface. The product only ever talks to Silver tables in Ignixa terms.

- **`SilverTableDefinition`** is an immutable value: name, ordered Delta schema JSON, and table properties. It is built from the loaded Silver contract and validates its invariants at construction.
- **`ISilverDeltaTable`**, with one implementation per engine, has two operations:
  - `EnsureCreatedAsync(definition, ct)` is idempotent. If the table exists, it verifies protocol, configuration and schema against the definition, and fails closed on mismatch, including a CDF value other than lowercase `true`.
  - `UpsertAsync(IAsyncEnumerable<RecordBatch> batches, ct)` applies the HDS upsert batch by batch. Each batch is reduced to the latest row per id, MERGEd as one commit, and retried with backoff on commit conflict. It returns per-commit metrics, taken from the commit's `operationMetrics`. The B3 outcome may change how a batch is split between append and MERGE behind this same signature.
- **`DeltaCommitWriter`** is Ignixa-owned. It serialises version-0 and metaData commits and publishes through a storage port with create-if-absent semantics: `CreateNew` locally and `If-None-Match: *` on ADLS/OneLake.
- **The engine adapter** (`DeltaLakeNetSilverTable`) is the only type that references `DeltaLake.*`. Its lifetime rules:
  - It owns one `DeltaEngine` per process. That engine is disposable, and its thread-safety is untested, so the adapter serialises calls per table.
  - It loads a fresh table handle per commit attempt, because a stale handle commits against an old snapshot. The same rule also picks up a fresh token.

Swapping in a managed engine, (b) or (d), means replacing only the adapter, and the contract tests stay the same. Those tests are the experiments above, rewritten as xUnit tests against a temp directory and an independent reader. The tests should cover:

- the commit-window fault injection;
- a checkpoint crossing;
- a SHA-256-id scale case with a memory budget.
