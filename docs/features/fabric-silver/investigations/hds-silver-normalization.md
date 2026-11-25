# Investigation: HDS Silver normalization semantics (Patient, Observation)

**Feature**: fabric-silver
**Status**: Complete (source-derived; no captured HDS row oracle exists)
**Created**: 2026-11-24

## Approach

Issue #550 asks for a native writer that produces Fabric-compatible Silver Delta tables for FHIR R4
`Patient` and `Observation`. Before choosing a design we need the row semantics the writer must
reproduce. This document derives them from the HDS v1.4.0 package source and the shipped Silver
schemas, **before** reading the depot session's claims, then cross-checks those claims (section I).

Nothing here is transplanted. Behavior is described in my own words; HDS is cited by package-relative
path and line. Paths are relative to `HDS.SourceCode/src/microsoft/fabric/hls/hds/` unless prefixed
with `config/` (then `HDS.SourceCode/src/config/fhir4/`) or `deployed/` (the deployed Silver
`_delta_log` v0 under `HDS.SourceCode/hds-build-artifacts/healthcare-tables/1.4.0/Silver/<Table>/`,
read locally for field order and types only; no schema text is reproduced). Line numbers marked `~`
are approximate (a few lines either way).

Evidence classes used below:

- **OBS** - observed directly in source or deployed metadata.
- **INF** - inferred from source plus standard Spark/Python/JDK behavior; not run.
- **UNV** - unverifiable without a real Spark run on HDS; listed again in section J.

Scope stays Patient and Observation (Phase 1, then local Phase 2). The depot's expansion to 146 R4
types does not change this document; the rules below are generic and the per-resource lists are
computed for these two only. This investigation does not depend on any depot Python API.

## A. Pipeline

| # | Step | Evidence |
|---|------|----------|
| 1 | Silver ingestion processes Bronze data per micro-batch (`epochId`) and per `resourceType`; one parse + transformation call per resource | OBS `services/silver_ingestion_service.py:576-624` |
| 2 | Select Bronze `id`, `resourceType`, content, `sourceSystem` (renamed `msftSourceSystem`) and `filePath` (renamed `msftFilePath`) | OBS same file :537-544 |
| 3 | `from_json(content, <Spark schema converted from the Silver .avsc>)`, **no options**; parsed fields are lifted to top level, skipping names already present (so `id`/`resourceType` stay the Bronze values) | OBS :549-559; schema loader `utils/utils.py:114-139` (Avro-to-Spark schema converter) |
| 4 | Flatten config for Patient/Observation is auto-generated from the avsc; there is no hand-written per-column config | OBS `flatten/flatten_config_generator.py:143-165` |
| 5 | Column step: only adds `meta_lastUpdated` (copy of `meta.lastUpdated`); source columns retained | OBS generator :159; `flatten/core.py` ~345-480 |
| 6 | Normalization: date `meta_lastUpdated`; id rule (also drives `identifierOrig`); then default rules (reference, date, nested) | OBS generator :161-163; `flatten/core.py` ~170-270 |
| 7 | Stringify complex columns into `*_string` | OBS `flatten/core.py` ~345-480; `utils/dataframe_utils.py:53-79` |
| 8 | Stamp `msftModifiedDatetime` then `msftCreatedDatetime`, move modified time to column 0, dedupe per `id`, merge | OBS `utils/dataframe_utils.py:566-632` |

Two consequences matter for a native writer:

1. Spark type coercion happens **first** (step 3). The avsc types decide whether a date arrives as a
   date, a timestamp or a string. The Python normalizers then only see Spark-typed values.
2. Rule order inside normalization is fixed (step 6), and it is what produces the physical column
   order (section E).

Normalization **failures are partly swallowed**: identifier normalization raises `ValueError` on a
missing column or schema mismatch, and `flatten/core.py` (~215-225) only logs it and continues, so `id`
can still be normalized while `identifierOrig` and the appended identifier are skipped (OBS). Reference
normalization is the opposite: it raises (section C) and the task fails.

## B. `id` / `idOrig`

OBS `flatten/normalization/identifier_normalization.py:14-43`, `flatten/normalization/utils.py:12-36`.

- Inputs: the resource type literal for the table (generator :162 passes `Patient` / `Observation`),
  the original `id`, and `msftSourceSystem`.
- If `id` or type is null, the result is null.
- Base string is `<Type>/<id>`.
- If `msftSourceSystem` is non-empty: strip a leading `scheme://` (`^\w+://`), strip **one** trailing
  `/`, and prefix `<namespace>/`. Empty or null source system means no prefix.
- Result is the lowercase hex SHA-256 of the UTF-8 bytes of that string.
- `idOrig` is the original `id`.
- INF: a source system that becomes empty after stripping (e.g. literally `https://`) would make
  string concatenation fail. Not reachable from stock ingestion (below).

**What `msftSourceSystem` is in stock HDS (OBS, important):** it is **not** a URL and not
caller-supplied. Bronze extracts it from the input file path with a fixed pattern that takes the single
path segment following `External|Process/<segment>/<segment>/` (`utils/dataframe_utils.py:922-948`,
called from the Bronze ingestion, `services/bronze_ingestion_service.py` ~398-430), and sets it null
when the pattern does not match. For a path such as `.../Process/Clinical/FHIR-NDJSON/east/...` the value
is the token `east`. No other code in the package sets it (grep of the package: only this derivation and
the Silver rename). Consequences:

- The scheme/trailing-slash stripping is a no-op for stock input; id hashing uses a bare token.
- Null `msftSourceSystem` (path did not match) is legal for ids (hash of `<Type>/<id>` alone) but
  **fails reference normalization** (section C), so stock rows with a null source system fail the task
  whenever a reference rule runs (OBS `reference_normalization.py:25-27`; Spark null handling INF).

## C. Identifier and reference normalization

### C1. `identifier` / `identifierOrig` (OBS `identifier_normalization.py:240-509`, `normalization/utils.py:97-138`)

1. `identifierOrig` = copy of the parsed identifier array **before** any change. Null stays null.
2. Schema check: the column must match a fixed Identifier layout (`flatten/constants.py`
   `NORM_IDENTIFIER_SCHEMA`: extension, use, value, system, type{extension, id, coding[...], text},
   period{extension, id, start, end}, assigner{extension, id, reference, type, identifier{seven string
   leaves}, display}). The identifier element itself has **no** `id`. A mismatch raises and is
   logged-and-swallowed (A).
3. Conform: each element is rebuilt into that layout; a null array becomes an **empty** array.
4. If no element has `type.coding[*].code = 'fhirId'` (system is not checked), append one element:
   `value` = original resource id, `system` = `msftSourceSystem`, `type` = {coding: [{system
   `http://terminology.hl7.org/CodeSystem/v2-0203`, code `fhirId`, display `FHIR Id`}], text `FHIR Id`}.
   `period` and `assigner` are **non-null structs whose fields are all null**. The append uses
   `array_union`, so Spark-semantics deduplication of identical elements applies (INF).
5. If a `fhirId` element already exists, the array is only conformed (null -> empty still applies).
6. The id rule runs before the default rules, so the nested rule `identifier.assigner` (C3) rewrites
   `assigner` in each element after the append; the result for the all-null assigner is INF/UNV.

### C2. Reference rewrite (OBS `normalization/reference_normalization.py:13-115`)

Output struct per reference (also per array element), field order: `reference`, `type`, `identifier`,
`display`, `extension`, `id`, `idOrig`, `msftSourceReference`.

- Falsy `msftSourceSystem` -> `ValueError` **before any other check** (:25-27). A non-struct input
  yields an all-null row.
- No `*Orig` column is created for references (`preserve` defaults false); the original string is kept
  in `msftSourceReference`.
- If `reference` is non-empty, two unanchored searches are tried: literal `[base/]Type/id[/_history/v]`
  (hard-coded type list, id `[A-Za-z0-9\-.]{1,64}`) or logical `[base/]Type?identifier=[system|]value`.
  - Literal with **no base URL, or base equal to the source system** (after the same stripping): `id` =
    normalized resource id of (parsed id, type, source system) (algorithm B), `idOrig` = parsed id,
    `type` = existing type or parsed type, and `identifier` is filled only when it has neither value nor
    system (value = id, system = source system, `fhirId` coding). A literal reference with a **foreign**
    base URL gets no `id` (INF from the branch structure).
  - Logical: the identifier is replaced from the query part.
  - **Whenever the incoming `reference` string is non-empty, the output `reference` is NULL** and the
    original text is in `msftSourceReference`, including strings that match neither pattern (`#frag`,
    `urn:uuid:...`). An empty `reference` leaves `msftSourceReference` null.
- `display` passes through; `extension` is a string.

### C3. Reference-typed columns (computed by emulating the generator; run-dir evidence `walk_avsc`)

Rule: any avsc field whose type name ends in `.Reference` (or array of it) at nesting <= 2. Level-1 rules
rewrite the column; level-2 rules rewrite a named child inside a struct or array of struct. Rule order:
refs, then dates, then children (generator :289-343).

| Resource | Level-1 reference columns (struct unless noted) | Nested (column.child) |
|----------|--------------------------------------------------|------------------------|
| Patient | `generalPractitioner` (array), `managingOrganization` | `identifier[].assigner`, `contact[].organization`, `link[].other` |
| Observation | `basedOn`[], `partOf`[], `subject`, `focus`[], `encounter`, `performer`[], `specimen`, `device`, `hasMember`[], `derivedFrom`[] | `identifier[].assigner`, `note[].authorReference` |

(Arrays are marked `[]`; the rest are single struct references. The walk found no reference or date
rules inside `component`.)

The deployed schemas confirm the rewritten layout (OBS, deployed): Reference fields in the order given in
C2. The `identifier` inside a normalized reference uses the full Identifier layout whose own `assigner` is
a *shallow* layout (extension, id, reference, type, an `identifier` of seven string leaves, display).
`identifierOrig` uses the same shallow assigner. Reference `type` is a string.

## D. Dates and timestamps

OBS `normalization/date_normalization.py:9-55`; execution `flatten/core.py` ~170-270.

The normalization function (declared as returning a Spark timestamp):

- timestamp in -> unchanged; date in -> midnight UTC.
- string `YYYY` -> Jan 1 00:00:00.000 UTC; `YYYY-MM` -> first of month UTC; `YYYY-MM-DD` -> midnight UTC.
- string with time (`date[T| ]hh:mm:ss[.f+]tz?`): fractional digits padded to >= 3; 1-6 digits parsed;
  **7+ raise** (INF task failure); `+hh:mm`/`-hh:mm` offsets kept; any other suffix (including `Z` or none)
  is treated as `+0000`. The result is an aware datetime stored by Spark as a UTC instant.
- anything else -> null.

**Which rules apply (generator :328-341; OBS from the walk):** an avsc `int`/`long` with logical type
`date` / `timestamp-millis` / `timestamp-micros`, when the parent is not an array and nesting <= 2, plus a
special-type branch (level 1 only) for Period `start`/`end`, Attachment `creation`, Signature `when`,
Annotation `time`.

| Resource | Top-level date rules (each gets `<name>Orig`) | Dotted rules (inside a struct) |
|----------|-----------------------------------------------|-------------------------------|
| Patient | `birthDate` (avsc date), `deceasedDateTime` (timestamp-micros) | `meta.lastUpdated` (timestamp-millis) |
| Observation | `effectiveDateTime`, `effectiveInstant`, `valueDateTime` (micros); `issued` (millis) | `meta.lastUpdated`; `effectivePeriod.start/end`, `valuePeriod.start/end` (Period special case) |

Details:

- Top-level rule: the original is copied to `<name>Orig` (preserve defaults true), then the column becomes
  the normalized timestamp. A dotted rule on a struct adds `<child>Orig` as the **last field of the
  struct** (deployed: `meta.lastUpdatedOrig`, `effectivePeriod.startOrig/endOrig`) and replaces `<child>`.
- `meta_lastUpdated` is a separate flat column: the first rule copies `meta.lastUpdated`, normalizes it
  and adds `meta_lastUpdatedOrig`. The nested `meta.lastUpdated` is then normalized by its own default
  rule (adding `meta.lastUpdatedOrig`). Both are inert for already-typed timestamps.
- **Types after the rule (OBS deployed):** `birthDate` is a `timestamp` (midnight UTC) while
  `birthDateOrig` is a `date`. For avsc timestamp columns both the column and its `Orig` are `timestamp`.
- **Partial dates and offsets are decided by Spark, not by the Python function.** Because `from_json`
  parses typed date/timestamp fields first (A step 3), the string branches above are not reached for
  these avsc-typed columns. How `from_json` treats `"1980"`, `"1980-04"` or a non-ISO string (null the
  field, null the row, or fail; depends on Spark version and partial-results setting) is **UNV**. Source
  therefore does not support "partial dates take the first day" for `birthDate(Orig)` (section I).
- Precision: Spark timestamps are microseconds. Whether sub-millisecond digits survive for
  `timestamp-millis` avsc fields is **UNV** (the logical type is mapped to the same Spark timestamp type).
  Session time zone use for offset-less strings is **UNV**.

## E. Column layout and `_string` companions

### E1. Physical order (OBS deployed + generator order)

1. `msftModifiedDatetime` (moved to position 0 by the writer, `utils/dataframe_utils.py` ~577-583)
2. `id`, `resourceType`, `msftSourceSystem`, `msftFilePath` (Bronze-derived, A step 2)
3. All remaining avsc fields in avsc order (Patient: 24 more, `extension` first; Observation: 43 more)
4. `meta_lastUpdated`, `meta_lastUpdatedOrig`, `identifierOrig`, `idOrig`
5. Top-level date `*Orig` columns in rule order (Patient: `birthDateOrig`, `deceasedDateTimeOrig`;
   Observation: `effectiveDateTimeOrig`, `effectiveInstantOrig`, `issuedOrig`, `valueDateTimeOrig`)
6. `*_string` columns in current-column order for every array/struct/map column present at that point
   (this includes `identifierOrig_string`, which therefore comes last among them)
7. `msftCreatedDatetime`

Counts (verified against the deployed log): Patient 51 columns (15 `_string`), Observation 89 (32
`_string`). Primitive columns (strings, booleans, ints, decimals, dates, timestamps) get no `_string`.

### E2. `_string` content (OBS `utils/dataframe_utils.py:53-79`)

- Generated by Spark `to_json` over the column **as it is after normalization** (so `meta_string` contains
  `lastUpdatedOrig`; reference columns show the rewritten layout and null `reference`). It is not raw FHIR.
- Option `ignoreNullFields=false`: every schema field is present, nulls written as JSON `null`.
- Field order is schema order (INF). A null top-level column gives a null cell (INF). An empty array gives
  `[]` (INF).
- `identifierOrig_string` serializes the pre-normalization conformed layout (no `id` inside identifier,
  shallow assigner), so it differs in shape from `identifier_string`.
- UNV bytes: timestamp text follows the Spark version's default format and the session time zone; decimals
  follow the JSON generator's decimal text (INF: scientific notation possible for tiny values at scale 18);
  separators without whitespace and non-ASCII escaping follow Jackson defaults (INF).

## F. Lineage and clocks

- `msftSourceSystem`: Bronze path token (B). `msftFilePath`: Bronze `filePath`, Spark `input_file_name()`
  at Bronze ingest (OBS `services/bronze_ingestion_service.py:422`), hence a **URI** of the file at read
  time, not a logical `Files/...` path. UNV: the exact form, and whether the Bronze file-compression/move
  step rewrites it (OBS the compressor handles the path column, `orchestrate/file_movement/file_compressor.py`
  ~43-77; effect on the final value UNV).
- Neither is derived from `meta.source`; both are ingress context.
- `msftModifiedDatetime` and `msftCreatedDatetime`: both are `current_timestamp()` passed through the date
  normalizer (identity on a timestamp) (OBS `utils/dataframe_utils.py:420-452, 566-574`). Spark evaluates
  the current timestamp once per query, so on insert the two are likely equal, at microsecond precision
  (INF). Clock is the cluster wall clock of the merge batch.
- On a matched update `msftModifiedDatetime`, `msftFilePath`, `msftSourceSystem` and every other column
  change; `msftCreatedDatetime` is excluded from the update set and keeps its first value (OBS ~:617-621).

## G. Types and malformed input

OBS from the avsc and deployed schema:

- `extension` is a `string` column, on every nested element struct as well. `contained` is `array<string>`.
  Array/object content of these FHIR elements is therefore captured as **text**, not structure. INF: Spark's
  JSON reader stores a non-string JSON token for a string-typed field as compact JSON text (key order
  preserved, whitespace dropped); numbers may not round-trip byte for byte. Exact output is **UNV**.
- No avsc field names start with `_`: primitive-extension siblings (`_birthDate`) and any other unknown
  element are **dropped** (INF: `from_json` ignores unknown keys). Choice types are only the
  avsc-declared alternatives (e.g. `deceasedBoolean`/`deceasedDateTime`, `multipleBirthBoolean`/`Integer`,
  `effective*`, `value*`).
- `text.div` is a string. Observation `valueQuantity.value` (and similar decimals) is `decimal(36,18)`;
  Spark JSON handling of excess scale/precision (round vs null) is **UNV**.
- `birthDate` is typed `date`; `multipleBirthInteger` is 32-bit. On a type mismatch or overflow, `from_json`
  behavior (null the field, null the whole struct, or fail) depends on Spark version and options (none are
  passed here) and is **UNV**. This is the largest source of row-level divergence risk.
- All Silver columns are nullable at top level in the deployed schema.

## H. Upsert (OBS `utils/dataframe_utils.py:538-632, 769-803`; `flatten/flatten_manager.py` ~107-213)

- Key: `unique_columns = ['id']` (`flatten/constants.py`), the **normalized** id. The merge key does not
  include `msftSourceSystem`; the source system only distinguishes rows via the id hash.
- Batch reduction (`prepare_source_data`, row_number at ~:800): window over `id`, order by
  `meta_lastUpdated` descending, keep row 1. **No tie-breaker**, so ties inside one batch are
  nondeterministic (INF). Spark descending order puts nulls last, so a non-null row wins over a null one
  (INF).
- Matched rows update only when `target.meta_lastUpdated < source.meta_lastUpdated` (strict). Equal or
  older leaves the row unchanged. A null source value makes the comparison null, so no update (INF).
- Not matched: insert only if `source.meta_lastUpdated is not null`. A new resource with no `lastUpdated`
  is silently not inserted; an existing one is not updated. There is no error (OBS).
- Update set = every column of the source frame except `msftCreatedDatetime`, including the modified time
  and lineage columns.
- Table creation: `get_or_create_managed_delta_table_using_path` runs before the emptiness check and
  creates the table from the frame schema by an empty overwrite if the path has no Delta table
  (`:231-244`). Silver tables are also pre-created from the avsc plus nullable `msftSourceSystem` /
  `msftFilePath` string columns (`flatten/flatten_manager.py` ~178-213). The merge itself sets no
  schema-merge option; schema evolution is a session setting.
- **Table properties are session defaults, not code (OBS):** the packaged config notebook sets default
  change-data-feed and schema auto-merge to true; the V-Order property in the deployed log comes from
  the Fabric runtime (HDS code only logs V-Order settings). Deployed v0: protocol reader 1 / writer 4,
  `delta.enableChangeDataFeed=true`, `delta.parquet.vorder.enabled=true` (OBS deployed). A native writer
  must set these itself; Parquet file layout is not part of the observable row contract.
- The merge is per micro-batch and per resource; there is no cross-table transaction (OBS).

## I. Cross-check of depot claims (depot ref `5ff5d76`; read only after sections A-H)

| # | Depot claim | Verdict | Evidence |
|---|-------------|---------|----------|
| 1 | `id` = lowercase hex SHA-256 of namespace (scheme stripped, one trailing slash removed) + `/` + Type + `/` + id; `idOrig` keeps original | **AGREE** on the algorithm; **DISAGREE** on the premise that the namespace is a URL | B: stock HDS derives a directory token from the input path, so URL-form namespaces (as in the depot sample) are not reachable through the stock binding |
| 2a | `meta_lastUpdatedOrig` = parsed timestamp; `meta_lastUpdated` normalized, timestamps unchanged | **AGREE** | D, E1 |
| 2b | `birthDateOrig` = parsed date; `birthDate` = midnight UTC timestamp | **AGREE** (types confirmed by deployed layout) | D |
| 2c | Partial dates take the first day | **UNVERIFIABLE, leaning DISAGREE** | D: Spark parses typed date fields before the Python normalizer, so partial strings never reach its string branches |
| 3 | `identifierOrig` = pre-normalization; identifier conformed and a source-id element appended (value = original id, system = namespace) unless a `fhirId` coding exists | **AGREE**, with extra rules | C1: no `id` in the layout, non-null all-null `period`/`assigner`, `array_union` dedupe, null -> empty, failures swallowed |
| 4a | `_string` serializes the final Silver value, explicit null fields, schema order, top-level null -> null | **AGREE** | E2 |
| 4b | Timestamps as UTC millisecond text; compact separators | **UNVERIFIABLE** | E2: Spark format and session time zone are INF only |
| 4c | Patient `extension` is a plain STRING column | **AGREE** | G |
| 5a | `msftSourceSystem`/`msftFilePath` are ingress context, not `meta.source` | **AGREE** for not `meta.source`; **DISAGREE** that the source system is caller-supplied | B, F: path-segment token extracted at Bronze |
| 5b | `msftFilePath` uses `input_file_name()`; logical vs OneLake URI unconfirmed | **AGREE** (mechanism); exact form **UNVERIFIABLE** | F |
| 6a | Merge key = normalized `id`; strict `<` on `meta_lastUpdated`; newer rows keep created time, change modified time and lineage; equal/older unchanged | **AGREE** | H |
| 6b | HDS null `lastUpdated` is not inserted | **AGREE**; a matched null source is also not updated | H |
| 6c | Depot null/tied-version failures are stricter than HDS | **AGREE** (HDS never fails here; in-batch ties are nondeterministic) | H |

The depot Patient column list was compared by **names and order only** (51 columns) against the deployed
log; no depot contract content is reused here. The depot is removing its Python shaper/writer and keeping
views/contracts for 146 R4 types; nothing in this document relies on either.

## J. Residual capture questions (only a real HDS run settles these)

Feed to the depot capture recipe (initial, replay, update events):

1. `from_json` on a partial date (`1980`, `1980-04`) in `birthDate`, offset-less and offset timestamps, a
   bad decimal, an out-of-range int: null field vs null row vs failure; Spark version and partial-results
   setting in the Fabric runtime.
2. `_string` bytes: timestamp text and time zone, decimal text (including values below 1e-6), escaping of
   non-ASCII/control characters, `extension`/`contained` text round-trip.
3. `msftFilePath` exact value (OneLake URI form; effect of Bronze compression/move) and `msftSourceSystem`
   for a path with extra segments or no match (and the resulting reference failure).
4. `msftModifiedDatetime`/`msftCreatedDatetime`: equality on insert, storage precision, behavior on replay
   and update.
5. `array_union` dedupe on identical identifiers; shape of the appended identifier's `assigner` after the
   nested reference rule (all-null struct vs null).
6. In-batch tie-break between two rows with equal `meta_lastUpdated`.
7. Created-table protocol and properties when created by the packaged path versus a merge into an empty
   path (CDF, V-Order, writer version).
8. Whether a reference with a foreign base URL, `#frag` or `urn:uuid:` yields exactly `reference = null`,
   `msftSourceReference = <original>`, `id = null`.
9. Whether real deployments bind the id namespace to a path token or a configured URL.

## Tradeoffs

| Choice | Cost | Benefit |
|--------|------|---------|
| Reproduce Spark `from_json` typing in the writer | Explicit policies where Spark behavior is UNV (partial dates, overflow) | Deterministic and testable without Spark |
| Treat source system as a caller-supplied token | Callers must choose a stable token per source | Matches stock HDS id hashing; also allows URL-form sources |
| Claim schema parity only today | No row-level claim possible | Honest scope until a capture exists |

## Alignment checklist

- [x] Behavior described in own words; no HDS code, schema text or avsc excerpts included
- [x] Evidence class stated for each rule (OBS / INF / UNV)
- [x] Scope limited to Patient and Observation
- [x] No row-level parity claim (no HDS-captured oracle exists)
- [x] Licensing of HDS-derived artifacts left unresolved (issue #550 decision 5)

## Evidence

- Source: HDS v1.4.0 package paths cited inline (`flatten/`, `flatten/normalization/`, `services/`, `utils/`).
- Computed: the rule lists in C3 and D come from emulating the generator over the shipped avsc; the
  throwaway script and output live in the run directory, not in this repository.
- Deployed schema v0 (local only): field order, types, nullability, nested layouts, protocol and table
  properties for `Patient` and `Observation`.

## Verdict

Patient/Observation row semantics are pinned from source except where Spark's JSON typing decides the
outcome (section J). A native writer can reproduce column layout, `id`/`idOrig`, identifier append,
reference rewrite, `*Orig` columns, `_string` structure and the upsert rules. It cannot claim row parity
until an HDS-captured oracle exists; until then only schema parity (names, order, types, nullability,
protocol, properties) is checkable.
