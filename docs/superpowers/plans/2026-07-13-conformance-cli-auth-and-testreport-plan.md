# Conformance CLI Auth Header and FHIR Output — As-Built

> **For agentic workers: DELIVERED — do not implement this plan.** The work shipped in PR #342
> (`695a65b5`, `88c13004`, `61e22c78`, `ee223860`). This file has been rewritten to describe the
> design **as built**, because what shipped diverged substantially from what was originally planned.
> Read it as a record of decisions and their reasons, not as a task list. The paired design doc is
> `docs/superpowers/specs/2026-07-13-conformance-cli-auth-and-testreport-design.md`.

**Goal:** Add authentication support to `ignixa-matrix run` and make FHIR `TestReport` its default
output.

**Tech Stack:** C#, .NET 10, System.CommandLine 2.0.1, Ignixa.TestScript, System.Text.Json.Nodes

---

## How the shipped design differs from the original plan

The original plan proposed a `--test-report <path>` option alongside `--out`. **That is not what
shipped and should not be reintroduced.** Two output paths can disagree with each other; `--out` is
now the single report file and `--format` selects its shape.

| Original plan | As built |
|---|---|
| `--test-report <path>` as a second output path | `--format <fhir\|json>`; `--out` is the only path |
| Matrix JSON was the only/default output | `fhir` is the **default**; `json` is the matrix shape |
| Bare `TestReport` for one script, `Bundle` for many | **Always** a `Bundle` |
| `ParseAuthHeader` hardcodes `Bearer`/`Basic`/`Digest` | No scheme list — see below |

---

## Decisions and their reasons

Each of these was a real bug found in review. Re-introducing any of them repeats a defect.

### `--format`, not a second output path

`--format fhir` (default) writes a `Bundle` (`type: collection`) of `TestReport` resources.
`--format json` writes the native `ImplReport` — the shape `merge` deserializes. **`merge` only
consumes `json`**; handed a Bundle it fails loudly (`JsonException`, non-zero exit) rather than
producing a wrong matrix. Any pipeline feeding `merge` must pass `--format json`.

The `json` shape is byte-compatible with [fhir262](https://github.com/HealthSamurai/fhir262)'s
reporter output. Keep it that way; that compatibility is the point of the format existing.

### `ParseAuthHeader` must not enumerate schemes

An HTTP header name cannot contain whitespace. Text before the first colon with no whitespace is a
header name; anything else is a bare credential for `Authorization`. This handles `Negotiate`,
`NTLM`, `AWS4-HMAC-SHA256`, and credentials containing colons without listing schemes. The original
hardcoded `Bearer`/`Basic`/`Digest` list broke on every scheme not on it.

### `ApplyAuthHeader` must fail, never return quietly

Applying no header runs the whole suite unauthenticated and reports every 401 as a legitimate test
failure — indistinguishable from a broken server. Two traps, both of which shipped broken once:

- **Guard on `authHeader is null`, not `IsNullOrWhiteSpace`.** An omitted flag is `null` and means
  "no auth"; an explicit `""` is a mistake worth reporting — usually an env var that expanded to
  nothing, which is the common CI failure. `IsNullOrWhiteSpace` conflates them and made the
  empty-value guard below unreachable for the only input that matters.
- **Check `TryAddWithoutValidation`'s return.** It returns `false` — it does not throw — for a name
  that is not a valid HTTP token (e.g. `Api@Key`), silently dropping the credential.

### FHIR validity rules that keep getting missed

The generator's whole purpose is valid FHIR. These were each violated at least once:

- **No empty arrays.** FHIR JSON prohibits them. `Bundle.entry` is omitted when there are no
  resources, not emitted as `[]`.
- **No null-valued object properties.** FHIR permits `null` only *inside* arrays, to align a
  primitive with its `_`-prefixed extension. `test.description` is omitted when absent.
- **`TestReport.testScript` is 1..1.** Always emitted. A relative file path goes in `display`, never
  `reference` — a relative `Reference.reference` is parsed as `[type]/[id]`, so
  `Search/intervals.json` would read as a resource of type `Search`.
- **`Bundle.entry.fullUrl` must be absolute** and agree with `Resource.id`. These TestReports are
  never persisted and carry no `id`, so `urn:uuid:` is the correct form. It is also collision-free,
  unlike the slugified path it replaced.

### `TestReport.tester` is the tool, not the server

Verbatim R4: *"Name of the tester producing this report (Organization or individual)."* That is
`Ignixa.ConformanceMatrix.Cli`. The server under test is `participant[server]`, and `--impl` belongs
in that participant's `display`. Setting `tester = impl` made the report claim the server tested
itself. **Note:** ignixa-lab's `frontend/src/lib/testReport.ts` has this bug. Its shape is otherwise
the reference for ours — do not copy this part of it, nor its encoding of the impl name into the
`test-engine` participant URI.

### `score` excludes skipped tests

`TestScriptReport.OverallOutcome` has no `Skip` branch, so an all-skipped script reports
`result: "pass"`. Counting skips as misses therefore produced `"result": "pass"` with `"score": 0` —
a self-contradicting resource — for exactly the scripts that `fhirVersion` gating and
`requiresCapability` are designed to skip. It also disagreed with `MatrixBuilder`, which keeps
skipped out of both pass and fail, so the two formats scored the same run differently. Skips are out
of both numerator and denominator; `score` is omitted entirely (it is 0..1) when nothing was scored.

### Scripts that never ran become `OperationOutcome`, not `TestReport`

A file that failed to parse was never a TestScript, and `TestReport.testScript` references the script
that was executed — so there is no honest TestReport to write. Without an entry, a suite whose
scripts all failed to parse wrote a `Bundle` with no entries, indistinguishable from a clean run of
nothing.

**Do not use `TestReport.status = "entered-in-error"` for this.** Its official definition is *"This
test report was entered or created in error"* — a retraction, asserting the report should not exist.
`stopped` is *"manually stopped"*. `completed` is *"all test operations have completed"*, false when
none ran. There is no status code for "could not read the script" because the resource cannot express
it.

`OperationOutcome` is the FHIR-native carrier, and `Bundle.type = collection` has no homogeneity
constraint, so the two coexist legally:

- parse failure → `code: "structure"` — *"unable to parse the content completely, invalid syntax"*
- evaluator exception → `code: "exception"` — *"An unexpected internal error has occurred"*

### `TestReportContext` owns its own invariants

Blank and whitespace-only strings normalize to `null` in the init accessors, so "absent" has exactly
one representation. Before this, the generator's three consumption sites each invented their own
answer and one emitted `"display": ""`. `ServerUri` is a `Uri` and rejects a relative one at
construction — `Uri` permits `UriKind.Relative`, which `participant.uri` does not.

`Ignixa.TestScript` is a **packable** library. `Generate(report, context = null)` must keep working
with `context` omitted (`EndToEndTests.cs`, `docs/site/docs/core-sdk/testscript.md` call it that
way), and changing a public member's type is breaking once released.

### Exit codes

| Code | Meaning |
|------|---------|
| `0` | Every test passed or was skipped. |
| `1` | The suite ran; at least one test failed or errored. |
| `2` | Usage error — unusable `--tests`, `--server`, `--auth-header`, or `--out`. Nothing ran. |
| `3` | Unexpected internal failure; the full exception is printed. |

CI otherwise cannot tell "auth misconfigured, nothing ran" from "the suite ran and 3 tests failed".
`--out` is validated **before** the suite runs so a typo cannot discard a completed run.

---

## Where the code lives

| Concern | File |
|---|---|
| CLI options, run loop, payload/format selection, auth | `tools/Ignixa.ConformanceMatrix.Cli/Commands/RunCommand.cs` |
| `--format` values | `tools/Ignixa.ConformanceMatrix.Cli/Commands/ReportFormat.cs` |
| `OperationOutcome` for never-run scripts | `tools/Ignixa.ConformanceMatrix.Cli/Reporting/OperationOutcomeResourceGenerator.cs` |
| `TestReport` generation | `src/Core/Ignixa.TestScript/Reporting/TestReportResourceGenerator.cs` |
| Run-scoped context for the above | `src/Core/Ignixa.TestScript/Reporting/TestReportContext.cs` |
| Native report shape (`merge` input) | `tools/Ignixa.ConformanceMatrix.Cli/Reporting/ImplReport.cs` |
| Tests | `test/Ignixa.ConformanceMatrix.Cli.Tests/RunCommandTests.cs`, `test/Ignixa.TestScript.Tests/Reporting/TestReportResourceGeneratorTests.cs` |

## Known gaps (not defects — deliberately deferred)

- **`/metadata` 401/403 fails open.** A rejected credential still runs the whole suite, producing the
  "every test 401s" outcome the auth guards exist to prevent. Narrow to auth-specific statuses, or
  detect systemic auth failure across the run. No conformance test currently expects a 401, so a
  detector would not false-positive today.
- **Setup-failure granularity differs between formats.** When setup fails, `--format json` reports
  each test as `fail` (`ReportMapper.MapSetupFailure`) while `--format fhir` reports `skip` actions.
  Both agree at the top level (`result: fail`, exit 1).
- **Parse warnings** (`parseResult.IsSuccess` with non-empty `Errors`) reach stderr only, never
  either artifact.
