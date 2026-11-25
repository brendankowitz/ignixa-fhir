# Investigation: Delta engine for the native Fabric Silver writer

**Feature**: fabric-silver
**Status**: Complete for local evidence. Spark/Fabric reads and OneLake are unverified.
**Created**: 2026-11-24

## Summary and recommendation

The candidates were:

- **(a)** DeltaLake.Net 0.33.3. This is a .NET binding over delta-rs `deltalake-core` 0.32.1, delta-kernel-rs 0.26.0 and object_store 0.13.2.
- **(b)** A managed Delta writer built on Parquet.Net.
- **(c)** A hybrid of the two.

**Recommendation: (c), the hybrid.**

- **Ignixa owns the version-0 commit.** That covers the protocol and metaData actions, and any later metaData change. It is a small managed JSON writer that publishes the commit with create-if-absent semantics.
- **DeltaLake.Net owns data movement.** That covers appends, the conditional MERGE that implements the HDS upsert, change-data-feed (CDF) emission, conflict detection and the atomic log commit.
- **A seam keeps the engine swappable.** The engine sits behind an Ignixa-owned seam inside `Ignixa.SqlOnFhir.Writers.Delta`, so swapping it for (b) later is a contained change.

| Candidate | Verdict |
|---|---|
| (a) DeltaLake.Net alone | **Not sufficient.** It cannot create a table with the HDS table properties: delta-rs rejects `delta.parquet.vorder.enabled`. It also silently disables CDF when the property value is HDS's literal `True` (capital T). |
| (b) Managed writer over Parquet.Net | **Not chosen for Phase 1.** It would mean re-implementing MERGE, CDF files, conflict checking and conditional-put commits. That is the largest correctness surface, and (a) has already measured PASS on it. This run did not size the effort (see "Not verified"). |
| (c) Hybrid | **Recommended.** Every local experiment the hybrid depends on passed (results table below). The managed part is about 100 lines of spike code. |

**Blocking decisions.** These need an owner decision; neither is an engine bug to work around silently.

1. **The `True` value on tables HDS already deployed.** HDS deploys `delta.enableChangeDataFeed` with the value `True` (capital T). Spark parses that value case-insensitively; delta-rs 0.32.1 does not. A merge into such a table writes no `cdc` actions, and the library's own CDF read then fails with `ChangeDataFeedUnsupported`.
   - **Why this matters downstream.** By the Delta protocol, a CDF reader treats a commit that has no `cdc` actions as plain adds (inserts) and removes (deletes). So a Spark change consumer would probably see every file the merge rewrote as a delete plus re-insert of every row in it. I did not run Spark to confirm this.
   - **Options:**
     - (i) Fail closed unless the value is lowercase `true`.
     - (ii) Normalise the value with an Ignixa-written metaData commit. The binding exposes no API to update table properties.
     - (iii) Accept that the table gets no CDF.
   - **Suggestion:** (i) now, (ii) as a follow-up. Tables that Ignixa creates simply write `true`.
2. **Licensing.** The package nuspec declares MIT, but the source repository's LICENSE and file headers are Apache-2.0. Both are permissive, but the discrepancy needs resolving before this becomes a production dependency.

**Not verified.** None of the following was run; do not read them as passing.

- Spark or Fabric reading these tables: plain reads, CDF over the `cdc` files, and INT64 timestamps. No Spark reader ran, and there is no HDS-captured row oracle.
- OneLake: authentication, token refresh, and whether OneLake honours the conditional create (`If-None-Match: *`).
- Linux and macOS native binaries. Only win-x64 was executed.
- Large-scale throughput, and the root cause of the merge memory peak.
- Sizing of option (b).

## Results by experiment

The setup for every experiment:

- **Schemas.** Each table was created from the real HDS v1.4.0 deployed Silver schemas for Patient and Observation, read at runtime from the caller-supplied package.
- **Environment.** Local filesystem on win-x64, .NET 10.
- **Independent checks.** I checked results without relying on the writer's engine, in two ways:
  - **Raw-log reader.** A raw replay of the `_delta_log` JSON, reading parquet with pyarrow. It shares no code with delta-rs or delta-kernel.
  - **DuckDB.** DuckDB's `delta_scan`. It runs on delta-kernel-rs, which is a different code path from the delta-rs writer, although the same organisation maintains both.
- **Not independent.** The Python `deltalake` package is delta-rs itself, so I did not count it as an independent reader.

| # | Experiment | Result | Evidence (one line) |
|---|---|---|---|
| 1 | Exact table creation | **FAIL** for (a); **PASS** for the hybrid | Library create rejects the vorder property. A create with only the CDF property matches protocol 1/4 and the schema, but omits the vorder key. The managed v0 matches HDS on protocol, format, partitionColumns, configuration and metaData key set, and the schema JSON is byte-identical. |
| 2 | Nested writes | **PASS** (types differ from Spark; both are valid) | Nested lists/structs, empty vs null arrays, decimal(36,18) at ±(10^18 − 10^-18) and 10^-18, and timestamps at 0001-01-01 all round-trip exactly. |
| 3 | MERGE/upsert semantics | **PASS** | All HDS rules hold on both types (raw-log reader). Matched duplicate source keys raise an error and nothing is committed. |
| 4 | Change data feed | **PASS** with `true`; **FAIL (silent)** with `True` | With `true`: insert, update_preimage and update_postimage rows, `cdc` actions and `_change_data` files, and the raw log and the library's kernel CDF read agree. With `True`: no CDF is written. |
| 5 | Interop reads | **PASS** (DuckDB); **UNVERIFIED** (Spark/Fabric) | DuckDB and the raw reader agree on every column of every row of 4 tables. Column order and nested types match. |
| 6 | Concurrency and crash | **PASS** (local, Windows); Linux and OneLake **UNVERIFIED** | Two writers, 3 runs, 86 conflicts: no lost or duplicate keys. 7 kills during append left no torn commit. |
| 7 | Memory/scale | **PARTIAL / CONCERN** | Appends stay bounded at 256 MB peak. An update-MERGE peaks at 1.38 GB working set against a 9.4 MB table; cause not isolated. |
| 8 | OneLake/auth | **UNVERIFIED** | Paper only; no Azure calls were made. |
| 9 | Packaging/ops | **PARTIAL** | The package is 233 MB with natives for 5 RIDs (no musl, no win-arm64). The license mismatch is flagged above. The repo's Docker image is glibc (azurelinux3.0), but the linux-x64 natives were not run. |
| 10 | Fallback sizing | **UNVERIFIED** | Deliberately deferred: the engine decision does not depend on it. |

## Evidence detail

The spike harness lives outside the repository so the HDS package never lands in git. It contains:

- a .NET 10 console app;
- Python readers: `compare_log.py`, `raw_read.py`, `duckdb_read.py` and `parquet_types.py`;
- PowerShell drivers: `race.ps1` and `crash.ps1`.

The raw outputs of every command are kept in the run directory, which is also not committed. The commands are in the harness README.

### 1. Table creation

**Library create.** `CreateTableAsync`, with a configuration containing `delta.parquet.vorder.enabled`, fails with a delta-kernel `Error parsing property 'delta.parquet.vorder.enabled'`. Changing the value's casing does not help. Three other create variants did succeed, but none matches HDS:

| Variant | Protocol | Configuration | metaData |
|---|---|---|---|
| CDF property only | 1/4, no features | Stored verbatim, including the `True` casing | Adds `"name": null` and `"description": null`, which HDS does not have |
| No properties | 1/2 | — | — |

In each of these variants the schemaString *is* byte-identical to HDS for both types: the generic Arrow schema built from the HDS schema JSON round-trips through delta-rs unchanged.

**Managed create.** The managed version-0 writer serialises the same Arrow schema back into Delta schema JSON, writes the protocol and metaData lines, and publishes with create-new semantics. Compared with HDS:

- **Same:** action order, protocol, format, `partitionColumns`, configuration (both keys, exact casing), metaData key set, and the byte-identical schema.
- **Different:** only `commitInfo`, which readers do not interpret. It has a different operation name and engine string, and its `createdTime` and timestamp come from the clock.

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
| File key-value metadata | `ARROW:schema` | Spark row metadata |

- **Unrelated HDS stub detail.** HDS's empty stub declares one value field as `decimal(18,5)`. That is irrelevant here, because Delta readers use the log schema rather than an empty stub file.
- **Spark compatibility.** Spark 3.x reads INT64 microsecond timestamps natively. This is standard Parquet, but it was not verified against Fabric here.
- **V-Order.** delta-rs does not apply V-Order. The property is metadata only, and Fabric readers do not require V-Ordered files. That claim comes from Fabric documentation and was not measured.

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

**Duplicate source keys.** Without the reduction step, a matched key that appears twice in the source makes the merge throw "MERGE matched a target row with multiple source rows", and nothing is committed. An *unmatched* key that appears twice would be inserted twice. **The reduction is therefore mandatory.**

**API limits:**

- `MergeAsync` returns no metrics. They are visible only in the commit's `commitInfo.operationMetrics`.
- The binding materialises the whole source batch in memory as a DataFusion in-memory table.
- The target is scanned with file pruning on min/max statistics of the join key. In the scale run, 9 of 10 files were skipped because ids were clustered per batch. Randomly distributed ids will prune much worse.
- The binding has a mapping bug for `WHEN NOT MATCHED BY SOURCE ... UPDATE`, which the HDS upsert does not use.

### 4. Change data feed

On a table configured with `true`:

- the seed merge wrote a `cdc` action with insert rows;
- the update merge wrote insert, `update_preimage` and `update_postimage` rows;
- the library's CDF read returned the same rows, plus `_commit_version` and `_commit_timestamp`.

On a table configured with `True`, the same merges wrote **no** `cdc` actions and no `_change_data` files, and the CDF read fails with `ChangeDataFeedUnsupported`. The raw log is the evidence, independent of any reader.

### 5. Interop

DuckDB `delta_scan` and the raw reader agree row for row and column for column. They were compared on the edge-case Patient (4 rows) and Observation (5 rows) tables and on the merged tables:

- **Patient (51 columns):** an empty array is read as an empty list, a null array as null, and nested `name[].given[]` with null elements is preserved.
- **Observation (89 columns):** decimal(36,18) values are exact at both extremes and at 10^-18; timestamps keep sub-second precision.

### 6. Concurrency and crash

**Two concurrent writers.** Two processes each merged 10 shared keys plus one unique key per iteration, with strictly increasing timestamps. A failed merge was retried by reloading the table and re-running the merge, up to 50 times.

- **Result:** in all 3 runs (20, 30 and 30 iterations), every shared key ended at the global latest value, with no duplicates and every unique key present once.
- **Error shape:** the library does not retry MERGE on a logical conflict. It throws `DeltaRuntimeException` with the text "a concurrent transactions added new data", and there is no typed conflict exception. The product retry policy must classify the error by message, or treat any failed commit as retryable a bounded number of times.
- **Orphans:** aborted attempts leave orphan data and `_change_data` files (58 on disk against 40 live after run 1). They are harmless to readers but need periodic vacuum.

**Kill test.** A 1M-row append was killed at a random 0–2.5 s after its native write began:

- **Outcome:** 7 kills, 13 completions. No torn or partial commit file, versions stayed contiguous, and the DuckDB row count always equalled the row count in the log.
- **Coverage gap:** no orphan parquet appeared, which suggests the writer buffers each file and publishes it in one step. The kill window inside the native write is short, so coverage of that window is limited.

**Commit mechanism (from source).** delta-rs commits with `put_opts(PutMode::Create)`:

| Store | How create-if-absent works |
|---|---|
| object_store local filesystem | Writes a staged file, then `hard_link`s it to the commit name; an existing name fails with AlreadyExists |
| object_store Azure | Sends `If-None-Match: *` |

Linux local behaviour and OneLake support for the conditional header were not tested.

### 7. Memory

| Workload | Peak working set | Managed heap | Notes |
|---|---|---|---|
| Append: 300k narrow Patient rows, 50k per commit | 256 MB (flat after the second commit) | ≤ 73 MB | |
| Insert-MERGE: 50k full Observation rows, 5k per merge | 559 MB | | |
| Update-MERGE: same keys, newer timestamps | 1.38 GB (first merge already 1.23 GB) | | Table is 9.4 MB on disk; merges are about 2x slower |
| Single 1M-row append (crash harness) | 2.6 GB | | Dominated by the harness's managed JSON rows, not by the engine |

The update-MERGE peak is the main performance risk. It needs profiling with production batch sizes before relying on it at scale.

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
- **Managed version-0 writer:** identical except for `commitInfo`, which is informational.

**Commits after creation.** Every later commit is written by delta-rs and has these properties:

- `add` actions carry statistics for the first 32 leaf columns and some null optional fields. Those fields are tolerated by protocol-1/4 readers but are not present in Spark output.
- No commit raises the protocol version: each subsequent commit still reads as protocol 1/4.

## Risks

| Risk | Impact | Mitigation |
|---|---|---|
| CDF silently off on `True` tables | Downstream change consumers get wrong or missing changes | Fail closed on any value other than lowercase `true`; add a startup check |
| Spark/Fabric read unverified | Tables may be unreadable or misread in Fabric | Manual or nightly Fabric job before GA; INT64 timestamps are the main suspect |
| Merge memory (1.4 GB for a 9 MB table) | Out-of-memory at scale; noisy neighbour | Bound merge batch size; profile; expose memory per batch as a metric |
| Native binary (≈150 MB per RID; 233 MB package) | Image size; no musl or win-arm64 | The repo image is glibc. Document the supported RIDs and fail fast when the native library is not loadable |
| 0.x binding; author-maintained; dependabot-driven cadence | API churn; slow fixes | Pin the version; keep it behind the Ignixa seam; the managed v0 writer already removes one dependency |
| Untyped conflict exceptions | Wrong retry classification | Bounded retries on any commit failure; the upsert is idempotent by `meta_lastUpdated`, so re-running is safe |
| Unix stack overflow (documented by the binding) | Process crash on Linux | Set `DOTNET_DefaultStackSize` in the container; verify on Linux |
| OneLake token refresh and conditional put | Long jobs fail, or a commit race corrupts the log | Unverified; real-lakehouse test is required |

## Recommended Phase-2 architecture for the Delta sink

`Ignixa.SqlOnFhir.Writers.Delta` should expose a small, engine-neutral surface. The product only ever talks to Silver tables in Ignixa terms.

- **`SilverTableDefinition`** is an immutable value: name, ordered Delta schema JSON, and table properties. It is built from the loaded Silver contract and validates its invariants at construction.
- **`ISilverDeltaTable`**, with one implementation per engine, has two operations:
  - `EnsureCreatedAsync(definition, ct)` is idempotent. If the table exists, it verifies protocol, configuration and schema against the definition, and fails closed on mismatch (including CDF casing).
  - `UpsertAsync(IAsyncEnumerable<RecordBatch> batches, ct)` applies the HDS upsert batch by batch. Each batch is reduced to the latest row per id, MERGEd as one commit, and retried with backoff on commit conflict. It returns per-commit metrics, taken from the commit's `operationMetrics`.
- **`DeltaCommitWriter`** is Ignixa-owned. It serialises version-0 and metaData commits and publishes through a storage port with create-if-absent semantics: `CreateNew` locally and `If-None-Match: *` on ADLS/OneLake.
- **The engine adapter** (`DeltaLakeNetSilverTable`) is the only type that references `DeltaLake.*`. Its lifetime rules:
  - It owns one `DeltaEngine` per process. That engine is disposable, and its thread-safety is untested, so the adapter serialises calls per table.
  - It loads a fresh table handle per commit attempt, because a stale handle commits against an old snapshot.

Swapping in a managed engine (b) means replacing only the adapter; the contract tests stay the same. Those tests are the experiments above, rewritten as xUnit tests against a temp directory and an independent reader.
