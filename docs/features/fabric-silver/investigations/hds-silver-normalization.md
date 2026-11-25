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
with `config/` (then `HDS.SourceCode/src/config/fhir4/`), `deployed/` (the deployed Silver
`_delta_log` v0 under `HDS.SourceCode/hds-build-artifacts/healthcare-tables/1.4.0/Silver/<Table>/`,
read locally for field order and types only; no schema text is reproduced) or `root:` (relative to
`HDS.SourceCode/`). Every line number below was re-checked against the package.

**Reproducibility.** The per-resource rule lists (C3, D) and the column counts are *computed* by
emulating the HDS rule generator over the Patient/Observation `.avsc` files and by reading the deployed
Delta log. Both inputs come from the HDS v1.4.0 package, which is supplied by the caller and is not part
of this repository (licensing is unresolved). The emulation script and its output are kept in the
run directory, not committed. Someone with the same package can re-derive the lists by running the
generator logic at `flatten/flatten_config_generator.py:143-343` over those two files; someone without
it can only trust the tables below.

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
| 1 | Silver ingestion processes Bronze data per micro-batch (`epochId`) and per `resourceType`; one parse + transformation call per resource | OBS `services/silver_ingestion_service.py:576-614` |
| 2 | Select Bronze `id`, `resourceType`, content, `sourceSystem` (renamed `msftSourceSystem`) and `filePath` (renamed `msftFilePath`) | OBS same file :537-544 |
| 3 | `from_json(content, <Spark schema converted from the Silver .avsc>)`, **no options**; parsed fields are lifted to top level, skipping names already present (so `id`/`resourceType` stay the Bronze values) | OBS same file :549-559; schema loader `utils/utils.py:114-139` (Avro-to-Spark schema converter) |
| 4 | Patient and Observation entries in the resource config name only their `.avsc`, no column-schema file, so the flatten config is auto-generated from the avsc | OBS `config/transformation/flatten/flatten_config.json:5-8, 17-20`; `flatten/core.py:302-329`; `flatten/flatten_config_generator.py:143-165` |
| 5 | Column step: only adds `meta_lastUpdated` (copy of `meta.lastUpdated`); source columns retained | OBS generator :159; `flatten/core.py:386-392` |
| 6 | Normalization: date `meta_lastUpdated`; id rule (also drives `identifierOrig`); then default rules (reference, date, nested) | OBS generator :161-163; `flatten/core.py:394-399` calling `__process_normalization` (:166-280) |
| 7 | Stringify complex columns into `*_string` | OBS `flatten/core.py:401-403`; `utils/dataframe_utils.py:53-79` |
| 8 | Stamp `msftModifiedDatetime` then `msftCreatedDatetime`, move modified time to column 0, get-or-create table, dedupe per `id`, merge | OBS `utils/dataframe_utils.py:566-633` (stamps :566-575, reorder :582-584, table :586, dedupe :605, merge :613-633) |

Two consequences matter for a native writer:

1. Spark type coercion happens **first** (step 3). The avsc types decide whether a date arrives as a
   date, a timestamp or a string. The Python normalizers then only see Spark-typed values.
2. Rule order inside normalization is fixed (step 6), and it is what produces the physical column
   order (section E).

Normalization **failures are partly swallowed**: identifier normalization raises `ValueError` on a
missing column or schema mismatch, and `flatten/core.py:219-230` only logs it and continues, so `id`
can still be normalized (the id expression is applied at :269-277 regardless) while `identifierOrig` and
the appended identifier may be skipped (OBS). Reference normalization is the opposite: nested-reference
failures are logged and re-raised (`flatten/core.py:253-256`) and the null-source-system check raises
inside the row function (section C), so the task fails.

## B. `id` / `idOrig`

OBS `flatten/normalization/identifier_normalization.py:26-43` (hash), `flatten/normalization/utils.py:12-36`
(scheme and trailing-slash stripping, returns null when nothing is left).

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
called from the Bronze ingestion, `services/bronze_ingestion_service.py:422-430`), and sets it null
when the pattern does not match. The function overwrites the column unconditionally even though its
docstring says "update if empty". For a path such as `.../Process/Clinical/FHIR-NDJSON/east/...` the value
is the token `east`. No other code in the package sets it (grep of the package: only this derivation and
the Silver rename). Consequences:

- The scheme/trailing-slash stripping is a no-op for stock input; id hashing uses a bare token.
- Null `msftSourceSystem` (path did not match) is legal for ids (hash of `<Type>/<id>` alone) but
  **fails reference normalization** (section C): the check at `reference_normalization.py:25-27` runs
  before any other test, so it applies even to rows with no references at all, whenever a reference rule
  applies to the table (always true for Patient and Observation). Whether Spark invokes the Python function
  on null input, and so whether it fires for a missing reference, is INF (Spark calls Python UDFs with
  `None`).

## C. Identifier and reference normalization

### C1. `identifier` / `identifierOrig` (OBS `identifier_normalization.py:241-457`, `normalization/utils.py:97-138`)

1. `identifierOrig` = copy of the parsed identifier array **before** any change. Null stays null.
2. Schema check: the column must match a fixed Identifier layout (`flatten/constants.py:166-175`
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
6. **Appended assigner after the nested rule.** The id rule runs before the default rules, so the nested
   rule `identifier[].assigner` (C3) then runs over every element, including the appended one. Two
   separate facts:
   - *Layout (OBS from the deployed schema):* every `identifier[].assigner` ends up with the normalized
     Reference layout of C2 (reference, type, identifier{...}, display, extension, id, idOrig,
     msftSourceReference), whose inner `identifier.assigner` is the shallow layout.
   - *Values for the appended element (INF, uncaptured):* the input is a non-null struct of nulls, which
     the row function treats as a struct (`reference_normalization.py:33-44`), so the expected result is a
     non-null Reference-layout struct whose fields are all null, with a non-null all-null `identifier`
     (:31, :72). No captured row shows this; see J.

### C2. Reference rewrite (OBS `normalization/reference_normalization.py:13-114`; wrappers :118-194)

Output struct per reference (also per array element), field order: `reference`, `type`, `identifier`,
`display`, `extension`, `id`, `idOrig`, `msftSourceReference` (:107-114).

**Missing, empty and present inputs (the distinction a writer most easily gets wrong):**

| Input to the row function | Output | Evidence |
|---|---|---|
| Falsy `msftSourceSystem` (any input) | `ValueError`, task fails | OBS :25-27 |
| Not a struct/dict, e.g. a **missing scalar Reference** (SQL null) | A **non-null** Reference-layout struct with every field null, including a **non-null `identifier` struct whose fields are all null** (not conformed) | OBS :31-42 for the row function; that Spark hands SQL null to the function as `None` and keeps the returned struct is INF |
| Present struct, `reference` empty/null | Passes `type`, `display`, `extension` through, conforms the identifier (:72); nothing to clear, `msftSourceReference` null, `id`/`idOrig` null | OBS :44-72, :107-114 |
| Present struct, `reference` non-empty | See below | OBS :75-105 |
| **Missing array** of References (null) or empty array | Unchanged: stays null / stays empty | OBS :118-126 |
| Array with elements | Each element goes through the row function; a null element becomes the all-null struct above | OBS :124 (element behavior INF) |
| Nested rule: parent struct null | Parent returned unchanged (null); a non-null parent with a missing child gets the all-null Reference struct in that child | OBS :145-160 |
| Nested rule: parent array | Null or empty parent array unchanged; each element's child normalized, so a missing child becomes the all-null struct | OBS :130-142, :182-194 |

So a **missing scalar Reference column becomes a non-null all-null struct while a missing array stays
null**, which changes `*_string` (E2) and the native column.

For a non-empty `reference`, `type_` is first set to the existing `type` if present, otherwise the type
parsed from the string (:77). Two unanchored searches are then tried
(`normalization/utils.py:39-70`): literal `[base/]Type/id[/_history/v]` (hard-coded type list, id
`[A-Za-z0-9\-.]{1,64}`) and logical `[base/]Type?identifier=[system|]value`.

- **Literal** (:78-89): applies only when there is **no base URL, or the base equals the source system**
  after scheme/trailing-slash stripping. Then:
  - `id` = SHA-256 id of (source system, **`type_`**, parsed id), where `type_` is the **existing
    `Reference.type` if one is set, even when it disagrees with the type in the string**; so
    `Patient/123` with `type = Organization` hashes `.../Organization/123` (:77, :83-85), and the output
    `type` is that same value.
  - `idOrig` = parsed id.
  - `identifier` goes through the source-id fill (:87-89 -> `identifier_normalization.py:76-148`): unchanged
    if a `fhirId` coding already exists, or if the identifier already has a value or system; otherwise
    value = parsed id, system = source system and a `fhirId` coding appended (or a new `type` created).
  - A literal with a **foreign base URL** leaves `id`, `idOrig` and `identifier` untouched (identifier
    only conformed at :72).
- **Logical** (:92-101): **never sets `id` or `idOrig`**. Only the identifier changes, and only in three
  disjoint cases, in this order:
  1. no base URL **and** no `system` in the query (:94-95): value = query value, system = **source
     system**;
  2. base URL equals the source system after stripping, **or** no base URL but the query has a `system`
     (:96-99): value = query value, system = **query system** (null if the base matched but the query had
     no `system|` part);
  3. anything else, e.g. a **foreign base URL** (:101): identifier unchanged (only conformed).
  Cases 1-2 overwrite `value` and `system` and keep `extension`, `use`, `type`, `period` and `assigner`
  from the existing identifier (`normalization/utils.py:140-150`).
- **Whenever the incoming `reference` is non-empty, the output `reference` is NULL** and the original
  text is in `msftSourceReference` (:103-105), including strings matching neither pattern (`#frag`,
  `urn:uuid:...`), for which nothing else changes. An empty `reference` leaves `msftSourceReference` null.
- No `*Orig` column is created for references (`preserve` defaults false, `flatten/core.py:236`).
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

OBS `normalization/date_normalization.py:11-55`; execution `flatten/core.py:193-202` (date rule; dotted
form :195-200) inside `__process_normalization` (:166-280).

The normalization function (declared as returning a Spark timestamp):

- timestamp in -> unchanged; date in -> midnight UTC.
- string `YYYY` -> Jan 1 00:00:00.000 UTC; `YYYY-MM` -> first of month UTC; `YYYY-MM-DD` -> midnight UTC.
- string with time (`date[T| ]hh:mm:ss[.f+]tz?`): fractional digits padded to >= 3; 1-6 digits parsed;
  **7+ raise** (INF task failure); `+hh:mm`/`-hh:mm` offsets kept; any other suffix (including `Z` or none)
  is treated as `+0000`. The result is an aware datetime. That Spark stores it as a UTC instant (and what
  the Parquet/Delta file records) is INF from standard Spark behavior; the deployed log shows only the
  column type.
- anything else -> null.

**Which rules apply (generator :319-341; OBS from the computed walk):** an avsc `int`/`long` with logical type
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

1. `msftModifiedDatetime` (moved to position 0 by the writer, `utils/dataframe_utils.py:582-584`)
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
- Field order is schema order (INF). An empty array gives `[]` (INF).
- **Null handling depends on what normalization left in the column, not on whether the source was missing
  (INF, uncaptured; see J):**
  - A column that is still SQL null after normalization gives a null cell. This covers a **missing array
    of References** (`generalPractitioner`, `hasMember`, ... stay null, C2) and non-reference columns such
    as a missing `text` or `meta`.
  - A **missing scalar Reference** (`managingOrganization`, Observation `subject`, `encounter`, `specimen`,
    `device`) is a non-null all-null struct after C2, so its `_string` is a JSON **object** with every
    field written as `null` (including a nested all-null `identifier` object), **not** a null cell. The same
    applies to the appended `identifier` element's `assigner`.
  - `identifier_string` is never null for a row that went through the id rule: a null `identifier` array
    becomes empty then gets the appended `fhirId` element (C1). `identifierOrig_string` follows
    `identifierOrig`, which keeps null.
- `identifierOrig_string` serializes the pre-normalization conformed layout (no `id` inside identifier,
  shallow assigner), so it differs in shape from `identifier_string`.
- UNV bytes: timestamp text follows the Spark version's default format and the session time zone; decimals
  follow the JSON generator's decimal text (INF: scientific notation possible for tiny values at scale 18);
  separators without whitespace and non-ASCII escaping follow Jackson defaults (INF).

## F. Lineage and clocks

- `msftSourceSystem`: Bronze path token (B). `msftFilePath`: Bronze `filePath`, Spark `input_file_name()`
  at Bronze ingest (OBS `services/bronze_ingestion_service.py:422`), hence a **URI** of the file at read
  time, not a logical `Files/...` path. UNV: the exact form, and whether the Bronze file-compression/move
  step rewrites it (OBS the compressor rewrites the path column with `withColumn` at
  `orchestrate/file_movement/file_compressor.py:43` and `:75`; the resulting value is UNV).
- Neither is derived from `meta.source`; both are ingress context.
- `msftModifiedDatetime` and `msftCreatedDatetime`: both are `current_timestamp()` passed through the date
  normalizer (identity on a timestamp) (OBS `utils/dataframe_utils.py:406-452`, stamps :570 and :574).
  Spark evaluates the current timestamp once per query, so on insert the two are likely equal, at
  microsecond precision (INF). Clock is the cluster wall clock of the merge batch.
- On a matched update `msftModifiedDatetime`, `msftFilePath`, `msftSourceSystem` and every other column
  change; `msftCreatedDatetime` is excluded from the update set and keeps its first value (OBS
  `utils/dataframe_utils.py:623-624`).

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

## H. Upsert (OBS `utils/dataframe_utils.py:538-633, 769-803`; `flatten/flatten_manager.py:55-56, 121-133, 178-213`)

- Key: `unique_columns = ['id']` (`flatten/constants.py:210`; defaults also `flatten/flatten_manager.py:55-56`
  and `global_constants/global_constants.py:132`), the **normalized** id. The merge key does not
  include `msftSourceSystem`; the source system only distinguishes rows via the id hash. The merge
  condition is at `dataframe_utils.py:613`.
- Batch reduction (`prepare_source_data`, called at :605, defined :769-803): window over `id`, order by
  `meta_lastUpdated` descending (:797, `row_number` :800), keep row 1. The ordering column default is
  `global_constants.py:133`. **No tie-breaker**, so ties inside one batch are nondeterministic (INF).
  Spark descending order puts nulls last, so a non-null row wins over a null one (INF).
- Matched rows update only when `target.meta_lastUpdated < source.meta_lastUpdated` (strict, :617). Equal
  or older leaves the row unchanged. A null source value makes the comparison null, so no update (INF).
- Not matched: insert only if `source.meta_lastUpdated is not null` (:620). A new resource with no
  `lastUpdated` is silently not inserted; an existing one is not updated. There is no error (OBS).
- Update set = every column of the source frame except `msftCreatedDatetime`, including the modified time
  and lineage columns (:623-624).
- Table creation: `get_or_create_managed_delta_table_using_path` (:246, called at :586) runs before the
  emptiness check and creates the table from the frame schema by an empty overwrite if the path has no
  Delta table (:231-244). Packaged table creation (`flatten/flatten_manager.py:178-192`) runs the
  **same flatten and upsert path on an empty dataframe** (:190-191), built from the avsc schema plus null
  string `msftSourceSystem` / `msftFilePath` (:194-213). So the created layout is the pipeline's output
  layout, not a separate pre-created schema (OBS). The merge itself sets no schema-merge option; schema
  evolution is a session setting.
- **Table properties are session defaults, not code (OBS unless marked):**
  - The packaged config notebook sets the session defaults `schema.autoMerge.enabled` and
    `properties.defaults.enableChangeDataFeed` to the exact string `'True'`
    (`root:hds-build-artifacts/healthcare-artifacts/1.4.0/relational-fhir-data-foundations/Notebooks/msft_config_notebook.ipynb`
    lines 262-263; the unpackaged `root:notebooks/msft_config_notebook.ipynb` lines 276-277 match).
  - The deployed v0 log records `delta.enableChangeDataFeed` as `'True'` (capitalized, as set by the
    notebook), `delta.parquet.vorder.enabled` as the lowercase string `'true'`, and protocol reader 1 /
    writer 4 (OBS deployed). Case matters if a native writer wants byte-identical table properties.
  - **V-Order source is INF:** no HDS code sets it; `utils/logging_utils.py:128-130` only logs the V-Order
    settings, so the property most likely comes from the Fabric runtime default.
  A native writer must set these itself; Parquet file layout is not part of the observable row contract.
- The merge is per micro-batch and per resource; there is no cross-table transaction (OBS).

## I. Cross-check of depot claims (depot ref `5ff5d76`; read only after sections A-H)

| # | Depot claim | Verdict | Evidence |
|---|-------------|---------|----------|
| 1 | `id` = lowercase hex SHA-256 of namespace (scheme stripped, one trailing slash removed) + `/` + Type + `/` + id; `idOrig` keeps original | **AGREE** on the algorithm; **DISAGREE** on the premise that the namespace is a URL | B: stock HDS derives a directory token from the input path, so URL-form namespaces (as in the depot sample) are not reachable through the stock binding |
| 2a | `meta_lastUpdatedOrig` = parsed timestamp; `meta_lastUpdated` normalized, timestamps unchanged | **AGREE** | D, E1 |
| 2b | `birthDateOrig` = parsed date; `birthDate` = midnight UTC timestamp | **AGREE** (types confirmed by deployed layout) | D |
| 2c | Partial dates take the first day | **UNVERIFIABLE, leaning DISAGREE** | D: Spark parses typed date fields before the Python normalizer, so partial strings never reach its string branches |
| 3 | `identifierOrig` = pre-normalization; identifier conformed and a source-id element appended (value = original id, system = namespace) unless a `fhirId` coding exists | **AGREE**, with extra rules | C1: no `id` in the layout, non-null all-null `period`/`assigner`, `array_union` dedupe, null -> empty, failures swallowed |
| 4a | `_string` serializes the final Silver value, explicit null fields, schema order, top-level null -> null | **PARTIAL / UNVERIFIABLE** | E2: holds for columns that stay SQL null (missing arrays, non-reference structs); a **missing scalar Reference** becomes a non-null all-null struct (C2, INF), so its `_string` is a JSON object of nulls, not a null cell |
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
5. `array_union` dedupe on identical identifiers; values of the appended identifier's normalized
   `assigner` (expected: non-null Reference-layout struct of nulls; layout is OBS, values INF).
6. In-batch tie-break between two rows with equal `meta_lastUpdated`.
7. Created-table protocol and properties when created by the packaged path versus a merge into an empty
   path (CDF `'True'`, V-Order `'true'`, writer version) and where V-Order is set.
8. Whether a reference with `#frag` or `urn:uuid:` yields exactly `reference = null`,
   `msftSourceReference = <original>`, `id = null`.
9. Whether real deployments bind the id namespace to a path token or a configured URL.
10. **Missing scalar Reference:** Patient without `managingOrganization` and Observation without
    `subject`: is the native column a non-null all-null struct, and is `managingOrganization_string` a JSON
    object of nulls (with nested all-null `identifier`) or a null cell? Capture one row with the scalar
    missing and one with `generalPractitioner` / `hasMember` missing (expected null cell).
11. **Logical references:** one row for each of the three cases (no base and no system; base equal to
    source system; no base with system) plus a foreign-base logical reference: confirm `id`/`idOrig`
    null, identifier value/system per C2, other identifier fields kept.
12. **Existing `Reference.type` differs from the type in the string** (e.g. `Patient/123` with
    `type = Organization`): confirm `id` hashes the existing type and the output `type`.
13. **Foreign-base literal reference:** confirm `id`/`idOrig` null and identifier untouched.
14. **Null source system** on a row with no references at all: confirm the task fails.

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
  throwaway script and output live in the run directory, not in this repository. They are reproducible
  only from the caller-supplied HDS package (see Approach); the package is not part of this repository.
- Deployed schema v0 (local only): field order, types, nullability, nested layouts, protocol and table
  properties for `Patient` and `Observation`.

## Verdict

Patient/Observation row semantics are pinned from source except where Spark's JSON typing decides the
outcome (section J). A native writer can reproduce column layout, `id`/`idOrig`, identifier append,
reference rewrite, `*Orig` columns, `_string` structure and the upsert rules. It cannot claim row parity
until an HDS-captured oracle exists; until then only schema parity (names, order, types, nullability,
protocol, properties) is checkable.
