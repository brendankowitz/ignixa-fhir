# SQL on FHIR CLI Enhancements

**Date:** 2026-05-11
**Branch:** `feat/sqlonfhir-cli-enhancements`
**Inspired by:** [flatquack](https://github.com/gotdan/flatquack)

---

## Goal

Extend the `ignixa-sqlonfhir` CLI from a single-file tool to a batch-capable analytics pipeline tool. The primary use case is applying a directory of ViewDefinitions to a directory of flat FHIR (NDJSON) files in one command.

---

## Command Surface

Three commands — `run`, `preview`, `validate` — replace and extend the existing `convert`, `preview`, and `validate` commands. All three use identical flag names and auto-detect single vs batch mode based on whether `--views` points to a file or a directory.

```
ignixa-sqlonfhir <version> run
  --views <file|dir>           ViewDefinition file or directory
  --input <file|dir>           NDJSON file or directory
  --out <file|dir>             Output file (single mode) or directory (batch mode)
  [--format parquet|csv|ndjson]  Batch mode only: output format (default: parquet). Ignored in single mode; use the --out file extension instead.
  [--pattern <glob>]           ViewDefinition discovery glob, batch mode only (default: **/*.json)
  [--input-pattern <glob>]     NDJSON match pattern, batch mode only; {resource} is substituted (default: *{resource}*.ndjson)
  [--var name=value]...        FHIRPath variables, repeatable
  [--quiet]                    Suppress all console output
  [--stats-out <file>]         Write JSON stats summary to file

ignixa-sqlonfhir <version> preview
  --views <file|dir>           ViewDefinition file or directory
  [--input <file|dir>]         NDJSON file or directory (optional; omit for schema-only)
  [--rows N]                   Sample rows to display (default: 5)
  [--pattern <glob>]           ViewDefinition discovery glob (default: **/*.json)

ignixa-sqlonfhir <version> validate
  --views <file|dir>           ViewDefinition file or directory
  [--pattern <glob>]           ViewDefinition discovery glob (default: **/*.json)
```

`run` replaces `convert`. The existing `convert` command is removed; the single-file usage pattern maps directly to `run` with file paths.

In single-file mode, `--format` is ignored — the output format is detected from the `--out` file extension (`.parquet`, `.csv`, `.ndjson`).

---

## Auto-Detection: Single vs Batch Mode

| `--views` path | Mode |
|----------------|------|
| Points to a file | Single mode |
| Points to a directory | Batch mode |

In batch mode:
- `--input` must be a directory (if provided)
- `--out` must be a directory
- `--format` determines output file extension (default: `parquet`)

---

## Batch Mode: ViewDefinition Discovery

ViewDefinition files are discovered under `--views` using the `--pattern` glob (default `**/*.json`). Files are processed in lexicographic order for deterministic output. The same `--pattern` flag applies identically in `preview` and `validate` batch mode.

---

## Batch Mode: Resource-to-NDJSON Matching

1. Extract the `resource` field from each ViewDefinition (e.g., `Patient`).
2. Search the `--input` directory for files matching `--input-pattern` with `{resource}` substituted (case-insensitive). Default: `*{resource}*.ndjson`.
3. If multiple files match (e.g., sharded bulk export `Patient_1.ndjson`, `Patient_2.ndjson`), all are read in lexicographic order as a single logical stream.
4. If no files match: emit a warning, skip the ViewDefinition, continue the batch.

---

## Batch Mode: Output File Naming

Output file name = ViewDefinition basename + format extension.

Example: `patient-demographics.json` → `patient-demographics.parquet` written to `--out` directory.

---

## New Capabilities

### NDJSON Output Format

Add `.ndjson` to single-mode extension detection and `ndjson` as a valid `--format` value for batch mode.

Output is one flat JSON object per line — the flattened ViewDefinition result row, not hierarchical FHIR. Implemented as `NdjsonFileWriter` alongside existing `CsvFileWriter` and `ParquetFileWriter`.

### FHIRPath Variables (`--var`)

Repeatable `name=value` flag. Parsed at startup (malformed inputs fail fast before any processing). Passed to `SqlOnFhirEvaluator` as `IReadOnlyDictionary<string, object>`. Accessible in ViewDefinition FHIRPath expressions as `%name`. Applied uniformly to all ViewDefinitions in a batch.

### Progress & Stats (batch `run`)

One line per ViewDefinition as it completes:

```
[1/8] patient-demographics  →  12,441 rows  →  patient-demographics.parquet (2.1 MB)  0.4s
[2/8] conditions            →  87,302 rows  →  conditions.parquet (8.9 MB)  1.2s
```

`--quiet` suppresses all console output. `--stats-out <file>` writes a JSON summary regardless of `--quiet`:

```json
{
  "total": 8,
  "completed": 7,
  "skipped": 1,
  "durationSeconds": 4.2,
  "views": [
    { "name": "patient-demographics", "status": "completed", "rows": 12441, "bytes": 2202624, "durationSeconds": 0.4 },
    { "name": "unknown-resource", "status": "skipped", "reason": "No matching NDJSON input found" }
  ]
}
```

`validate` batch output: summary table — name, status (✓/✗), column count, error message if any. Exit code 1 if any ViewDefinition fails validation or if all ViewDefinitions are skipped (none successfully parsed).

---

## Error Handling

| Situation | Behaviour |
|-----------|-----------|
| No NDJSON match for a ViewDefinition | Warn + skip; continue batch |
| ViewDefinition parse failure | Warn + skip; continue batch |
| Row evaluation error on a specific resource | Log row index + error; skip row; continue file |
| Output write failure (disk full, bad path) | Abort entire batch; exit 1 |
| All ViewDefinitions skipped | Exit 1 |
| Malformed `--var` (no `=`, empty name/value) | Fail fast at startup before any processing |

Single mode: unchanged from today — any error exits 1 with message and stack trace.

---

## Implementation Breakdown

### New files

| File | Purpose |
|------|---------|
| `tools/Ignixa.SqlOnFhir.Cli/Commands/RunCommand.cs` | Replaces `ConvertCommand`; handles single + batch |
| `tools/Ignixa.SqlOnFhir.Cli/Batch/BatchProcessor.cs` | Directory scan, resource matching, output routing |
| `tools/Ignixa.SqlOnFhir.Cli/Batch/BatchResult.cs` | Result model for stats/reporting |
| `src/Core/Ignixa.SqlOnFhir.Writers/NdjsonFileWriter.cs` | New writer alongside Csv/Parquet |
| `tools/Ignixa.SqlOnFhir.Cli/VarParser.cs` | `--var` flag parsing |

### Modified files

| File | Change |
|------|--------|
| `tools/Ignixa.SqlOnFhir.Cli/Program.cs` | Register `run`; remove `convert`; extend `preview` + `validate` |
| `tools/Ignixa.SqlOnFhir.Cli/Commands/PreviewCommand.cs` | Add `--input` optional, `--pattern`, dir detection |
| `tools/Ignixa.SqlOnFhir.Cli/Commands/ValidateCommand.cs` | Add `--pattern`, dir detection, summary output |

### Removed files

| File | Reason |
|------|--------|
| `tools/Ignixa.SqlOnFhir.Cli/Commands/ConvertCommand.cs` | Superseded by `RunCommand` |

---

## Testing

### Unit tests

- **`BatchProcessorTests`** — resource matching (multi-file sharding, case-insensitive, missing match returns warning not exception), output path derivation (basename + format), glob pattern filtering. No real file I/O.
- **`NdjsonFileWriterTests`** — write rows, flush, verify output is valid NDJSON (one object per line).
- **`VarParserTests`** — valid `name=value`, malformed inputs (no `=`, empty name, empty value), duplicate keys.

### Integration tests (`Ignixa.Tests.Compatibility.CLI`)

Small fixture directory: 2–3 ViewDefinition files + matching NDJSON fixture files.

- `run` dir mode → correct output files produced
- `run` dir mode with one unmatched VD → others complete, exit 0, warning emitted
- `preview` schema-only (no `--input`) → schema printed, no error
- `preview --input` dir → schema + sample rows for each VD
- `validate` dir mode → summary table, exit code reflects pass/fail

No mocking of the evaluator — integration tests run against the real evaluator and existing test fixtures.

---

## Out of Scope

- dbt model output template (deferred to follow-up)
- Streaming from stdin / piped NDJSON
- Parallel batch execution (sequential for now; parallelism is an optimization)
