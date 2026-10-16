# ADR 2610: cli-on-fhir Agent Surface (patient summary, toolsets, lenient arguments, analyzer hints)

## Status

Proposed

> Shapes what `cli-on-fhir mcp` offers an agent by default from benchmark evidence: a `patient_summary` first call, a
> small `query` toolset, lenient handling of argument combinations whose intent is clear, and FHIRPath hints from the
> static analyzer before evaluation. Implemented as cli-on-fhir V14.

## Context

cli-on-fhir (`tools/Ignixa.CliOnFhir`) serves a folder of FHIR data to an agent over MCP, and the same operations as
CLI verbs. Its tools were tuned from V1 to V13 against
[fhir-question-bench](https://github.com/brendankowitz/fhir-question-bench/blob/fhir-agent-bench-phase0/docs/superpowers/reports/2026-10-cli-on-fhir-agent-eval.md)
(Copilot CLI with Claude Sonnet 5.5, Claude Haiku 5.5 and GPT-6 Luna; 58 held-out clinician questions over 109 Synthea
patients, plus 14 cohort questions). The design target is the PatientIQ agent contract: one MCP surface over a patient
context index, a warm root digest before the first question, every answer citing leaf resources.

### What the benchmark showed

From the evaluation report (V12, held-out set):

| Model | Shell only | MCP only + pre-computed patient map |
|---|---|---|
| Sonnet 5.5 | 96.6%, 70K tokens, 19.2 s | **100%, 48K (−31%), 11.5 s (−40%)** |
| Haiku 5.5 | 75.9%, 117K, 35.8 s | **100%, 67K (−43%), 19.7 s** |
| GPT-6 Luna | 91.4%, 57K, 28.0 s | **100%, 36K (−37%), 21.8 s** |

Later ablation runs, summarised for this ADR and not yet in the report, separate the two ingredients:

- **The tool alone**, given to an agent that also has a shell, is more accurate than the shell but uses more tokens: the
  agent pays for two menus and mixes the approaches.
- **The pre-computed patient map alone**, given to a shell agent, adds tokens (+12%) for little accuracy gain.
- **Tool and map together, with no shell**: 98–100% correct across the three models against 77–97% for the shell, with
  34–48% fewer tokens and about 40% less time.
- **Asking the model to build its own map first** (the metacognitive arm) costs more, and derailed Haiku to 31%.

The map is worth most when it is derived and handed over, which is the PatientIQ warm root digest. Today it is reached
through `patient_context` with `operation=root`, one operation of a three-operation tool, and the instructions have to
explain it. It was called 9 times in 548 attempts because the bench put the map in the prompt; a host that cannot do that
has no direct way to pre-load it.

### Tool usage, 548 attempts

| Call | Calls | Note |
|---|---|---|
| `search_fhir_resources` with `columns` | 478 | the workhorse |
| `patient_cohort` | 133 | |
| `patient_context find` | 114 | |
| `folder_code_systems text` | 108 | |
| `read_resources` | 81 | |
| `help` | 78 | in 14% of attempts: the short descriptions leave agents guessing |
| `fhirpath` mode | 42 | **31% of them failed** |
| `patient_context root` | 9 | the map was in the prompt |
| `terminology expand` | 3 | |
| `folder_inspect`, `patient_list` | 0 | in the lean profile |
| terminology other than class-search, class-members, search, lookup, translate | ≈0 | |

Of 87 failed search calls, about 60 were argument-combination errors, not FHIRPath errors: `columns` with `elements`
(34), a column named `ref` (13), `columns` with `fhirpath` (6), `fhirpath` with paging (6), and `perPatient` with
`_sort` (5). In each the intent was unambiguous, and the call failed anyway, costing a turn and the tokens of a retry.
Most of the remaining failures were FHIRPath paths that could never yield anything, such as a misspelt element or a choice
element written with a type it does not have.

Every request carries the tool menu: 17 tools cost about 12K tokens at V2; V13 is twelve tools and 2,985 tokens. Most of
what remains describes tools and operations the usage above shows agents do not call.

## Decision

### 1. A `patient_summary` tool and resource

`patient_summary(patient)` returns the compact patient map (the PatientIQ warm root digest, about 4 KB) for a patient
named by id, by identifier value (an MRN) or by `system|value`. The identifier is resolved with the `patient_list` fast
lookup (`PatientMatch`, which reads only Patient resources and never a search index). An identifier that names more than
one patient fails with the candidates, and one that names none fails with the near matches; the call never guesses. The
server instructions make it the first call for a patient question.

The same map is an MCP resource template, `patient://{id}/summary`, so a host that knows the patient can pre-load it into
the context before the first question, which is the configuration the benchmark measured.

For the Ignixa server roadmap, recommend the same shape as an operation, `Patient/{id}/$summary` (or a `$context`
operation) returning `Parameters`, so an agent reaching a FHIR server through generic tools gets the map in one call.
This relates to [ADR 2512: Patient Summary Generation](adr-2512-patient-summary-generation.md), which defines `$summary`
as the IPS document. The agent map is a different artifact: a compact, citable index for navigation, not a clinical
document. The two should not share the operation name without a decision there. This ADR recommends the operation and
does not implement it.

### 2. Toolsets: `--toolset query|all`, `query` by default

`query` exposes what agents use to answer questions:

| Tool | Under `query` |
|---|---|
| `patient_summary` | |
| `search_fhir_resources` | `columns`, `perPatient`, `summary=count`, `compartment`; no `fhirpath` mode |
| `patient_cohort` | |
| `folder_code_systems` | `text`, now with an optional `patient` scope |
| `terminology` | lookup, search, expand (`scope=folder`), class-search, class-members, translate |
| `read_resources` | |
| `help` | topics for these tools only |

`all` adds `patient_list`, `folder_inspect`, `patient_context` (root, expand, find), the `fhirpath` expression mode and
`patientSearch()`, the terminology operations subsumes, navigate, validate, list and classes, `terminology_load_package`,
`validate_resources` and `plan_fhir_search`. Each stays a CLI verb whatever the toolset. A call to something outside the
toolset (an operation, `fhirpath`, `patientSearch()`) fails with an issue naming `--toolset all`, rather than being
silently absent.

### 3. Lenient argument handling

Where the intent is unambiguous, apply the fix and say so in a warning (`argument-adjusted`) instead of failing:

| Combination | Applied fix |
|---|---|
| `columns` with `elements` (or `_elements`) | ignore `elements`: columns already choose what comes back |
| a column named `ref` | rename it `ref_1` (the next free `ref_N`): `ref` is the key column |
| `perPatient=latest` or `earliest` with `_sort` | drop the `_sort`: the pick orders by date itself |
| `columns` with `fhirpath` | keep `columns`, drop `fhirpath` |
| `fhirpath` with `count`, `offset` or `_count` | drop the paging: the expression sees every match |

Truly ambiguous calls still fail, for example `columns` with `summary=count`, or `perPatient=first` without a `_sort`.
The rules live in the search, so the CLI verb applies them too.

### 4. FHIRPath hints from the static analyzer

Before evaluation, every `columns` path and `fhirpath` expression is analysed by
`src/Core/Ignixa.FhirPath/Analysis/FhirPathAnalyzer.cs` against the searched type (the folder's FHIR schema provider; a
`fhirpath` expression against `Bundle`, with `entry.resource` taken as the searched type). Its findings become actionable
hints:

- **An unknown element** is an error that blocks evaluation, with a did-you-mean from the type's elements
  (`valueQuantiy` → `valueQuantity`).
- **A choice element spelt with a type it does not have** is an error naming the types it has (`effectiveDate` →
  `effective.ofType(dateTime)`, `effectivePeriod`...). A choice spelt with one of its types (`valueQuantity`) is valid:
  the evaluator answers it, so it raises nothing.
- **A path that can never yield anything** (a type filter no value can pass, an element of another resource type) is a
  warning that rides along with the results.

The engine must not reject an expression the evaluator answers. So only the unknown-element finding blocks, and only on a
concrete type. Everything the analyzer cannot decide (after `resolve()`, on an abstract `Resource`, an unknown function
such as `patientSearch`) produces no issue at all. Four analyzer defects surfaced while measuring this against the
expressions the tool's own tests and help use, and they are fixed in Core:

- a lower-case element named like a resource type (`medication`, `encounter`) was typed as that resource;
- every element on an abstract `Resource` focus was an error;
- `ofType(Observation)` on a `Resource` focus was "always empty";
- an unknown function's result took its focus's type, so navigation after `patientSearch()` was checked against Patient.

Unresolved properties become structured issues (`ValidationIssueKind.UnresolvedProperty` with the property and focus
types), so the hint is built from the schema rather than from message text.

### 5. A smaller menu and help

The `query` menu, with the instructions, is held to a test budget of 2,000 tokens (V13: 2,985 for everything; V14 measures
1,760). `all` gains `patient_summary` and is held to 3,300 (V14: 3,188). Both are measured with LF line breaks, since a
CRLF checkout adds a character per description line. `help` lists only the topics of the toolset in use.

## Consequences

- **Breaking for MCP clients.** Under the default `query` toolset, `patient_list`, `folder_inspect`, `patient_context`,
  `terminology_load_package`, `validate_resources`, `plan_fhir_search`, the `fhirpath` mode, `patientSearch()` and five
  terminology operations are gone. A client that relied on them passes `--toolset all`. `patient_context` with
  `operation=root` becomes `patient_summary`, which also accepts an MRN. The response envelope is unchanged.
- **CLI parity.** Every verb stays. `summary <path> <patient>` is the CLI form of `patient_summary`, and `systems --text`
  takes `--patient`. The lenient rules and analyzer hints apply to `search` as they do over MCP.
- **Lenient rules change failures into warnings.** An agent that ignores warnings gets the fixed call's answer, which is
  the answer it asked for in every listed case. The rules are a closed list; a combination not on it still fails.
- **The analyzer is now on the request path.** It is static and runs once per expression, at microseconds against a
  search over indexes. Its blind spots are by design: it never blocks after `resolve()`, and a case-only misspelling
  (`authoredon`) still yields an empty column with a `column-empty` warning, because the analyzer resolves names
  case-insensitively.
- **What the benchmark re-measures.** The `p3` profiles move to the V14 `query` menu. The bench should report, per model
  on the held-out and cohort sets: correctness, tokens and time against V12/V13; the share of searches failing (target:
  well under the 87 in 548 of V13); `help` calls per attempt; and whether `patient_summary` as the first call (the
  instructed route, no map in the prompt) matches the warm-map arm. If it does, the bench can drop the prompt-injected map
  in favour of the resource template.
- **The resource URI puts the id in the authority.** `patient://{id}/summary` is what was asked for, and hosts that send
  the URI as written work with any id, MRN or encoded `system|value`. A client that normalizes it through a URI parser
  (`System.Uri` does) lowercases the authority, so a mixed-case id names nobody, and an unencoded `|` or `:` is not a
  valid host. A path form (`patient:///summary/{id}`, or `patient://summary/{id}`) avoids both; decide before hosts
  depend on the template.
- **Reversibility.** `--toolset all` restores the V13 surface, less the failure modes the lenient rules remove.
- **V15 adds four grounded hints** from the MAI-Code-1.1-Flash transcript on the "LDL not at goal" cohort (it searched
  ICD-10 codes in a SNOMED-only folder, let `asOf` default to the clock two months after the data, and met an empty cohort
  with no reason). Each states a fact the tool computed and is silent otherwise: `code-system-not-in-folder`,
  `asOfSource` with an `asof-after-data` warning, `cohort-empty-step`, and a "values disagree at the same time" extension of
  the `patient_summary` quirk for one analyte under several LOINC codes. They live in issue text and help topics, so the
  menu and instructions budgets are unchanged. See the README section "Grounded hints (V15)".

## References

- [cli-on-fhir agent evaluation (fhir-question-bench)](https://github.com/brendankowitz/fhir-question-bench/blob/fhir-agent-bench-phase0/docs/superpowers/reports/2026-10-cli-on-fhir-agent-eval.md)
- [ADR 2512: Patient Summary Generation](adr-2512-patient-summary-generation.md): the `$summary` IPS operation.
- [cli-on-fhir README](../../tools/Ignixa.CliOnFhir/README.md): tools, toolsets and verbs.
- PatientIQ on the Patient Context Index (design note, Oct 2026): agent contract, warm root digest, question routes.
