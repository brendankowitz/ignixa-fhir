# Search Query Conformance Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add portable R4, R4B, and R5 TestScript coverage for `_lastUpdated`, history paging, projections, composed search queries, and direct-plus-iterated includes without weakening conformance assertions for Ignixa.

**Architecture:** Keep conformance behavior in five focused TestScript surfaces under `Ignixa.TestScript.Suites`; the scripts own deterministic fixtures, narrow capability gates, assertions, and teardown. The focused evaluator regression proves that an absolute `Bundle.link[relation='next'].url` extracted through FHIRPath is passed to the request provider unchanged. It passes against existing behavior, so evaluator production code remains unchanged.

**Tech Stack:** .NET 10 SDK, C# 13, xUnit, Shouldly, NSubstitute, Ignixa TestScript JSON, FHIRPath, PowerShell, SQL-backed API E2E tests.

---

**Source spec:** `docs/superpowers/specs/2026-07-21-search-query-conformance-coverage-design.md`

## File map

| File | Action | Responsibility |
|---|---|---|
| `test/Ignixa.TestScript.Tests/Evaluation/VariableExtractorTests.cs` | Modify | Prove a FHIRPath-extracted absolute history `next` URL is used unchanged as a complete `operation.url`. |
| `src/Core/Ignixa.TestScript.Suites/testscripts/Search/last-updated.json` | Create | POST/server-assigned-id lifecycle with deterministic lower, upper, bounded, and contradictory `_lastUpdated` coverage. |
| `src/Core/Ignixa.TestScript.Suites/testscripts/CRUD/history.json` | Modify | Add a third instance version, hard history paging assertions, opaque next-link extraction, and unchanged traversal. |
| `src/Core/Ignixa.TestScript.Suites/testscripts/Search/projection.json` | Create | `_elements`, `_summary=text`, `_summary=data`, `_summary=true`, and `_summary=count` projection behavior. |
| `src/Core/Ignixa.TestScript.Suites/testscripts/Search/query-composition.json` | Create | Typed-chain, status, code, `_lastUpdated`, unique sort, `_count=10`, match membership, and decoy exclusion coverage. |
| `src/Core/Ignixa.TestScript.Suites/testscripts/Search/includes.json` | Modify | Add a branch-visibility request and a separate direct-plus-iterated cross-path deduplication request. |
| `test/Ignixa.RepoGuards.Tests/RepoRootTests.cs` | Conditional create | If worktree guard execution proves the root-marker defect, cover `.git` file and directory discovery directly. |
| `test/Ignixa.RepoGuards.Tests/RepoRoot.cs` | Conditional modify | After the focused tests fail, recognize `.git` as either a file or directory. |
| `test/Ignixa.TestScript.Tests/Conformance/ConformanceScriptParseTests.cs` | Test only | Parse every suite and reject parser warnings; no source edit planned. |
| `test/Ignixa.RepoGuards.Tests/ConformanceSuiteExtensionGuardTests.cs` | Test only | Reject unknown Ignixa TestScript extensions; no source edit planned. |
| `test/Ignixa.Api.E2ETests/Conformance/TestScriptConformanceReportTests.cs` | Test only | Execute the repository suites against the isolated SQL E2E server and emit a report; no source edit planned. |

## Non-negotiable constraints

- The five suite surfaces are exactly `Search/last-updated.json`, `CRUD/history.json`, `Search/projection.json`, `Search/query-composition.json`, and `Search/includes.json`.
- TestScripts are the behavior tests. Do not add duplicate C# tests for search semantics.
- Every new test has `fhirVersions` value `4.0,4.3,5.0`.
- Every hard assertion stays hard. A failing Ignixa response is evidence of a server defect, not a reason to set `warningOnly`.
- Repeated `_lastUpdated` match-set assertions, iterate-dependent branch assertions, and direct-versus-iterated cross-path deduplication assertions remain warning-only unless an actual profile canonical requires the stronger behavior.
- Projection uses the fixed Patient id `ignixa-projection-pat1`, PUT update-create setup, delete teardown, and `_id=ignixa-projection-pat1` on every search. Setup retains `responseId` `setup-response` but has no assertions. Before the five query tests, the first FHIR-version-gated test uses one source-correlated hard-outcome `assertionAnyOfGroup` with only warning-only exact 200 and 201 members; no `okay`, 202, or 204 alternative and no production change.
- `_elements` and `_summary` controls are not gated on advertised `searchParam` entries. The first four projection query tests require only Patient `search-type`; the count composition additionally requires resource- or system-level `_lastUpdated`. Requested and mandatory fields stay hard; absence of unrequested, ordinary, narrative, or non-summary fields is warning-only because extras are legal.
- Every conditional exact SUBSETTED implication is warning-only because the normative language is SHOULD; the exact system remains `http://terminology.hl7.org/CodeSystem/v3-ObservationValue` and the exact code remains `SUBSETTED`.
- `_summary=count` permits no match entries; any entries present must be `OperationOutcome` resources with `search.mode='outcome'`.
- Query composition uses `_count=10`; it does not test pagination. Both matches and all three decoys must fit on the first response page.
- Query composition uses an existing pre-setup suite `requiresCapability` gate with exact `CapabilityStatement.fhirVersion` prefixes `4.0`, `4.3`, and `5.0` combined with Practitioner/DiagnosticReport update-create and delete requirements. Setup has seven PUT operations with unique `responseId` values and no assertions. The first test uses seven unique `assertionAnyOfGroup` groups, each with only exact warning-only 200 and 201 alternatives correlated by `sourceId`; each group fails hard if neither matches. Alternative groups are supported in test actions, not setup, so no root `fhirVersions` extension or engine/model/parser/evaluator support change is required. The two query tests assert exact HTTP 200 and non-vacuous criteria; repeated-range HTTP/Bundle shape stays hard while only result conclusions are warning-only.
- This effort adds exactly two include queries. `_total=accurate` stays in both requests, but `_total` is a control parameter and is not a CapabilityStatement advertisement requirement.
- History follows the server-provided `next` URL as an opaque complete URL. Do not parse, decode, append to, or reconstruct it.
- No new profile, TestScript extension, infrastructure, reference document, or production search behavior is in scope.
- Commit commands below are execution steps for the implementing worker. Do not run them while authoring or reviewing this plan.

### Task 1: Guard opaque absolute operation URLs

**Files:**
- Modify: `test/Ignixa.TestScript.Tests/Evaluation/VariableExtractorTests.cs:14-134`

- [ ] **Step 1: Add the focused evaluator regression test**

Add this test to `VariableExtractorTests`. It uses the real R4 schema already stored in `_r4Schema`, extracts a `Bundle.link.url` through FHIRPath, and uses the result as the complete second operation URL.

```csharp
[Fact]
public async Task GivenExpressionExtractedAbsoluteNextUrl_WhenUsedAsOperationUrl_ThenPassesThroughUnchanged()
{
    const string nextUrl =
        "https://example.test/fhir/Patient/123/_history?_count=1&ct=opaque%2Btoken";
    var responses = new Queue<TestResponse>(
    [
        new TestResponse
        {
            StatusCode = 200,
            Body = JsonSourceNodeFactory.Parse(
                $$"""
                {
                  "resourceType": "Bundle",
                  "type": "history",
                  "link": [
                    {
                      "relation": "next",
                      "url": "{{nextUrl}}"
                    }
                  ]
                }
                """)
        },
        new TestResponse
        {
            StatusCode = 200,
            Body = JsonSourceNodeFactory.Parse("""{"resourceType":"Bundle","type":"history"}""")
        }
    ]);
    _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
        .Returns(_ => responses.Dequeue());

    var definition = new TestScriptDefinition
    {
        Metadata = new TestScriptMetadata { Name = "OpaqueNextUrl" },
        Variables =
        [
            new VariableDefinition
            {
                Name = "nextUrl",
                SourceId = "history-page-one",
                Extraction = new ExpressionExtraction(
                    "Bundle.link.where(relation = 'next').url")
            }
        ],
        Setup =
        [
            new OperationExpression
            {
                Type = "history",
                Url = "Patient/123/_history?_count=1",
                ResponseId = "history-page-one"
            }
        ],
        Tests =
        [
            new TestPhaseDefinition
            {
                Name = "FollowNext",
                Actions =
                [
                    new OperationExpression
                    {
                        Type = "history",
                        Url = "${nextUrl}"
                    }
                ]
            }
        ]
    };

    var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _r4Schema);

    var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

    report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
    await _mockProvider.Received(1).ExecuteAsync(
        Arg.Is<TestRequest>(request => request.Url == nextUrl),
        Arg.Any<CancellationToken>());
}
```

- [ ] **Step 2: Run the focused regression**

Run:

```powershell
dotnet test test\Ignixa.TestScript.Tests\Ignixa.TestScript.Tests.csproj `
  --framework net10.0 `
  --filter "FullyQualifiedName~VariableExtractorTests.GivenExpressionExtractedAbsoluteNextUrl_WhenUsedAsOperationUrl_ThenPassesThroughUnchanged" `
  --logger "console;verbosity=minimal"
```

Expected: `Passed! - Failed: 0, Passed: 1`. The existing `BuildUrl` branch resolves `op.Url` and
returns it unchanged; NSubstitute observes the exact scheme, host, escaped `%2B`, and opaque `ct` key.
Do not modify evaluator, model, parser, or HTTP-provider production code.

- [ ] **Step 3: Commit the regression guard**

```powershell
git add test\Ignixa.TestScript.Tests\Evaluation\VariableExtractorTests.cs
git commit -m "Guard opaque TestScript operation URLs"
```

### Task 2: Add deterministic `_lastUpdated` behavior tests

**Files:**
- Create: `src/Core/Ignixa.TestScript.Suites/testscripts/Search/last-updated.json`

- [ ] **Step 1: Create the suite fixture, setup, and teardown**

Use this lifecycle fragment when creating `last-updated.json`. Setup POSTs a Patient, captures its
server-assigned id, and teardown deletes that id. The suite-specific identifier prevents server-global
data from satisfying the checks; Steps 2-3 add the tests.

```json
{
  "resourceType": "TestScript",
  "name": "Search/last-updated",
  "description": "_lastUpdated search-parameter conformance tests against a single server-assigned-id Patient: single-bound ge/lt assertions, AND-by-repetition over a broad range, and a contradictory repeated-bound case whose result is implementation-defined.",
  "status": "active",
  "extension": [
    {
      "url": "http://ignixa.io/testscript/requiresCapability",
      "valueString": "rest.resource.where(type='Patient').interaction.where(code='create').exists() and rest.resource.where(type='Patient').interaction.where(code='delete').exists()"
    }
  ],
  "fixture": [
    {
      "id": "pat1",
      "autocreate": false,
      "autodelete": false,
      "resource": {
        "resourceType": "Patient",
        "identifier": [
          {
            "system": "http://ignixa.io/testscript/suite/last-updated",
            "value": "LAST-UPDATED-PAT1"
          }
        ],
        "active": true,
        "name": [{ "family": "LastUpdated", "given": ["Pat1"] }]
      }
    }
  ],
  "variable": [
    {
      "name": "responseId",
      "sourceId": "create-pat1",
      "path": "id",
      "description": "Server-assigned id for the fixture patient"
    }
  ],
  "setup": {
    "action": [
      {
        "operation": {
          "type": { "code": "create" },
          "resource": "Patient",
          "sourceId": "pat1",
          "responseId": "create-pat1",
          "description": "POST Patient to establish the fixture with a server-assigned lastUpdated timestamp"
        }
      },
      {
        "assert": {
          "description": "Setup create must return 201 Created",
          "sourceId": "create-pat1",
          "response": "created"
        }
      }
    ]
  },
  "teardown": {
    "action": [
      {
        "operation": {
          "type": { "code": "delete" },
          "url": "Patient/${responseId}",
          "description": "Teardown: delete the fixture patient created by this run"
        }
      }
    ]
  }
}
```

- [ ] **Step 2: Add the two hard single-bound behavior tests**

Add these first two objects to the `test` array:

```json
[
  {
    "name": "_lastUpdated ge2000 with suite identifier returns the fixture",
    "description": "The POST-created Patient must match a lower bound far before its creation and is identified by its captured resource id and suite identifier.",
    "extension": [
      {
        "url": "http://ignixa.io/testscript/fhirVersions",
        "valueString": "4.0,4.3,5.0"
      },
      {
        "url": "http://ignixa.io/testscript/requiresCapability",
        "valueString": "rest.resource.where(type='Patient').interaction.where(code='search-type').exists() and (rest.resource.where(type='Patient').searchParam.where(name='identifier').exists() or rest.searchParam.where(name='identifier').exists()) and (rest.resource.where(type='Patient').searchParam.where(name='_lastUpdated').exists() or rest.searchParam.where(name='_lastUpdated').exists())"
      }
    ],
    "action": [
      {
        "operation": {
          "type": { "code": "search" },
          "resource": "Patient",
          "params": "?identifier=http://ignixa.io/testscript/suite/last-updated|LAST-UPDATED-PAT1&_lastUpdated=ge2000",
          "responseId": "ge2000-response",
          "description": "GET /Patient?identifier=http://ignixa.io/testscript/suite/last-updated|LAST-UPDATED-PAT1&_lastUpdated=ge2000"
        }
      },
      { "assert": { "description": "Search must return HTTP 200", "responseCode": "200" } },
      { "assert": { "description": "Response must be a Bundle", "resource": "Bundle" } },
      { "assert": { "description": "Bundle must be a searchset", "expression": "type = 'searchset'" } },
      {
        "assert": {
          "description": "The captured fixture id and suite identifier must both be present",
          "expression": "entry.where(resource.id = '${responseId}' and resource.identifier.where(system = 'http://ignixa.io/testscript/suite/last-updated' and value = 'LAST-UPDATED-PAT1').exists()).exists()"
        }
      }
    ]
  },
  {
    "name": "_lastUpdated lt2000 with suite identifier returns empty bundle",
    "description": "The POST-created Patient must not match an upper bound before its creation.",
    "extension": [
      {
        "url": "http://ignixa.io/testscript/fhirVersions",
        "valueString": "4.0,4.3,5.0"
      },
      {
        "url": "http://ignixa.io/testscript/requiresCapability",
        "valueString": "rest.resource.where(type='Patient').interaction.where(code='search-type').exists() and (rest.resource.where(type='Patient').searchParam.where(name='identifier').exists() or rest.searchParam.where(name='identifier').exists()) and (rest.resource.where(type='Patient').searchParam.where(name='_lastUpdated').exists() or rest.searchParam.where(name='_lastUpdated').exists())"
      }
    ],
    "action": [
      {
        "operation": {
          "type": { "code": "search" },
          "resource": "Patient",
          "params": "?identifier=http://ignixa.io/testscript/suite/last-updated|LAST-UPDATED-PAT1&_lastUpdated=lt2000",
          "responseId": "lt2000-response",
          "description": "GET /Patient?identifier=http://ignixa.io/testscript/suite/last-updated|LAST-UPDATED-PAT1&_lastUpdated=lt2000"
        }
      },
      { "assert": { "description": "Search must return HTTP 200", "responseCode": "200" } },
      { "assert": { "description": "Response must be a Bundle", "resource": "Bundle" } },
      { "assert": { "description": "Bundle must be a searchset", "expression": "type = 'searchset'" } },
      {
        "assert": {
          "description": "No match entry may be returned",
          "expression": "entry.where(search.mode = 'match').empty()"
        }
      }
    ]
  }
]
```

- [ ] **Step 3: Add bounded and contradictory repeated-key behavior tests**

Append these objects to the `test` array. HTTP status and Bundle shape remain hard; only the
repeated-key match-set conclusions are warning-only. The broad-range membership expression correlates
the captured id and suite identifier on the same entry.

```json
[
{
  "name": "repeated _lastUpdated keys express a broad bounded range",
  "description": "The workload records repeated-key range behavior; base FHIR does not make this unprofiled match-set expectation portable.",
  "extension": [
    {
      "url": "http://ignixa.io/testscript/fhirVersions",
      "valueString": "4.0,4.3,5.0"
    },
    {
      "url": "http://ignixa.io/testscript/requiresCapability",
      "valueString": "rest.resource.where(type='Patient').interaction.where(code='search-type').exists() and (rest.resource.where(type='Patient').searchParam.where(name='identifier').exists() or rest.searchParam.where(name='identifier').exists()) and (rest.resource.where(type='Patient').searchParam.where(name='_lastUpdated').exists() or rest.searchParam.where(name='_lastUpdated').exists())"
    }
  ],
  "action": [
    {
      "operation": {
        "type": { "code": "search" },
        "resource": "Patient",
        "params": "?identifier=http://ignixa.io/testscript/suite/last-updated|LAST-UPDATED-PAT1&_lastUpdated=ge2000&_lastUpdated=lt2999",
        "responseId": "range-response",
        "description": "Search with ge2000 and lt2999"
      }
    },
    { "assert": { "description": "Search must return HTTP 200", "responseCode": "200" } },
    { "assert": { "description": "Response must be a Bundle", "resource": "Bundle" } },
    { "assert": { "description": "Bundle must be a searchset", "expression": "type = 'searchset'" } },
    {
      "assert": {
        "description": "Unprofiled repeated-key behavior should include the fixture",
        "expression": "entry.where(resource.id = '${responseId}' and resource.identifier.where(system = 'http://ignixa.io/testscript/suite/last-updated' and value = 'LAST-UPDATED-PAT1').exists()).exists()",
        "warningOnly": true
      }
    }
  ]
},
{
  "name": "contradictory repeated _lastUpdated keys return no match",
  "description": "The workload records contradictory repeated-key behavior; base FHIR does not make this unprofiled match-set expectation portable.",
  "extension": [
    {
      "url": "http://ignixa.io/testscript/fhirVersions",
      "valueString": "4.0,4.3,5.0"
    },
    {
      "url": "http://ignixa.io/testscript/requiresCapability",
      "valueString": "rest.resource.where(type='Patient').interaction.where(code='search-type').exists() and (rest.resource.where(type='Patient').searchParam.where(name='identifier').exists() or rest.searchParam.where(name='identifier').exists()) and (rest.resource.where(type='Patient').searchParam.where(name='_lastUpdated').exists() or rest.searchParam.where(name='_lastUpdated').exists())"
    }
  ],
  "action": [
    {
      "operation": {
        "type": { "code": "search" },
        "resource": "Patient",
        "params": "?identifier=http://ignixa.io/testscript/suite/last-updated|LAST-UPDATED-PAT1&_lastUpdated=ge2000&_lastUpdated=lt2000",
        "responseId": "contradictory-response",
        "description": "Search with contradictory ge2000 and lt2000"
      }
    },
    { "assert": { "description": "Search must return HTTP 200", "responseCode": "200" } },
    { "assert": { "description": "Response must be a Bundle", "resource": "Bundle" } },
    { "assert": { "description": "Bundle must be a searchset", "expression": "type = 'searchset'" } },
    {
      "assert": {
        "description": "Unprofiled contradictory repeated-key behavior should return no entry",
        "expression": "entry.where(search.mode = 'match').empty()",
        "warningOnly": true
      }
    }
  ]
}
]
```

- [ ] **Step 4: Parse the new behavior test**

Run:

```powershell
Get-Content src\Core\Ignixa.TestScript.Suites\testscripts\Search\last-updated.json -Raw |
  ConvertFrom-Json |
  Out-Null
dotnet test test\Ignixa.TestScript.Tests\Ignixa.TestScript.Tests.csproj `
  --framework net10.0 `
  --filter "FullyQualifiedName~ConformanceScriptParseTests" `
  --logger "console;verbosity=minimal"
```

Expected: PowerShell exits without a JSON parse error; `dotnet test` ends with `Failed: 0`; `ConformanceScriptParseTests` reports no parser warnings for `Search/last-updated.json`.

- [ ] **Step 5: Execute the `_lastUpdated` behavior test against the R4 E2E target**

Run with the repository SQL E2E connection already available in `TEST_SQL_CONNECTION_STRING`:

```powershell
$reportPath = Join-Path $env:TEMP 'ignixa-last-updated-conformance.json'
$env:IGNIXA_RUN_CONFORMANCE = 'true'
$env:IGNIXA_CONFORMANCE_REPORT_PATH = $reportPath
dotnet test test\Ignixa.Api.E2ETests\Ignixa.Api.E2ETests.csproj `
 --framework net10.0 `
 --filter "FullyQualifiedName~TestScriptConformanceReportTests.GivenConformanceRunEnabled_WhenRunningRepositoryTestScripts_ThenWritesLatestReport" `
 --logger "console;verbosity=minimal"
$results = @((Get-Content $reportPath -Raw | ConvertFrom-Json).results |
 Where-Object { $_.file -eq 'Search/last-updated.json' })
if (-not $results) { throw 'Search/last-updated.json was not executed' }
$blocking = @($results | Where-Object { $_.status -in @('fail', 'error') })
if ($blocking) { throw 'Search/last-updated.json has a hard conformance failure' }
```

Expected: `dotnet test` ends with `Failed: 0`; the report contains `Search/last-updated.json`; no hard result is `fail` or `error`. If a hard assertion fails, keep it hard and stop for server diagnosis.

- [ ] **Step 6: Commit the `_lastUpdated` suite**

```powershell
git add src\Core\Ignixa.TestScript.Suites\testscripts\Search\last-updated.json
git commit -m "Add last-updated conformance coverage"
```

### Task 3: Extend instance history with opaque next-link paging

**Files:**
- Modify: `src/Core/Ignixa.TestScript.Suites/testscripts/CRUD/history.json:4,31-53,94-143,230-236`

- [ ] **Step 1: Add a second content-changing patch fixture**

Append this fixture after `patch-add-active`. It changes `active` from `true` to `false`, guaranteeing a third persisted version rather than an idempotent update.

```json
{
  "id": "patch-replace-active",
  "autocreate": false,
  "autodelete": false,
  "resource": {
    "resourceType": "Parameters",
    "parameter": [
      {
        "name": "operation",
        "part": [
          { "name": "type", "valueString": "replace" },
          { "name": "path", "valueString": "Patient.active" },
          { "name": "value", "valueBoolean": false }
        ]
      }
    ]
  }
}
```

Update the suite description to say the portable instance-history fixture receives two actual FHIRPath Patch content changes and therefore has at least three versions.

- [ ] **Step 2: Produce three versions in the existing instance-history test**

Use this description and action list in the three-version instance-history test. Keep its existing
`history-instance` and `patch` capability gate unchanged.

```json
{
"description": "After applying two FHIRPath Patches to the Patient created in setup (first adding active=true, then replacing it with active=false), GET /Patient/{id}/_history must return at least three entries with the newest version first (default order).",
"action": [
  {
    "operation": {
      "type": { "code": "patch" },
      "url": "Patient/${histAId}",
      "contentType": "application/fhir+json",
      "sourceId": "patch-add-active",
      "responseId": "hista-update-response",
      "description": "FHIRPath Patch the Patient (add Patient.active=true) to produce a second version."
    }
  },
  { "assert": { "description": "First patch must return a 2xx success status", "response": "okay" } },
  {
    "operation": {
      "type": { "code": "patch" },
      "url": "Patient/${histAId}",
      "contentType": "application/fhir+json",
      "sourceId": "patch-replace-active",
      "responseId": "hista-update2-response",
      "description": "FHIRPath Patch the Patient (replace Patient.active with false) to produce a third version."
    }
  },
  { "assert": { "description": "Second patch must return a 2xx success status", "response": "okay" } },
  {
    "operation": {
      "type": { "code": "history" },
      "url": "Patient/${histAId}/_history",
      "responseId": "hista-history-response",
      "description": "GET instance-level history for the Patient"
    }
  },
  { "assert": { "description": "Must return HTTP 200", "response": "okay" } },
  { "assert": { "description": "Response must be a Bundle", "resource": "Bundle" } },
  { "assert": { "description": "Bundle type must be 'history'", "expression": "type = 'history'" } },
  { "assert": { "description": "Must contain at least three entries (the create and two patches)", "expression": "entry.count()", "value": "2", "operator": "greaterThan" } },
  {
    "assert": {
      "description": "Default order must be newest first (entry[0] lastUpdated >= entry[1] lastUpdated)",
      "expression": "entry[0].resource.meta.lastUpdated >= entry[1].resource.meta.lastUpdated"
    }
  }
]
}
```

Change both supported sort-test descriptions from “two-version” to “three-version”. Keep their
ascending and descending ordering assertions hard. Preserve `warningOnly` only on unsupported or
server-specific rejection behavior such as `_sort=_id`.

- [ ] **Step 3: Define the next-link and first-page version variables**

Append these variables to the top-level `variable` array:

```json
[
  {
    "name": "histANextUrl",
    "sourceId": "hista-page-one-response",
    "expression": "Bundle.link.where(relation = 'next').url",
    "description": "absolute next-page URL extracted from the first history page; used to test opaque-URL following"
  },
  {
    "name": "histAFirstVersionId",
    "sourceId": "hista-page-one-response",
    "expression": "Bundle.entry.first().resource.meta.versionId",
    "description": "versionId of the first entry on page 1 of the paged history; used to assert page 2 returns a different version"
  }
]
```

- [ ] **Step 4: Add the portable paging behavior test**

Insert this test immediately after the three-version instance-history test and before the existing sort tests:

```json
{
  "name": "instance history paging: _count=1 returns at most one entry per page and follows the opaque next link",
  "description": "GET /Patient/{id}/_history?_count=1 must return HTTP 200, a history Bundle with at most one entry, and a next link. Following that next link (treated as opaque — no token inspection) must yield a second page whose first entry has a different versionId than the first page.",
  "extension": [
    {
      "url": "http://ignixa.io/testscript/fhirVersions",
      "valueString": "4.0,4.3,5.0"
    },
    {
      "url": "http://ignixa.io/testscript/requiresCapability",
      "valueString": "rest.resource.where(type='Patient').interaction.where(code='history-instance').exists() and rest.resource.where(type='Patient').interaction.where(code='patch').exists()"
    }
  ],
  "action": [
    {
      "operation": {
        "type": { "code": "history" },
        "url": "Patient/${histAId}/_history?_count=1",
        "responseId": "hista-page-one-response",
        "description": "GET first page of instance history with page size 1"
      }
    },
    { "assert": { "description": "Must return HTTP 200", "responseCode": "200" } },
    { "assert": { "description": "Response must be a Bundle", "resource": "Bundle" } },
    { "assert": { "description": "Bundle type must be 'history'", "expression": "type = 'history'" } },
    { "assert": { "description": "Page 1 must contain at least one entry", "expression": "entry.exists()" } },
    { "assert": { "description": "Page 1 must contain at most one entry (_count=1)", "expression": "entry.count() <= 1" } },
    {
      "assert": {
        "description": "A next link with a URL must be present (there are more versions beyond page 1)",
        "expression": "link.where(relation = 'next').url.exists()"
      }
    },
    {
      "operation": {
        "type": { "code": "history" },
        "url": "${histANextUrl}",
        "responseId": "hista-page-two-response",
        "description": "Follow the opaque next link to fetch page 2 of the instance history"
      }
    },
    { "assert": { "description": "Must return HTTP 200", "responseCode": "200" } },
    { "assert": { "description": "Response must be a Bundle", "resource": "Bundle" } },
    { "assert": { "description": "Bundle type must be 'history'", "expression": "type = 'history'" } },
    { "assert": { "description": "Page 2 must contain at least one entry", "expression": "entry.exists()" } },
    { "assert": { "description": "Page 2 must contain at most one entry (_count=1)", "expression": "entry.count() <= 1" } },
    { "assert": { "description": "First entry on page 2 must have a different versionId than page 1 (confirming distinct history versions)", "expression": "entry.first().resource.meta.versionId != '${histAFirstVersionId}'" } }
  ]
}
```

The `_count=1` value is a history control parameter, not an advertised search parameter. Do not add
an `_count` `searchParam` capability gate; the actual gate is `history-instance` plus `patch`, which
is required to construct the deterministic multi-version fixture.

- [ ] **Step 5: Parse history and run the opaque-URL regression test**

Run:

```powershell
Get-Content src\Core\Ignixa.TestScript.Suites\testscripts\CRUD\history.json -Raw |
  ConvertFrom-Json |
  Out-Null
dotnet test test\Ignixa.TestScript.Tests\Ignixa.TestScript.Tests.csproj `
  --framework net10.0 `
  --filter "FullyQualifiedName~VariableExtractorTests.GivenExpressionExtractedAbsoluteNextUrl_WhenUsedAsOperationUrl_ThenPassesThroughUnchanged|FullyQualifiedName~ConformanceScriptParseTests" `
  --logger "console;verbosity=minimal"
```

Expected: JSON parsing exits cleanly; the absolute-URL test passes; all conformance scripts parse with `Failed: 0` and no parser warnings.

- [ ] **Step 6: Execute history paging against the R4 E2E target**

Run:

```powershell
$reportPath = Join-Path $env:TEMP 'ignixa-history-paging-conformance.json'
$env:IGNIXA_RUN_CONFORMANCE = 'true'
$env:IGNIXA_CONFORMANCE_REPORT_PATH = $reportPath
dotnet test test\Ignixa.Api.E2ETests\Ignixa.Api.E2ETests.csproj `
  --framework net10.0 `
  --filter "FullyQualifiedName~TestScriptConformanceReportTests.GivenConformanceRunEnabled_WhenRunningRepositoryTestScripts_ThenWritesLatestReport" `
  --logger "console;verbosity=minimal"
$results = @((Get-Content $reportPath -Raw | ConvertFrom-Json).results |
  Where-Object { $_.file -eq 'CRUD/history.json' })
if (-not $results) { throw 'CRUD/history.json was not executed' }
$blocking = @($results | Where-Object { $_.status -in @('fail', 'error') })
if ($blocking) { throw 'CRUD/history.json has a hard conformance failure' }
```

Expected: `dotnet test` ends with `Failed: 0`; the report contains `CRUD/history.json`; both bounded
pages are non-empty history Bundles with at most one entry, the second page starts with a different
version, and the opaque second request succeeds. Supported sort ordering and `_summary=count`
behavior remain hard; only unsupported/server-specific rejection observations remain non-blocking.

- [ ] **Step 7: Commit history paging**

```powershell
git add src\Core\Ignixa.TestScript.Suites\testscripts\CRUD\history.json
git commit -m "Add opaque history paging coverage"
```

### Task 4: Add portable projection behavior tests

**Files:**
- Create: `src/Core/Ignixa.TestScript.Suites/testscripts/Search/projection.json`

- [ ] **Step 1: Create and validate the projection fixture and lifecycle**

Use this lifecycle fragment when creating the file with the fixed Patient id
`ignixa-projection-pat1`. `communication` is deliberately
ordinary non-summary data used to distinguish `_summary=true`; `name` is the unrequested `_elements`
probe. Setup is a PUT update-create and teardown is delete, so the suite-level lifecycle gate requires
both capabilities. Setup captures `setup-response` and contains no assertions. A dedicated first test,
gated to `fhirVersions` `4.0,4.3,5.0`, validates that source through one
`assertionAnyOfGroup`. The group has exactly two `warningOnly` members correlated by `sourceId`,
accepting only exact `responseCode` `200` and exact `responseCode` `201`; the group outcome is hard
when neither matches. Do not add `okay`, `202`, or `204`. Alternative groups are supported in test
actions rather than setup, so no production parser/evaluator or other engine change is required.

```json
{
  "resourceType": "TestScript",
  "name": "Search/projection",
  "description": "_elements and _summary projection conformance tests against a single PUT-upserted Patient fixture (id=ignixa-projection-pat1) that carries a narrative, active, name, gender, and non-summary communication field. All projection searches are scoped by _id=ignixa-projection-pat1 for guaranteed isolation.",
  "status": "active",
  "extension": [
    {
      "url": "http://ignixa.io/testscript/requiresCapability",
      "valueString": "rest.resource.where(type='Patient').updateCreate = true and rest.resource.where(type='Patient').interaction.where(code='delete').exists()"
    }
  ],
  "fixture": [
    {
      "id": "pat1",
      "autocreate": false,
      "autodelete": false,
      "resource": {
        "resourceType": "Patient",
        "id": "ignixa-projection-pat1",
        "text": {
          "status": "generated",
          "div": "<div xmlns=\"http://www.w3.org/1999/xhtml\">Projection suite fixture: PROJECTION-PAT1, active, female, with communication data</div>"
        },
        "identifier": [
          {
            "system": "http://ignixa.io/testscript/suite/projection",
            "value": "PROJECTION-PAT1"
          }
        ],
        "active": true,
        "name": [{ "family": "Projection", "given": ["Pat1"] }],
        "gender": "female",
        "communication": [
          {
            "language": {
              "coding": [
                {
                  "system": "urn:ietf:bcp:47",
                  "code": "en"
                }
              ]
            }
          }
        ]
      }
    }
  ],
  "setup": {
    "action": [
      {
        "operation": {
          "type": { "code": "update" },
          "url": "Patient/ignixa-projection-pat1",
          "sourceId": "pat1",
          "responseId": "setup-response",
          "description": "PUT Patient/ignixa-projection-pat1 — upsert fixture: active=true, name (Projection/Pat1), gender=female, non-summary communication, and narrative div"
        }
      }
    ]
  },
  "test": [
    {
      "name": "setup PUT response is 200 or 201",
      "description": "The fixture PUT must return exactly 200 OK for an update or 201 Created for a create; grouped warningOnly alternatives produce a hard failure when neither status matches.",
      "extension": [
        { "url": "http://ignixa.io/testscript/fhirVersions", "valueString": "4.0,4.3,5.0" }
      ],
      "action": [
        { "assert": { "description": "Update alternative: setup PUT returns 200 OK", "sourceId": "setup-response", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "setup-put-status" }], "responseCode": "200", "warningOnly": true } },
        { "assert": { "description": "Create alternative: setup PUT returns 201 Created", "sourceId": "setup-response", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "setup-put-status" }], "responseCode": "201", "warningOnly": true } }
      ]
    }
  ],
  "teardown": {
    "action": [
      {
        "operation": {
          "type": { "code": "delete" },
          "url": "Patient/ignixa-projection-pat1",
          "description": "Teardown: delete the projection suite fixture patient"
        }
      }
    ]
  }
}
```

- [ ] **Step 2: Add `_elements=active` with advisory conditional SUBSETTED coverage**

Append this object to `test` after the setup-response validation test. The fixed REST id and requested `active` are hard. Name omission is
warning-only because extra fields are permitted. The exact SUBSETTED implication is also warning-only
because the applicable normative language is SHOULD.

```json
{
  "name": "_elements=active: active field projected, name omission warningOnly, SUBSETTED implication warningOnly",
  "description": "GET /Patient?_id=ignixa-projection-pat1&_elements=active must retain active=true. Name omission and the conditional SUBSETTED implication are warningOnly.",
  "extension": [
    {
      "url": "http://ignixa.io/testscript/fhirVersions",
      "valueString": "4.0,4.3,5.0"
    },
    {
      "url": "http://ignixa.io/testscript/requiresCapability",
      "valueString": "rest.resource.where(type='Patient').interaction.where(code='search-type').exists()"
    }
  ],
  "action": [
    {
      "operation": {
        "type": { "code": "search" },
        "resource": "Patient",
        "params": "?_id=ignixa-projection-pat1&_elements=active",
        "responseId": "elements-active",
        "description": "GET /Patient?_id=ignixa-projection-pat1&_elements=active"
      }
    },
    { "assert": { "description": "Search must return HTTP 200", "responseCode": "200" } },
    { "assert": { "description": "Response must be a Bundle", "resource": "Bundle" } },
    { "assert": { "description": "Bundle type must be searchset", "expression": "Bundle.type", "value": "searchset", "operator": "equals" } },
    {
      "assert": {
        "description": "Matched resource must retain its mandatory REST id",
        "expression": "entry.where(resource.id = 'ignixa-projection-pat1').exists()"
      }
    },
    {
      "assert": {
        "description": "Requested Patient.active must be present and true",
        "expression": "entry.where(resource.id = 'ignixa-projection-pat1').resource.where(active = true).exists()"
      }
    },
    {
      "assert": {
        "description": "Unrequested Patient.name should be omitted, but extra fields are permitted",
        "expression": "entry.where(resource.id = 'ignixa-projection-pat1').resource.name.exists()",
        "value": "false",
        "operator": "equals",
        "warningOnly": true
      }
    },
    {
      "assert": {
        "description": "SUBSETTED implication (warningOnly): name is present or the exact SUBSETTED tag exists",
        "expression": "entry.where(resource.id = 'ignixa-projection-pat1').resource.name.exists() or entry.where(resource.id = 'ignixa-projection-pat1').resource.meta.tag.where(system = 'http://terminology.hl7.org/CodeSystem/v3-ObservationValue' and code = 'SUBSETTED').exists()",
        "warningOnly": true
      }
    }
  ]
}
```

- [ ] **Step 3: Add `_summary=text`, `_summary=data`, and `_summary=true`**

Append these three behavior tests. Required fields remain hard. Every absence check and every exact
SUBSETTED implication is warning-only because extras are permitted and SUBSETTED uses SHOULD language.
The controls are not gated as advertised `searchParam` entries.

```json
[
{
  "name": "_summary=text: narrative present, data omission warningOnly, SUBSETTED conditional warningOnly",
  "description": "Narrative presence is hard; ordinary-data omission and the exact SUBSETTED implication are warningOnly.",
  "extension": [
    { "url": "http://ignixa.io/testscript/fhirVersions", "valueString": "4.0,4.3,5.0" },
    {
      "url": "http://ignixa.io/testscript/requiresCapability",
      "valueString": "rest.resource.where(type='Patient').interaction.where(code='search-type').exists()"
    }
  ],
  "action": [
    {
      "operation": {
        "type": { "code": "search" },
        "resource": "Patient",
        "params": "?_id=ignixa-projection-pat1&_summary=text",
        "responseId": "summary-text",
        "description": "GET /Patient?_id=ignixa-projection-pat1&_summary=text"
      }
    },
    { "assert": { "description": "Search must return HTTP 200", "responseCode": "200" } },
    { "assert": { "description": "Response must be a Bundle", "resource": "Bundle" } },
    { "assert": { "description": "Bundle type must be searchset", "expression": "Bundle.type", "value": "searchset", "operator": "equals" } },
    { "assert": { "description": "Fixture patient must appear", "expression": "entry.where(resource.id = 'ignixa-projection-pat1').exists()" } },
    { "assert": { "description": "Narrative must be present", "expression": "entry.where(resource.id = 'ignixa-projection-pat1').resource.text.`div`.exists()" } },
    { "assert": { "description": "Ordinary data should be absent; extras are permitted", "expression": "entry.where(resource.id = 'ignixa-projection-pat1').resource.where(active.empty() and name.empty() and gender.empty()).exists()", "warningOnly": true } },
    { "assert": { "description": "Communication is present or the exact SUBSETTED tag exists", "expression": "entry.where(resource.id = 'ignixa-projection-pat1').resource.communication.exists() or entry.where(resource.id = 'ignixa-projection-pat1').resource.meta.tag.where(system = 'http://terminology.hl7.org/CodeSystem/v3-ObservationValue' and code = 'SUBSETTED').exists()", "warningOnly": true } }
  ]
},
{
  "name": "_summary=data: data fields present, narrative omission warningOnly, SUBSETTED conditional warningOnly",
  "description": "Fixture data presence is hard; narrative omission and the exact SUBSETTED implication are warningOnly.",
  "extension": [
    { "url": "http://ignixa.io/testscript/fhirVersions", "valueString": "4.0,4.3,5.0" },
    {
      "url": "http://ignixa.io/testscript/requiresCapability",
      "valueString": "rest.resource.where(type='Patient').interaction.where(code='search-type').exists()"
    }
  ],
  "action": [
    {
      "operation": {
        "type": { "code": "search" },
        "resource": "Patient",
        "params": "?_id=ignixa-projection-pat1&_summary=data",
        "responseId": "summary-data",
        "description": "GET /Patient?_id=ignixa-projection-pat1&_summary=data"
      }
    },
    { "assert": { "description": "Search must return HTTP 200", "responseCode": "200" } },
    { "assert": { "description": "Response must be a Bundle", "resource": "Bundle" } },
    { "assert": { "description": "Bundle type must be searchset", "expression": "Bundle.type", "value": "searchset", "operator": "equals" } },
    { "assert": { "description": "Fixture patient must appear", "expression": "entry.where(resource.id = 'ignixa-projection-pat1').exists()" } },
    { "assert": { "description": "Active must be retained", "expression": "entry.where(resource.id = 'ignixa-projection-pat1').resource.where(active = true).exists()" } },
    { "assert": { "description": "Name must be retained", "expression": "entry.where(resource.id = 'ignixa-projection-pat1').resource.name.exists()" } },
    { "assert": { "description": "Gender must be retained", "expression": "entry.where(resource.id = 'ignixa-projection-pat1').resource.gender.exists()" } },
    { "assert": { "description": "Communication must be retained", "expression": "entry.where(resource.id = 'ignixa-projection-pat1').resource.communication.exists()" } },
    { "assert": { "description": "Narrative should be absent; extras are permitted", "expression": "entry.where(resource.id = 'ignixa-projection-pat1').resource.text.exists()", "value": "false", "operator": "equals", "warningOnly": true } },
    { "assert": { "description": "Narrative is present or the exact SUBSETTED tag exists", "expression": "entry.where(resource.id = 'ignixa-projection-pat1').resource.text.exists() or entry.where(resource.id = 'ignixa-projection-pat1').resource.meta.tag.where(system = 'http://terminology.hl7.org/CodeSystem/v3-ObservationValue' and code = 'SUBSETTED').exists()", "warningOnly": true } }
  ]
},
{
  "name": "_summary=true: Patient summary fields present, communication omission warningOnly, SUBSETTED conditional warningOnly",
  "description": "Patient summary fields are hard; communication omission and the exact SUBSETTED implication are warningOnly.",
  "extension": [
    { "url": "http://ignixa.io/testscript/fhirVersions", "valueString": "4.0,4.3,5.0" },
    {
      "url": "http://ignixa.io/testscript/requiresCapability",
      "valueString": "rest.resource.where(type='Patient').interaction.where(code='search-type').exists()"
    }
  ],
  "action": [
    {
      "operation": {
        "type": { "code": "search" },
        "resource": "Patient",
        "params": "?_id=ignixa-projection-pat1&_summary=true",
        "responseId": "summary-true",
        "description": "GET /Patient?_id=ignixa-projection-pat1&_summary=true"
      }
    },
    { "assert": { "description": "Search must return HTTP 200", "responseCode": "200" } },
    { "assert": { "description": "Response must be a Bundle", "resource": "Bundle" } },
    { "assert": { "description": "Bundle type must be searchset", "expression": "Bundle.type", "value": "searchset", "operator": "equals" } },
    { "assert": { "description": "Fixture patient must appear", "expression": "entry.where(resource.id = 'ignixa-projection-pat1').exists()" } },
    { "assert": { "description": "Active summary field must be retained", "expression": "entry.where(resource.id = 'ignixa-projection-pat1').resource.where(active = true).exists()" } },
    { "assert": { "description": "Name summary field must be retained", "expression": "entry.where(resource.id = 'ignixa-projection-pat1').resource.name.exists()" } },
    { "assert": { "description": "Gender summary field must be retained", "expression": "entry.where(resource.id = 'ignixa-projection-pat1').resource.gender.exists()" } },
    { "assert": { "description": "Non-summary communication should be absent; extras are permitted", "expression": "entry.where(resource.id = 'ignixa-projection-pat1').resource.communication.exists()", "value": "false", "operator": "equals", "warningOnly": true } },
    { "assert": { "description": "Communication is present or the exact SUBSETTED tag exists", "expression": "entry.where(resource.id = 'ignixa-projection-pat1').resource.communication.exists() or entry.where(resource.id = 'ignixa-projection-pat1').resource.meta.tag.where(system = 'http://terminology.hl7.org/CodeSystem/v3-ObservationValue' and code = 'SUBSETTED').exists()", "warningOnly": true } }
  ]
}
]
```

- [ ] **Step 4: Add `_summary=count` composed with `_lastUpdated`**

Append this final test. Unlike the other four projection tests, it requires resource- or system-level
`_lastUpdated` advertisement in addition to Patient `search-type`; `_summary` itself remains an
ungated control parameter.

```json
{
  "name": "_summary=count with _lastUpdated=ge2000: total=1, no match entries, outcome entries guarded",
  "description": "The fixed _id-scoped match contributes total=1, no match entries are returned, and any entries present are outcome-mode OperationOutcome resources.",
  "extension": [
    { "url": "http://ignixa.io/testscript/fhirVersions", "valueString": "4.0,4.3,5.0" },
    {
      "url": "http://ignixa.io/testscript/requiresCapability",
      "valueString": "rest.resource.where(type='Patient').interaction.where(code='search-type').exists() and (rest.resource.where(type='Patient').searchParam.where(name='_lastUpdated').exists() or rest.searchParam.where(name='_lastUpdated').exists())"
    }
  ],
  "action": [
    {
      "operation": {
        "type": { "code": "search" },
        "resource": "Patient",
        "params": "?_id=ignixa-projection-pat1&_summary=count&_lastUpdated=ge2000",
        "responseId": "summary-count",
        "description": "GET /Patient?_id=ignixa-projection-pat1&_summary=count&_lastUpdated=ge2000"
      }
    },
    { "assert": { "description": "Search must return HTTP 200", "responseCode": "200" } },
    { "assert": { "description": "Response must be a Bundle", "resource": "Bundle" } },
    { "assert": { "description": "Bundle type must be searchset", "expression": "Bundle.type", "value": "searchset", "operator": "equals" } },
    { "assert": { "description": "Only the suite fixture must count", "expression": "total = 1" } },
    { "assert": { "description": "Count summary must not include match entries", "expression": "entry.where(search.mode = 'match').empty()" } },
    { "assert": { "description": "Every entry present must be an outcome-mode OperationOutcome", "expression": "entry.all(resource.ofType(OperationOutcome).exists() and search.mode = 'outcome')" } }
  ]
}
```

- [ ] **Step 5: Parse the projection suite**

Run:

```powershell
Get-Content src\Core\Ignixa.TestScript.Suites\testscripts\Search\projection.json -Raw |
  ConvertFrom-Json |
  Out-Null
dotnet test test\Ignixa.TestScript.Tests\Ignixa.TestScript.Tests.csproj `
  --framework net10.0 `
  --filter "FullyQualifiedName~ConformanceScriptParseTests" `
  --logger "console;verbosity=minimal"
```

Expected: JSON parsing exits cleanly; the parser suite ends with `Failed: 0` and no warnings for `Search/projection.json`.

- [ ] **Step 6: Execute projection behavior against the R4 E2E target**

Run:

```powershell
$reportPath = Join-Path $env:TEMP 'ignixa-projection-conformance.json'
$env:IGNIXA_RUN_CONFORMANCE = 'true'
$env:IGNIXA_CONFORMANCE_REPORT_PATH = $reportPath
dotnet test test\Ignixa.Api.E2ETests\Ignixa.Api.E2ETests.csproj `
  --framework net10.0 `
  --filter "FullyQualifiedName~TestScriptConformanceReportTests.GivenConformanceRunEnabled_WhenRunningRepositoryTestScripts_ThenWritesLatestReport" `
  --logger "console;verbosity=minimal"
$results = @((Get-Content $reportPath -Raw | ConvertFrom-Json).results |
  Where-Object { $_.file -eq 'Search/projection.json' })
if (-not $results) { throw 'Search/projection.json was not executed' }
$blocking = @($results | Where-Object { $_.status -in @('fail', 'error') })
if ($blocking) { throw 'Search/projection.json has a hard conformance failure' }
```

Expected: `dotnet test` ends with `Failed: 0`; the report contains `Search/projection.json`; fixed
`_id` isolation, requested/mandatory fields, summary-field presence, `total=1`, no match entries, and
outcome-entry restrictions have no hard failure. Field-absence and exact SUBSETTED implications may
remain warnings.

- [ ] **Step 7: Commit projection coverage**

```powershell
git add src\Core\Ignixa.TestScript.Suites\testscripts\Search\projection.json
git commit -m "Add projection conformance coverage"
```

### Task 5: Add the composed DiagnosticReport workload

**Files:**
- Create: `src/Core/Ignixa.TestScript.Suites/testscripts/Search/query-composition.json`

- [ ] **Step 1: Create and validate deterministic practitioners and reports**

Use this fixture, validation, and lifecycle fragment when creating `query-composition.json`. Setup
contains seven PUT operations with unique `responseId` values and no assertions. The dedicated first
test is gated to `fhirVersions` `4.0,4.3,5.0` and validates each captured response through a unique
`assertionAnyOfGroup`. Every group has exactly two `warningOnly` members correlated by `sourceId`:
exact `responseCode` `200` and exact `responseCode` `201`. The aggregate group outcome is hard when
neither alternative matches; do not add `202`, `204`, or `okay` alternatives.

Alternative groups are parser-supported in test actions, not setup actions, which is why validation
is the first test phase. No production parser or evaluator change is required. The existing
suite-level `requiresCapability` evaluator checks exact `CapabilityStatement.fhirVersion` prefixes
`4.0`, `4.3`, or `5.0` together with lifecycle support before setup. There is no root
`fhirVersions` extension. The two matches have unique `issued` values, and each decoy differs from a
match in exactly one required predicate.

```json
{
  "resourceType": "TestScript",
  "name": "Search/query-composition",
  "description": "Portable DiagnosticReport query composition with status, code, typed chain, _lastUpdated, unique issued sort, _count=10 as workload composition rather than pagination coverage, and independent single-predicate decoys.",
  "status": "active",
  "extension": [
    {
      "url": "http://ignixa.io/testscript/requiresCapability",
      "valueString": "(fhirVersion.startsWith('4.0') or fhirVersion.startsWith('4.3') or fhirVersion.startsWith('5.0')) and rest.resource.where(type='Practitioner').updateCreate = true and rest.resource.where(type='Practitioner').interaction.where(code='delete').exists() and rest.resource.where(type='DiagnosticReport').updateCreate = true and rest.resource.where(type='DiagnosticReport').interaction.where(code='delete').exists()"
    }
  ],
  "fixture": [
    {
      "id": "composition-practitioner-target",
      "autocreate": false,
      "autodelete": false,
      "resource": {
        "resourceType": "Practitioner",
        "id": "ignixa-query-prac-target",
        "identifier": [
          {
            "system": "http://ignixa.io/testscript/suite/query-composition",
            "value": "QUERY-PRAC-TARGET"
          }
        ],
        "name": [{ "family": "CompositionTarget" }]
      }
    },
    {
      "id": "composition-practitioner-other",
      "autocreate": false,
      "autodelete": false,
      "resource": {
        "resourceType": "Practitioner",
        "id": "ignixa-query-prac-other",
        "identifier": [
          {
            "system": "http://ignixa.io/testscript/suite/query-composition",
            "value": "QUERY-PRAC-OTHER"
          }
        ],
        "name": [{ "family": "CompositionOther" }]
      }
    },
    {
      "id": "composition-match-one",
      "autocreate": false,
      "autodelete": false,
      "resource": {
        "resourceType": "DiagnosticReport",
        "id": "ignixa-query-match-1",
        "identifier": [
          {
            "system": "http://ignixa.io/testscript/suite/query-composition",
            "value": "QUERY-MATCH-1"
          }
        ],
        "status": "final",
        "code": {
          "coding": [{ "system": "http://loinc.org", "code": "24323-8" }]
        },
        "issued": "2020-01-01T00:00:00Z",
        "resultsInterpreter": [
          { "reference": "Practitioner/ignixa-query-prac-target" }
        ]
      }
    },
    {
      "id": "composition-match-two",
      "autocreate": false,
      "autodelete": false,
      "resource": {
        "resourceType": "DiagnosticReport",
        "id": "ignixa-query-match-2",
        "identifier": [
          {
            "system": "http://ignixa.io/testscript/suite/query-composition",
            "value": "QUERY-MATCH-2"
          }
        ],
        "status": "final",
        "code": {
          "coding": [{ "system": "http://loinc.org", "code": "24323-8" }]
        },
        "issued": "2021-01-01T00:00:00Z",
        "resultsInterpreter": [
          { "reference": "Practitioner/ignixa-query-prac-target" }
        ]
      }
    },
    {
      "id": "composition-decoy-status",
      "autocreate": false,
      "autodelete": false,
      "resource": {
        "resourceType": "DiagnosticReport",
        "id": "ignixa-query-decoy-status",
        "identifier": [
          {
            "system": "http://ignixa.io/testscript/suite/query-composition",
            "value": "QUERY-DECOY-STATUS"
          }
        ],
        "status": "preliminary",
        "code": {
          "coding": [{ "system": "http://loinc.org", "code": "24323-8" }]
        },
        "issued": "2022-01-01T00:00:00Z",
        "resultsInterpreter": [
          { "reference": "Practitioner/ignixa-query-prac-target" }
        ]
      }
    },
    {
      "id": "composition-decoy-code",
      "autocreate": false,
      "autodelete": false,
      "resource": {
        "resourceType": "DiagnosticReport",
        "id": "ignixa-query-decoy-code",
        "identifier": [
          {
            "system": "http://ignixa.io/testscript/suite/query-composition",
            "value": "QUERY-DECOY-CODE"
          }
        ],
        "status": "final",
        "code": {
          "coding": [{ "system": "http://loinc.org", "code": "99999-9" }]
        },
        "issued": "2023-01-01T00:00:00Z",
        "resultsInterpreter": [
          { "reference": "Practitioner/ignixa-query-prac-target" }
        ]
      }
    },
    {
      "id": "composition-decoy-practitioner",
      "autocreate": false,
      "autodelete": false,
      "resource": {
        "resourceType": "DiagnosticReport",
        "id": "ignixa-query-decoy-practitioner",
        "identifier": [
          {
            "system": "http://ignixa.io/testscript/suite/query-composition",
            "value": "QUERY-DECOY-PRACTITIONER"
          }
        ],
        "status": "final",
        "code": {
          "coding": [{ "system": "http://loinc.org", "code": "24323-8" }]
        },
        "issued": "2024-01-01T00:00:00Z",
        "resultsInterpreter": [
          { "reference": "Practitioner/ignixa-query-prac-other" }
        ]
      }
    }
  ],
  "setup": {
    "action": [
      { "operation": { "type": { "code": "update" }, "url": "Practitioner/ignixa-query-prac-target", "sourceId": "composition-practitioner-target", "responseId": "setup-practitioner-target-response", "description": "PUT target Practitioner" } },
      { "operation": { "type": { "code": "update" }, "url": "Practitioner/ignixa-query-prac-other", "sourceId": "composition-practitioner-other", "responseId": "setup-practitioner-other-response", "description": "PUT other Practitioner" } },
      { "operation": { "type": { "code": "update" }, "url": "DiagnosticReport/ignixa-query-match-1", "sourceId": "composition-match-one", "responseId": "setup-match-one-response", "description": "PUT first matching report" } },
      { "operation": { "type": { "code": "update" }, "url": "DiagnosticReport/ignixa-query-match-2", "sourceId": "composition-match-two", "responseId": "setup-match-two-response", "description": "PUT second matching report" } },
      { "operation": { "type": { "code": "update" }, "url": "DiagnosticReport/ignixa-query-decoy-status", "sourceId": "composition-decoy-status", "responseId": "setup-decoy-status-response", "description": "PUT status decoy" } },
      { "operation": { "type": { "code": "update" }, "url": "DiagnosticReport/ignixa-query-decoy-code", "sourceId": "composition-decoy-code", "responseId": "setup-decoy-code-response", "description": "PUT code decoy" } },
      { "operation": { "type": { "code": "update" }, "url": "DiagnosticReport/ignixa-query-decoy-practitioner", "sourceId": "composition-decoy-practitioner", "responseId": "setup-decoy-practitioner-response", "description": "PUT practitioner-chain decoy" } }
    ]
  },
  "test": [
    {
      "name": "fixture setup response validation",
      "description": "Each fixture PUT must return exactly 200 OK for an update or 201 Created for a create; grouped warningOnly alternatives produce a hard failure when neither status matches.",
      "extension": [
        { "url": "http://ignixa.io/testscript/fhirVersions", "valueString": "4.0,4.3,5.0" }
      ],
      "action": [
        { "assert": { "description": "Update alternative: target Practitioner PUT returns 200 OK", "sourceId": "setup-practitioner-target-response", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "setup-practitioner-target-status" }], "responseCode": "200", "warningOnly": true } },
        { "assert": { "description": "Create alternative: target Practitioner PUT returns 201 Created", "sourceId": "setup-practitioner-target-response", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "setup-practitioner-target-status" }], "responseCode": "201", "warningOnly": true } },
        { "assert": { "description": "Update alternative: other Practitioner PUT returns 200 OK", "sourceId": "setup-practitioner-other-response", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "setup-practitioner-other-status" }], "responseCode": "200", "warningOnly": true } },
        { "assert": { "description": "Create alternative: other Practitioner PUT returns 201 Created", "sourceId": "setup-practitioner-other-response", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "setup-practitioner-other-status" }], "responseCode": "201", "warningOnly": true } },
        { "assert": { "description": "Update alternative: first matching report PUT returns 200 OK", "sourceId": "setup-match-one-response", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "setup-match-one-status" }], "responseCode": "200", "warningOnly": true } },
        { "assert": { "description": "Create alternative: first matching report PUT returns 201 Created", "sourceId": "setup-match-one-response", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "setup-match-one-status" }], "responseCode": "201", "warningOnly": true } },
        { "assert": { "description": "Update alternative: second matching report PUT returns 200 OK", "sourceId": "setup-match-two-response", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "setup-match-two-status" }], "responseCode": "200", "warningOnly": true } },
        { "assert": { "description": "Create alternative: second matching report PUT returns 201 Created", "sourceId": "setup-match-two-response", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "setup-match-two-status" }], "responseCode": "201", "warningOnly": true } },
        { "assert": { "description": "Update alternative: status decoy PUT returns 200 OK", "sourceId": "setup-decoy-status-response", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "setup-decoy-status-status" }], "responseCode": "200", "warningOnly": true } },
        { "assert": { "description": "Create alternative: status decoy PUT returns 201 Created", "sourceId": "setup-decoy-status-response", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "setup-decoy-status-status" }], "responseCode": "201", "warningOnly": true } },
        { "assert": { "description": "Update alternative: code decoy PUT returns 200 OK", "sourceId": "setup-decoy-code-response", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "setup-decoy-code-status" }], "responseCode": "200", "warningOnly": true } },
        { "assert": { "description": "Create alternative: code decoy PUT returns 201 Created", "sourceId": "setup-decoy-code-response", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "setup-decoy-code-status" }], "responseCode": "201", "warningOnly": true } },
        { "assert": { "description": "Update alternative: practitioner-chain decoy PUT returns 200 OK", "sourceId": "setup-decoy-practitioner-response", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "setup-decoy-practitioner-status" }], "responseCode": "200", "warningOnly": true } },
        { "assert": { "description": "Create alternative: practitioner-chain decoy PUT returns 201 Created", "sourceId": "setup-decoy-practitioner-response", "extension": [{ "url": "http://ignixa.io/testscript/assertionAnyOfGroup", "valueString": "setup-decoy-practitioner-status" }], "responseCode": "201", "warningOnly": true } }
      ]
    }
  ],
  "teardown": {
    "action": [
      { "operation": { "type": { "code": "delete" }, "url": "DiagnosticReport/ignixa-query-decoy-practitioner", "description": "Teardown practitioner decoy" } },
      { "operation": { "type": { "code": "delete" }, "url": "DiagnosticReport/ignixa-query-decoy-code", "description": "Teardown code decoy" } },
      { "operation": { "type": { "code": "delete" }, "url": "DiagnosticReport/ignixa-query-decoy-status", "description": "Teardown status decoy" } },
      { "operation": { "type": { "code": "delete" }, "url": "DiagnosticReport/ignixa-query-match-2", "description": "Teardown second match" } },
      { "operation": { "type": { "code": "delete" }, "url": "DiagnosticReport/ignixa-query-match-1", "description": "Teardown first match" } },
      { "operation": { "type": { "code": "delete" }, "url": "Practitioner/ignixa-query-prac-other", "description": "Teardown other Practitioner" } },
      { "operation": { "type": { "code": "delete" }, "url": "Practitioner/ignixa-query-prac-target", "description": "Teardown target Practitioner" } }
    ]
  }
}
```

- [ ] **Step 2: Add the hard single-bound composed query after fixture validation**

Append this object to the `test` array after the fixture-validation test:

```json
[
  {
    "name": "typed-chain query composes filters, unique sort, and count",
    "description": "_count=10 keeps both matches and all decoy candidates on one page so match-scoped membership, exclusion, and unique issued ordering are provable; _count is workload composition only, not pagination coverage.",
    "extension": [
      { "url": "http://ignixa.io/testscript/fhirVersions", "valueString": "4.0,4.3,5.0" },
      {
        "url": "http://ignixa.io/testscript/requiresCapability",
        "valueString": "rest.resource.where(type='DiagnosticReport').interaction.where(code='search-type').exists() and rest.resource.where(type='DiagnosticReport').searchParam.where(name='status').exists() and rest.resource.where(type='DiagnosticReport').searchParam.where(name='code').exists() and rest.resource.where(type='DiagnosticReport').searchParam.where(name='results-interpreter').exists() and rest.resource.where(type='DiagnosticReport').searchParam.where(name='issued').exists() and (rest.resource.where(type='DiagnosticReport').searchParam.where(name='_lastUpdated').exists() or rest.searchParam.where(name='_lastUpdated').exists()) and (rest.resource.where(type='DiagnosticReport').searchParam.where(name='_sort').exists() or rest.searchParam.where(name='_sort').exists()) and (rest.resource.where(type='DiagnosticReport').searchParam.where(name='_count').exists() or rest.searchParam.where(name='_count').exists()) and rest.resource.where(type='Practitioner').searchParam.where(name='identifier').exists()"
      }
    ],
    "action": [
      {
        "operation": {
          "type": { "code": "search" },
          "resource": "DiagnosticReport",
          "params": "?status=final&code=http://loinc.org|24323-8&results-interpreter:Practitioner.identifier=http://ignixa.io/testscript/suite/query-composition|QUERY-PRAC-TARGET&_lastUpdated=ge2000&_sort=issued&_count=10",
          "responseId": "query-composition-single-bound",
          "description": "Run the complete single-bound workload; _count=10 composes the workload and is not pagination coverage"
        }
      },
      { "assert": { "description": "Search must return HTTP 200", "responseCode": "200" } },
      { "assert": { "description": "Response must be a Bundle", "resource": "Bundle" } },
      { "assert": { "description": "Bundle must be a searchset", "expression": "type = 'searchset'" } },
      { "assert": { "description": "Exactly two match entries must be returned; non-match entries do not affect this count", "expression": "entry.where(search.mode = 'match').count() = 2" } },
      { "assert": { "description": "First expected report must appear exactly once as a match", "expression": "entry.where(search.mode = 'match' and resource.id = 'ignixa-query-match-1').count() = 1" } },
      { "assert": { "description": "Second expected report must appear exactly once as a match", "expression": "entry.where(search.mode = 'match' and resource.id = 'ignixa-query-match-2').count() = 1" } },
      { "assert": { "description": "Every match entry must have final status", "expression": "entry.where(search.mode = 'match' and (resource.status.empty() or resource.status != 'final')).empty()" } },
      { "assert": { "description": "Every match entry must contain the required LOINC coding", "expression": "entry.where(search.mode = 'match' and resource.code.coding.where(system = 'http://loinc.org' and code = '24323-8').empty()).empty()" } },
      { "assert": { "description": "Every match entry must reference the target Practitioner", "expression": "entry.where(search.mode = 'match' and resource.resultsInterpreter.where(reference = 'Practitioner/ignixa-query-prac-target').empty()).empty()" } },
      { "assert": { "description": "The earlier unique issued value must be the first match", "expression": "entry.where(search.mode = 'match')[0].resource.id = 'ignixa-query-match-1'" } },
      { "assert": { "description": "The later unique issued value must be the second match", "expression": "entry.where(search.mode = 'match')[1].resource.id = 'ignixa-query-match-2'" } },
      { "assert": { "description": "Status-only decoy must be excluded from matches", "expression": "entry.where(search.mode = 'match' and resource.id = 'ignixa-query-decoy-status').empty()" } },
      { "assert": { "description": "Code-only decoy must be excluded from matches", "expression": "entry.where(search.mode = 'match' and resource.id = 'ignixa-query-decoy-code').empty()" } },
      { "assert": { "description": "Practitioner-identifier-only decoy must be excluded from matches", "expression": "entry.where(search.mode = 'match' and resource.id = 'ignixa-query-decoy-practitioner').empty()" } }
    ]
  }
]
```

- [ ] **Step 3: Add the repeated-range workload variant**

Append this test. Exact HTTP 200 and response shape are hard. Match-set, membership, criteria,
exclusion, and order expectations are all warning-only without a profile fixing repeated-key
semantics.

```json
{
  "name": "typed-chain workload with repeated _lastUpdated range keys",
  "description": "The same workload repeats _lastUpdated=ge2000 and _lastUpdated=lt2999. HTTP and Bundle shape remain hard, while result expectations are warningOnly because repeated-key AND semantics are not normatively profiled. _count=10 remains workload composition only, not pagination coverage.",
  "extension": [
    { "url": "http://ignixa.io/testscript/fhirVersions", "valueString": "4.0,4.3,5.0" },
    {
      "url": "http://ignixa.io/testscript/requiresCapability",
      "valueString": "rest.resource.where(type='DiagnosticReport').interaction.where(code='search-type').exists() and rest.resource.where(type='DiagnosticReport').searchParam.where(name='status').exists() and rest.resource.where(type='DiagnosticReport').searchParam.where(name='code').exists() and rest.resource.where(type='DiagnosticReport').searchParam.where(name='results-interpreter').exists() and rest.resource.where(type='DiagnosticReport').searchParam.where(name='issued').exists() and (rest.resource.where(type='DiagnosticReport').searchParam.where(name='_lastUpdated').exists() or rest.searchParam.where(name='_lastUpdated').exists()) and (rest.resource.where(type='DiagnosticReport').searchParam.where(name='_sort').exists() or rest.searchParam.where(name='_sort').exists()) and (rest.resource.where(type='DiagnosticReport').searchParam.where(name='_count').exists() or rest.searchParam.where(name='_count').exists()) and rest.resource.where(type='Practitioner').searchParam.where(name='identifier').exists()"
    }
  ],
  "action": [
    {
      "operation": {
        "type": { "code": "search" },
        "resource": "DiagnosticReport",
        "params": "?status=final&code=http://loinc.org|24323-8&results-interpreter:Practitioner.identifier=http://ignixa.io/testscript/suite/query-composition|QUERY-PRAC-TARGET&_lastUpdated=ge2000&_lastUpdated=lt2999&_sort=issued&_count=10",
        "responseId": "query-composition-repeated-range",
        "description": "Run the complete repeated-range workload; _count=10 composes the workload and is not pagination coverage"
      }
    },
    { "assert": { "description": "Search must return HTTP 200", "responseCode": "200" } },
    { "assert": { "description": "Response must be a Bundle", "resource": "Bundle" } },
    { "assert": { "description": "Bundle must be a searchset", "expression": "type = 'searchset'" } },
    { "assert": { "description": "Repeated-key behavior should retain exactly two matches", "expression": "entry.where(search.mode = 'match').count() = 2", "warningOnly": true } },
    { "assert": { "description": "Repeated-key behavior should retain both expected matches exactly once", "expression": "entry.where(search.mode = 'match' and resource.id = 'ignixa-query-match-1').count() = 1 and entry.where(search.mode = 'match' and resource.id = 'ignixa-query-match-2').count() = 1", "warningOnly": true } },
    { "assert": { "description": "Repeated-key matches should satisfy status, code, and Practitioner-reference criteria", "expression": "entry.where(search.mode = 'match' and (resource.status.empty() or resource.status != 'final')).empty() and entry.where(search.mode = 'match' and resource.code.coding.where(system = 'http://loinc.org' and code = '24323-8').empty()).empty() and entry.where(search.mode = 'match' and resource.resultsInterpreter.where(reference = 'Practitioner/ignixa-query-prac-target').empty()).empty()", "warningOnly": true } },
    { "assert": { "description": "Repeated-key matches should remain ordered by unique issued values", "expression": "entry.where(search.mode = 'match')[0].resource.id = 'ignixa-query-match-1' and entry.where(search.mode = 'match')[1].resource.id = 'ignixa-query-match-2'", "warningOnly": true } },
    { "assert": { "description": "Repeated-key behavior should exclude all three decoys from matches", "expression": "entry.where(search.mode = 'match' and (resource.id = 'ignixa-query-decoy-status' or resource.id = 'ignixa-query-decoy-code' or resource.id = 'ignixa-query-decoy-practitioner')).empty()", "warningOnly": true } }
  ]
}
```

- [ ] **Step 4: Parse the query-composition suite**

Run:

```powershell
Get-Content src\Core\Ignixa.TestScript.Suites\testscripts\Search\query-composition.json -Raw |
  ConvertFrom-Json |
  Out-Null
dotnet test test\Ignixa.TestScript.Tests\Ignixa.TestScript.Tests.csproj `
  --framework net10.0 `
  --filter "FullyQualifiedName~ConformanceScriptParseTests" `
  --logger "console;verbosity=minimal"
```

Expected: JSON parsing exits cleanly; the parser suite ends with `Failed: 0` and no warnings for `Search/query-composition.json`.

- [ ] **Step 5: Execute query composition against the R4 E2E target**

Run:

```powershell
$reportPath = Join-Path $env:TEMP 'ignixa-query-composition-conformance.json'
$env:IGNIXA_RUN_CONFORMANCE = 'true'
$env:IGNIXA_CONFORMANCE_REPORT_PATH = $reportPath
dotnet test test\Ignixa.Api.E2ETests\Ignixa.Api.E2ETests.csproj `
  --framework net10.0 `
  --filter "FullyQualifiedName~TestScriptConformanceReportTests.GivenConformanceRunEnabled_WhenRunningRepositoryTestScripts_ThenWritesLatestReport" `
  --logger "console;verbosity=minimal"
$results = @((Get-Content $reportPath -Raw | ConvertFrom-Json).results |
  Where-Object { $_.file -eq 'Search/query-composition.json' })
if (-not $results) { throw 'Search/query-composition.json was not executed' }
$blocking = @($results | Where-Object { $_.status -in @('fail', 'error') })
if ($blocking) { throw 'Search/query-composition.json has a hard conformance failure' }
```

Expected: `dotnet test` ends with `Failed: 0`; all seven fixture-status groups accept exactly 200
or 201 and would fail hard for any other status; the single-bound workload returns both uniquely
ordered matches and excludes every decoy on its `_count=10` page. A repeated-range warning does not
fail the run.

- [ ] **Step 6: Commit query-composition coverage**

```powershell
git add src\Core\Ignixa.TestScript.Suites\testscripts\Search\query-composition.json
git commit -m "Add composed search conformance coverage"
```

### Task 6: Split include branch visibility from cross-path deduplication

**Files:**
- Modify: `src/Core/Ignixa.TestScript.Suites/testscripts/Search/includes.json:263-313`

- [ ] **Step 1: Add the branch-visibility behavior test**

Insert this object after the existing single-branch iterate tests. The query intentionally omits
`Observation:performer`, so Organization and Practitioner can only be visible through the two Patient
iterate branches. Keep `_total=accurate`, but do not require `_total` CapabilityStatement
advertisement because it is a control parameter.

```json
{
  "name": "two iterate branches are visible without a direct performer include",
  "description": "The hard direct-subject contract is separated from warning-only branch visibility because base CapabilityStatement has no precise iterate declaration.",
  "extension": [
    { "url": "http://ignixa.io/testscript/fhirVersions", "valueString": "4.0,4.3,5.0" },
    {
      "url": "http://ignixa.io/testscript/requiresCapability",
      "valueString": "rest.resource.where(type='Observation').interaction.where(code='search-type').exists() and (rest.resource.where(type='Observation').searchParam.where(name='_id').exists() or rest.searchParam.where(name='_id').exists()) and rest.resource.where(type='Observation').searchInclude.where($this='Observation:subject' or $this='*').exists()"
    }
  ],
  "action": [
    {
      "operation": {
        "type": { "code": "search" },
        "resource": "Observation",
        "params": "?_id=ignixa-inc-obs1&_include=Observation:subject&_include:iterate=Patient:organization&_include:iterate=Patient:general-practitioner&_total=accurate",
        "responseId": "inc-iterate-branch-visibility",
        "description": "Search with direct subject and two iterate branches, without direct performer"
      }
    },
    { "assert": { "description": "Search must return HTTP 200", "responseCode": "200" } },
    { "assert": { "description": "Response must be a Bundle", "resource": "Bundle" } },
    { "assert": { "description": "Bundle must be a searchset", "expression": "type = 'searchset'" } },
    { "assert": { "description": "Observation must appear once as the match", "expression": "entry.where(resource.id = 'ignixa-inc-obs1' and search.mode = 'match').count() = 1" } },
    { "assert": { "description": "Patient must appear once as a direct include", "expression": "entry.where(resource.id = 'ignixa-inc-pat1' and search.mode = 'include').count() = 1" } },
    { "assert": { "description": "Included resources must not affect total", "expression": "total = 1" } },
    {
      "assert": {
        "description": "Organization should be visible through Patient:organization",
        "expression": "entry.where(resource.id = 'ignixa-inc-org' and search.mode = 'include').exists()",
        "warningOnly": true
      }
    },
    {
      "assert": {
        "description": "Practitioner should be visible through Patient:general-practitioner",
        "expression": "entry.where(resource.id = 'ignixa-inc-prac1' and search.mode = 'include').exists()",
        "warningOnly": true
      }
    }
  ]
}
```

- [ ] **Step 2: Add the separate direct-plus-iterated deduplication workload**

Insert this object immediately after the branch-visibility test. Organization and Practitioner
presence is hard because `Observation:performer` directly reaches them. Their count assertions are
warning-only because those counts depend on deduplication across direct and potentially iterated
paths. As in the first query, `_total=accurate` remains in the request without an `_total`
advertisement gate.

```json
{
  "name": "direct and iterated include paths deduplicate logical resources",
  "description": "Direct performer includes make Organization and Practitioner presence portable; they do not prove either iterate branch executed. Cross-path counts remain warning-only without a profile.",
  "extension": [
    { "url": "http://ignixa.io/testscript/fhirVersions", "valueString": "4.0,4.3,5.0" },
    {
      "url": "http://ignixa.io/testscript/requiresCapability",
      "valueString": "rest.resource.where(type='Observation').interaction.where(code='search-type').exists() and (rest.resource.where(type='Observation').searchParam.where(name='_id').exists() or rest.searchParam.where(name='_id').exists()) and rest.resource.where(type='Observation').searchInclude.where($this='Observation:subject' or $this='*').exists() and rest.resource.where(type='Observation').searchInclude.where($this='Observation:performer' or $this='*').exists()"
    }
  ],
  "action": [
    {
      "operation": {
        "type": { "code": "search" },
        "resource": "Observation",
        "params": "?_id=ignixa-inc-obs1&_include=Observation:subject&_include=Observation:performer&_include:iterate=Patient:organization&_include:iterate=Patient:general-practitioner&_total=accurate",
        "responseId": "inc-direct-iterated-dedup",
        "description": "Search with direct subject and performer plus both iterate branches"
      }
    },
    { "assert": { "description": "Search must return HTTP 200", "responseCode": "200" } },
    { "assert": { "description": "Response must be a Bundle", "resource": "Bundle" } },
    { "assert": { "description": "Bundle must be a searchset", "expression": "type = 'searchset'" } },
    { "assert": { "description": "Observation must appear once as the match", "expression": "entry.where(resource.id = 'ignixa-inc-obs1' and search.mode = 'match').count() = 1" } },
    { "assert": { "description": "Patient direct include must appear once", "expression": "entry.where(resource.id = 'ignixa-inc-pat1' and search.mode = 'include').count() = 1" } },
    { "assert": { "description": "Direct performer Organization must be included", "expression": "entry.where(resource.id = 'ignixa-inc-org' and search.mode = 'include').exists()" } },
    { "assert": { "description": "Direct performer Practitioner must be included", "expression": "entry.where(resource.id = 'ignixa-inc-prac1' and search.mode = 'include').exists()" } },
    { "assert": { "description": "Included resources must not affect total", "expression": "total = 1" } },
    {
      "assert": {
        "description": "Organization should appear once across direct and potentially iterated paths",
        "expression": "entry.where(resource.id = 'ignixa-inc-org').count() = 1",
        "warningOnly": true
      }
    },
    {
      "assert": {
        "description": "Practitioner should appear once across direct and potentially iterated paths",
        "expression": "entry.where(resource.id = 'ignixa-inc-prac1').count() = 1",
        "warningOnly": true
      }
    }
  ]
}
```

- [ ] **Step 3: Confirm the existing fixture remains unchanged**

Inspect `obs1`, `pat1`, `org`, and `prac1` and verify these exact references remain:

```text
Observation/ignixa-inc-obs1.subject -> Patient/ignixa-inc-pat1
Observation/ignixa-inc-obs1.performer -> Practitioner/ignixa-inc-prac1
Observation/ignixa-inc-obs1.performer -> Organization/ignixa-inc-org
Patient/ignixa-inc-pat1.managingOrganization -> Organization/ignixa-inc-org
Patient/ignixa-inc-pat1.generalPractitioner -> Practitioner/ignixa-inc-prac1
```

Expected: no fixture, setup, or teardown diff. The first query establishes branch-only visibility by omitting the direct performer include; the second query records cross-path deduplication without pretending resource presence proves branch execution.

- [ ] **Step 4: Parse the includes suite**

Run:

```powershell
Get-Content src\Core\Ignixa.TestScript.Suites\testscripts\Search\includes.json -Raw |
  ConvertFrom-Json |
  Out-Null
dotnet test test\Ignixa.TestScript.Tests\Ignixa.TestScript.Tests.csproj `
  --framework net10.0 `
  --filter "FullyQualifiedName~ConformanceScriptParseTests" `
  --logger "console;verbosity=minimal"
```

Expected: JSON parsing exits cleanly; the parser suite ends with `Failed: 0` and no warnings for `Search/includes.json`.

- [ ] **Step 5: Execute include branch and dedup behavior against the R4 E2E target**

Run:

```powershell
$reportPath = Join-Path $env:TEMP 'ignixa-includes-conformance.json'
$env:IGNIXA_RUN_CONFORMANCE = 'true'
$env:IGNIXA_CONFORMANCE_REPORT_PATH = $reportPath
dotnet test test\Ignixa.Api.E2ETests\Ignixa.Api.E2ETests.csproj `
  --framework net10.0 `
  --filter "FullyQualifiedName~TestScriptConformanceReportTests.GivenConformanceRunEnabled_WhenRunningRepositoryTestScripts_ThenWritesLatestReport" `
  --logger "console;verbosity=minimal"
$results = @((Get-Content $reportPath -Raw | ConvertFrom-Json).results |
  Where-Object { $_.file -eq 'Search/includes.json' })
if (-not $results) { throw 'Search/includes.json was not executed' }
$blocking = @($results | Where-Object { $_.status -in @('fail', 'error') })
if ($blocking) { throw 'Search/includes.json has a hard conformance failure' }
```

Expected: `dotnet test` ends with `Failed: 0`; the report contains `Search/includes.json`; hard direct-subject, direct-performer, mode, and total assertions pass. Iterate-dependent visibility and cross-path counts may report warnings but cannot be promoted or weakened within this task.

- [ ] **Step 6: Commit split include coverage**

```powershell
git add src\Core\Ignixa.TestScript.Suites\testscripts\Search\includes.json
git commit -m "Split include branch and dedup coverage"
```

### Task 7: Run static corpus and guard validation

**Files:**
- Test: `test/Ignixa.TestScript.Tests/Conformance/ConformanceScriptParseTests.cs`
- Test: `test/Ignixa.RepoGuards.Tests/ConformanceSuiteExtensionGuardTests.cs`
- Conditional create: `test/Ignixa.RepoGuards.Tests/RepoRootTests.cs`
- Conditional modify: `test/Ignixa.RepoGuards.Tests/RepoRoot.cs`

- [ ] **Step 1: Parse all five JSON surfaces directly**

Run:

```powershell
$suiteFiles = @(
  'src\Core\Ignixa.TestScript.Suites\testscripts\Search\last-updated.json',
  'src\Core\Ignixa.TestScript.Suites\testscripts\CRUD\history.json',
  'src\Core\Ignixa.TestScript.Suites\testscripts\Search\projection.json',
  'src\Core\Ignixa.TestScript.Suites\testscripts\Search\query-composition.json',
  'src\Core\Ignixa.TestScript.Suites\testscripts\Search\includes.json'
)

foreach ($suiteFile in $suiteFiles) {
  Get-Content $suiteFile -Raw | ConvertFrom-Json | Out-Null
  Write-Host "Parsed $suiteFile"
}
```

Expected: five `Parsed ...` lines and no `ConvertFrom-Json` error.

- [ ] **Step 2: Run the complete TestScript parser guard**

Run:

```powershell
dotnet test test\Ignixa.TestScript.Tests\Ignixa.TestScript.Tests.csproj `
  --framework net10.0 `
  --filter "FullyQualifiedName~ConformanceScriptParseTests" `
  --logger "console;verbosity=minimal"
```

Expected: `Failed: 0`; every corpus file parses; neither the three new files nor the two modified files emit a parser warning.

- [ ] **Step 3: Run the extension guard**

Run:

```powershell
dotnet test test\Ignixa.RepoGuards.Tests\Ignixa.RepoGuards.Tests.csproj `
  --framework net10.0 `
  --filter "FullyQualifiedName~ConformanceSuiteExtensionGuardTests.GivenConformanceSuites_WhenReadingExtensionUrls_ThenAllAreImplementedByTheEngine" `
  --logger "console;verbosity=minimal"
```

Expected: `Passed! - Failed: 0, Passed: 1`. The suites use only `fhirVersions` and `requiresCapability`; no guard source change is required.

- [ ] **Step 4: Conditionally make repository-root discovery worktree-safe with focused TDD**

Run this step only if the repository-guard project fails because a Git worktree has a `.git` file
rather than a `.git` directory. First add focused `RepoRootTests` coverage for both marker forms:

```csharp
[Fact]
public void GivenNestedPathUnderGitFile_WhenFindingRepoRoot_ThenReturnsMarkedRoot()
{
    var root = CreateFixtureRoot();
    try
    {
        var nestedPath = Directory.CreateDirectory(Path.Combine(root, "nested", "path")).FullName;
        File.WriteAllText(Path.Combine(root, ".git"), "gitdir: unused");

        RepoRoot.Find(nestedPath).ShouldBe(root);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

[Fact]
public void GivenNestedPathUnderGitDirectory_WhenFindingRepoRoot_ThenReturnsMarkedRoot()
{
    var root = CreateFixtureRoot();
    try
    {
        var nestedPath = Directory.CreateDirectory(Path.Combine(root, "nested", "path")).FullName;
        Directory.CreateDirectory(Path.Combine(root, ".git"));

        RepoRoot.Find(nestedPath).ShouldBe(root);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

private static string CreateFixtureRoot() =>
    Directory.CreateDirectory(
        Path.Combine(AppContext.BaseDirectory, $"repo-root-tests-{Guid.NewGuid():N}")).FullName;
```

Run `RepoRootTests` to establish the `.git`-file failure, then make only this production change in
`RepoRoot.Find(string startDirectory)`:

```csharp
while (dir is not null &&
       !Directory.Exists(Path.Combine(dir.FullName, ".git")) &&
       !File.Exists(Path.Combine(dir.FullName, ".git")))
{
    dir = dir.Parent;
}
```

Re-run:

```powershell
dotnet test test\Ignixa.RepoGuards.Tests\Ignixa.RepoGuards.Tests.csproj `
  --framework net10.0 `
  --filter "FullyQualifiedName~RepoRootTests" `
  --logger "console;verbosity=minimal"
```

Expected: `Failed: 0, Passed: 2`. Keep this fix limited to `RepoRoot.cs` and `RepoRootTests.cs`; do not
broaden it into the separate `GitIgnoreSourcePathsTests` helper.

- [ ] **Step 5: Verify cross-version declarations and forbidden vendor patterns**

Run:

```powershell
$suiteFiles = @(
  'src\Core\Ignixa.TestScript.Suites\testscripts\Search\last-updated.json',
  'src\Core\Ignixa.TestScript.Suites\testscripts\CRUD\history.json',
  'src\Core\Ignixa.TestScript.Suites\testscripts\Search\projection.json',
  'src\Core\Ignixa.TestScript.Suites\testscripts\Search\query-composition.json',
  'src\Core\Ignixa.TestScript.Suites\testscripts\Search\includes.json'
)
$text = $suiteFiles | ForEach-Object { Get-Content $_ -Raw }

if ($text -match '"valueString"\s*:\s*"(4\.0|4\.3|5\.0)"') {
  throw 'A new single-version declaration was found; new tests require 4.0,4.3,5.0.'
}

$forbidden = @('_source', 'bulk-delete', 'continuation-token', 'x-ms-', 'ct=')
foreach ($token in $forbidden) {
  if ($text -match [regex]::Escape($token)) {
    throw "Forbidden vendor-specific pattern found: $token"
  }
}

Write-Host 'Cross-version and vendor-pattern checks passed'
```

Expected: `Cross-version and vendor-pattern checks passed`. The history TestScript may contain an opaque URL returned at runtime, but its source must not name or inspect a continuation-token key.

- [ ] **Step 6: Verify the implementation file boundary**

Run from the implementation branch:

```powershell
$base = 'feature/conformance-suite-consolidation'
$allowed = @(
  'docs/superpowers/plans/2026-07-21-search-query-conformance-coverage.md',
  'docs/superpowers/specs/2026-07-21-search-query-conformance-coverage-design.md',
  'src/Core/Ignixa.TestScript.Suites/testscripts/Search/last-updated.json',
  'src/Core/Ignixa.TestScript.Suites/testscripts/CRUD/history.json',
  'src/Core/Ignixa.TestScript.Suites/testscripts/Search/projection.json',
  'src/Core/Ignixa.TestScript.Suites/testscripts/Search/query-composition.json',
  'src/Core/Ignixa.TestScript.Suites/testscripts/Search/includes.json',
  'test/Ignixa.TestScript.Tests/Evaluation/VariableExtractorTests.cs',
  'test/Ignixa.RepoGuards.Tests/RepoRoot.cs',
  'test/Ignixa.RepoGuards.Tests/RepoRootTests.cs'
)

$changed = @()
$changed += git diff --name-only "$base...HEAD"
if ($LASTEXITCODE -ne 0) { throw 'Failed to inspect committed paths.' }
$changed += git diff --cached --name-only
if ($LASTEXITCODE -ne 0) { throw 'Failed to inspect staged paths.' }
$changed += git diff --name-only
if ($LASTEXITCODE -ne 0) { throw 'Failed to inspect unstaged paths.' }
$changed += git ls-files --others --exclude-standard
if ($LASTEXITCODE -ne 0) { throw 'Failed to inspect untracked paths.' }
$changed = @($changed | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)

if ($changed.Count -eq 0) {
  throw 'No committed, staged, unstaged, or untracked paths were inspected.'
}

$unexpected = $changed | Where-Object { $_ -notin $allowed }

if ($unexpected) {
  throw "Out-of-scope files changed:`n$($unexpected -join "`n")"
}

Write-Host 'Implementation file boundary passed'
```

Expected: `Implementation file boundary passed` after inspecting the union of committed, staged,
unstaged, and untracked paths. Evaluator, model, and parser production files are not allowed.

### Task 8: Run targeted conformance and broader verification

**Files:**
- Test: `test/Ignixa.Api.E2ETests/Conformance/TestScriptConformanceReportTests.cs`
- Test: `test/Ignixa.TestScript.Tests/Ignixa.TestScript.Tests.csproj`
- Test: `test/Ignixa.RepoGuards.Tests/Ignixa.RepoGuards.Tests.csproj`
- Test: `All.sln`

- [ ] **Step 1: Run the isolated SQL conformance E2E test**

The current repository E2E target is FHIR R4 (`TestScriptConformanceReportTests.FhirVersion = "4.0"`). There is no existing R4B or R5 E2E target in this test project, so do not invent one in this work.

With the same SQL Server and `TEST_SQL_CONNECTION_STRING` used by `.github/workflows/ci.yml`, run:

```powershell
$reportPath = Join-Path $env:TEMP 'ignixa-search-query-conformance.json'
$env:IGNIXA_RUN_CONFORMANCE = 'true'
$env:IGNIXA_CONFORMANCE_REPORT_PATH = $reportPath

dotnet test test\Ignixa.Api.E2ETests\Ignixa.Api.E2ETests.csproj `
  --framework net10.0 `
  --filter "FullyQualifiedName~TestScriptConformanceReportTests.GivenConformanceRunEnabled_WhenRunningRepositoryTestScripts_ThenWritesLatestReport" `
  --logger "console;verbosity=minimal"
```

Expected: `Failed: 0`; the isolated conformance fixture starts; no suite parse or evaluator infrastructure error occurs; `$reportPath` is created.

- [ ] **Step 2: Fail the targeted check on hard failures or missing files**

Run:

```powershell
$reportPath = Join-Path $env:TEMP 'ignixa-search-query-conformance.json'
$expectedFiles = @(
  'Search/last-updated.json',
  'CRUD/history.json',
  'Search/projection.json',
  'Search/query-composition.json',
  'Search/includes.json'
)
$report = Get-Content $reportPath -Raw | ConvertFrom-Json
$selected = @($report.results | Where-Object { $_.file -in $expectedFiles })
$observedFiles = @($selected.file | Sort-Object -Unique)
$missing = @($expectedFiles | Where-Object { $_ -notin $observedFiles })
$blocking = @($selected | Where-Object { $_.status -in @('fail', 'error') })

if ($missing) {
  throw "Conformance report omitted targeted files: $($missing -join ', ')"
}

if ($blocking) {
  $details = $blocking | ForEach-Object {
    "$($_.file): $($_.error.assertion) -- $($_.error.received)"
  }
  throw "Targeted hard conformance failures:`n$($details -join "`n")"
}

$selected |
  Sort-Object file, id |
  Format-Table file, id, status -AutoSize
```

Expected: all five relative file names appear; no selected result has `fail` or `error`; warning-only observations may report their non-failing warning status. If a hard assertion fails, keep the assertion unchanged and diagnose the server separately.

- [ ] **Step 3: Run the focused TestScript and repository-guard projects**

Run:

```powershell
dotnet test test\Ignixa.TestScript.Tests\Ignixa.TestScript.Tests.csproj `
  --framework net10.0 `
  --logger "console;verbosity=minimal"

dotnet test test\Ignixa.RepoGuards.Tests\Ignixa.RepoGuards.Tests.csproj `
  --framework net10.0 `
  --logger "console;verbosity=minimal"
```

Expected: both commands end with `Failed: 0`; the evaluator URL regression, parser corpus, and extension guard all pass.

- [ ] **Step 4: Run the solution build**

Run:

```powershell
dotnet build All.sln --configuration Release --no-restore
```

Expected: `Build succeeded.` with `0 Warning(s)` and `0 Error(s)`.

- [ ] **Step 5: Run the full solution test suite**

Run:

```powershell
dotnet test All.sln `
  --configuration Release `
  --no-build `
  --logger "console;verbosity=minimal"
```

Expected: every test project completes with `Failed: 0`.

- [ ] **Step 6: Run compatibility checks for the cross-version suite metadata**

Run:

```powershell
.\run-compat-tests.ps1
```

Expected: the compatibility script exits `0`; no R4, R4B, or R5 compatibility regression is reported.

- [ ] **Step 7: Commit any verification-only correction**

Normally this step has no changes. If a parser, guard, or test exposed a defect in one of the allowed implementation files, fix only that defect, repeat the failing command, then commit with:

```powershell
git add src\Core\Ignixa.TestScript.Suites\testscripts\Search\last-updated.json `
  src\Core\Ignixa.TestScript.Suites\testscripts\CRUD\history.json `
  src\Core\Ignixa.TestScript.Suites\testscripts\Search\projection.json `
  src\Core\Ignixa.TestScript.Suites\testscripts\Search\query-composition.json `
  src\Core\Ignixa.TestScript.Suites\testscripts\Search\includes.json `
  test\Ignixa.TestScript.Tests\Evaluation\VariableExtractorTests.cs `
  test\Ignixa.RepoGuards.Tests\RepoRoot.cs `
  test\Ignixa.RepoGuards.Tests\RepoRootTests.cs
git commit -m "Correct search conformance verification"
```

Do not use this commit to add infrastructure, reference documentation, unrelated cleanup, or assertion downgrades.

## Traceability

| Approved specification requirement | Plan task and exact evidence |
|---|---|
| Fixed broad `_lastUpdated` lower and upper bounds | Task 2, Steps 1-2: POST/server-assigned-id fixture plus hard membership and empty scoped upper-bound assertions. |
| Bounded and contradictory repeated `_lastUpdated` ranges | Task 2, Step 3: warning-only match-set conclusions with hard HTTP and Bundle shape; broad-range membership correlates the captured id and suite identifier on the same entry. |
| At least three history versions from actual content changes | Task 3, Steps 1-2: add then replace `Patient.active`; hard `entry.count()` greater than `2`. |
| `_count=1`, `Bundle.type='history'`, page maximum, and required next link | Task 3, Step 4: hard first-page and second-page assertions; `_count` is not an advertised-parameter gate. |
| Non-empty and distinct followed history page | Task 3, Steps 3-4: extract `histAFirstVersionId`, hard-assert `entry.exists()` on both pages, and require page 2's first version id to differ. |
| Extract and follow opaque next URL unchanged | Task 1 regression guard and Task 3 `histANextUrl` complete `operation.url`. |
| Supported history sort and `_summary=count` behavior remain hard | Task 3 preserves ascending/descending ordering and summary total/no-entry assertions; only unsupported/server-specific rejection behavior such as `_sort=_id` remains warning-only. |
| Existing evaluator behavior is sufficient | Task 1, Steps 1-2: the opaque absolute-URL regression passes; evaluator, model, and parser production code remains unchanged. |
| Projection fixed id and lifecycle | Task 4, Step 1: `ignixa-projection-pat1`, PUT update-create setup with captured `setup-response` and no setup assertions, delete teardown, and suite `updateCreate` plus `delete` gate. |
| Projection setup response validation | Task 4, Step 1: the dedicated first `4.0,4.3,5.0` test has one source-correlated hard-outcome `assertionAnyOfGroup` with exactly two warning-only members, exact 200 and exact 201; no `okay`, 202, 204, or production change. |
| Every projection search uses fixed `_id` scope | Task 4, Steps 2-4: every query contains `_id=ignixa-projection-pat1`; none uses `identifier` for scoping. |
| Projection control gates stay narrow | Task 4, Steps 2-3 require only Patient `search-type`; Step 4 additionally requires resource- or system-level `_lastUpdated`, never `_elements`/`_summary` advertisement. |
| Requested and mandatory projection fields hard | Task 4, Steps 2-3: hard fixed resource id and required requested/summary fields. |
| Unrequested, ordinary, narrative, and non-summary absence warning-only | Task 4, Steps 2-3: every extras-permitted absence assertion carries `warningOnly`. |
| Exact SUBSETTED implications warning-only | Task 4, Steps 2-3: exact system/code conditionals carry `warningOnly` because the normative language is SHOULD. |
| `_summary=count` with `_lastUpdated` | Task 4, Step 4: exact HTTP 200 plus hard `total=1`, no match entries, and only outcome-mode `OperationOutcome` entries behind the `_lastUpdated` gate. |
| Two matches plus status, code, and practitioner decoys | Task 5, Step 1: five DiagnosticReports with one independently failing predicate per decoy; setup has seven PUTs with unique response ids and no assertions, followed by a first FHIR-version-gated test containing seven unique hard-outcome `assertionAnyOfGroup` groups. Each group correlates exactly two warning-only members by `sourceId`, exact 200 and exact 201, so later membership and exclusion checks are non-vacuous. |
| Parser-supported fixture-status alternatives | Task 5, Step 1: groups are placed in the first test because `assertionAnyOfGroup` is supported in test actions rather than setup; no 202, 204, or `okay` alternative and no parser/evaluator production change. |
| Suite-level cross-version query-composition gate | Task 5, Step 1: one pre-setup `requiresCapability` expression checks `fhirVersion.startsWith('4.0')`, `fhirVersion.startsWith('4.3')`, or `fhirVersion.startsWith('5.0')` and lifecycle support; no root `fhirVersions` extension or engine support change. |
| Typed chain, `_lastUpdated`, unique sort, and `_count=10` | Task 5, Step 2: exact query, exact HTTP 200, non-vacuous match criteria, ordered membership, and exclusion assertions. |
| `_count` is workload composition, not paging | Task 5 descriptions and Task 5, Step 2: both matches and all decoys fit on one page; no next-link assertion. |
| Repeated-range composition remains warning-only | Task 5, Step 3: hard exact HTTP 200/response shape; result count, membership, criteria, order, and exclusion warning-only. |
| Branch visibility uses direct subject plus two iterate branches | Task 6, Step 1: performer is absent and both branch targets are warning-only. |
| Direct-plus-iterated cross-path dedup is separate | Task 6, Step 2: performer is present; cross-path counts are warning-only. |
| Direct performer results do not prove branch execution | Task 6 descriptions and Step 3 fixture/reference audit. |
| Existing include fixture semantics stay unchanged | Task 6, Step 3: exact reference graph and no fixture/setup/teardown diff. |
| `_total=accurate` without advertisement gating | Task 6, Steps 1-2: both requests retain `_total=accurate`; neither capability expression requires `_total`. |
| Worktree-safe static guard stays minimal | Task 7, Step 4: focused `RepoRootTests` cover `.git` file/directory markers before the `RepoRoot` change; `GitIgnoreSourcePathsTests` is untouched. |
| Exact cross-version applicability and narrow gates | Tasks 2-6 retain test-level `4.0,4.3,5.0`; Task 5 also uses the exact pre-setup `fhirVersion` prefix gate, and Task 7 checks extension vocabulary. |
| No proprietary search or continuation-token semantics | Task 3 treats URL as opaque; Task 7 scans forbidden patterns. |
| Targeted parsing, guard, E2E, then broader verification | Tasks 7-8 in that order. |
| No assertion weakening to make Ignixa pass | Non-negotiable constraints and Task 8, Step 2 failure handling. |

## Completion gate

- Every checkbox in Tasks 1-8 is complete.
- The implementation diff is limited to the exact boundary list; evaluator, model, and parser
  production files are absent, and RepoRoot scope remains limited to `RepoRoot.cs`/`RepoRootTests.cs`.
- All five suite files parse without warnings and use only implemented extensions.
- The targeted conformance report contains all five files and no hard `fail` or `error`.
- The solution builds with zero warnings and all solution tests pass.
- No hard assertion was changed to warning-only in response to an Ignixa failure.
