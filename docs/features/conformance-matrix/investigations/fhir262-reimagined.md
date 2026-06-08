# Investigation: Reimagining fhir262 with Our TestScript Engine + FhirFaker

**Feature**: conformance-matrix
**Status**: In Progress
**Created**: 2026-06-08

## Approach

Use our existing `Ignixa.TestScript` engine and `Ignixa.FhirFakes` library to build a FHIR conformance matrix equivalent to [fhir262](https://healthsamurai.github.io/fhir262/). The core idea: write test cases as FHIR TestScript JSON resources, execute them against containerised FHIR servers via a multi-impl CLI runner, merge per-impl reports into a comparison matrix, and publish a static site to GitHub Pages.

This differs from fhir262's approach (TypeScript/Jest + testcontainers-node) in a key way: **the tests themselves are FHIR TestScript resources**, which are the specification-defined format for FHIR conformance tests. Any FHIR server could run our suite against itself.

### What We'd Build

```
TestScript JSON files (tests/)
         │
         ▼
CLI Runner (dotnet run -- --impl hapi --out hapi.json)
         │
         ├─── Testcontainers.NET (spin up Docker FHIR server)
         ├─── ITestRequestProvider (HTTP to running server)
         └─── TestScriptEvaluator (our existing engine)
                     │
                     ▼
          per-impl JSON report (.results/hapi.json)
                     │
                     ▼
          Matrix Builder (merge all impl reports)
                     │
                     ▼
          dist/runs/run-YYYY-MM-DD.json
                     │
                     ▼
          Static site (UI matrix table) → GitHub Pages
```

### Layer Breakdown

**Tests layer** — FHIR TestScript JSON files organized by category:
```
tests/
  CRUD/
    basic.json           # create/read/update/delete semantics
  Search/
    basic.json           # token, string, date, reference search
    modifiers.json       # :exact, :contains, :missing, :not, :of-type
    sort.json            # _sort, _count, paging
    joins.json           # AND, OR, comma syntax
    intervals.json       # date range prefixes
  Validation/
    validate-op.json     # $validate operation
    primitives.json      # primitive type violations
    shapes.json          # structure violations
```

**Runner layer** — `Ignixa.ConformanceMatrix.Cli`:
```
dotnet run -- run --impl hapi --out .results/hapi.json
```
- Loads all TestScript JSON files
- Starts containerised server via Testcontainers.NET
- Constructs `TestScriptEvaluator` with `HttpTestRequestProvider` pointing at the container
- Executes each TestScript, collects `TestScriptReport`
- Writes per-impl JSON (maps to fhir262's `ImplReport` shape)

**Matrix builder** — port of fhir262's `framework/build-run.ts` to C#:
- Merges per-impl reports
- Produces `Run` (meta + impls + modules + statuses matrix)
- Writes `dist/runs/run-YYYY-MM-DD.json` and `dist/runs/index.json`

**UI** — adapt fhir262's React matrix table:
- Rows = test cases, columns = FHIR implementations
- Cells = pass / fail / skip + duration
- Error detail popover on failure
- Historical run picker
- Zero build step (UMD React inline in HTML)

**GitHub Actions** — parallel jobs per impl, merge on completion, deploy to Pages.

---

## Tradeoffs

| Pros | Cons |
|------|------|
| TestScript is the FHIR-spec-defined test format — portable across ecosystems | TestScript JSON is verbose; authoring is slower than TypeScript |
| Engine already exists (zero-cost execution layer) | FhirPathCriteria is not yet implemented — blocks non-trivial assertions |
| FhirFaker generates realistic fixtures (vs fhir262's minimal inline JSON) | Testcontainers.NET integration doesn't exist yet |
| Can generate FHIR TestReport resources alongside matrix JSON | FHIR operations ($validate) not in current OperationType enum |
| Tests are infrastructure-agnostic (can be run by any FHIR server's own test runner) | Multi-impl CLI runner and matrix builder need building from scratch |
| C# ecosystem (dotnet build, NuGet) is consistent with our existing toolchain | No population-isolation mechanism in current engine (tag-based isolation not implemented) |
| Variables + autocreate/autodelete handle multi-step setup idiomatically | Cross-test shared IDs (fhir262's `pA`, `pB` pattern) needs careful mapping to fixture IDs |
| FhirFaker can replace fhir262's handwritten inline JSON with richer generated fixtures | |

---

## Alignment

- [x] Follows architectural layering rules — CLI runner is an application shell, engine stays in Core
- [x] Developer Experience — `dotnet run -- --impl hapi` is consistent with existing tooling
- [ ] Specification compliance — TestScript format is FHIR R4/R5 spec; OperationType gaps remain
- [x] Consistent with existing patterns — extends `ITestRequestProvider` / `IFixtureProvider` model

---

## Evidence

### Current Engine Capabilities vs What fhir262 Needs

#### What maps directly (zero engine changes):

| fhir262 pattern | Our engine support |
|---|---|
| `create("Patient", body)` → expect 201 | `OperationExpression { Type = "create" }` + `ResponseCodeCriteria("201")` |
| `read("Patient", id)` → expect 200 | `OperationExpression { Type = "read" }` + `ResponseCodeCriteria("200")` |
| `update("Patient", id, body)` → expect 200/201 | `ResponseCodeCriteria` with `In` operator |
| `delete("Patient", id)` → expect 200/202/204 | `ResponseCodeCriteria` with `In` operator |
| `read` after delete → expect 404/410 | Same + `ResponseStatusCriteria("notFound")` / `ResponseCodeCriteria("410")` |
| Capture server-assigned `id` from response | `VariableExtraction.PathExtraction("id")` |
| Use captured `id` in subsequent URLs | `VariableResolver` (`${patientId}` substitution) |
| Multiple resources created in setup | Multiple `FixtureDefinition` with `autocreate=true` |
| Cleanup via delete | `autodelete=true` on fixtures |

#### What needs engine work before it can map:

| fhir262 assertion | Gap | Effort |
|---|---|---|
| `expect(body.gender).toBe("female")` | `FhirPathCriteria` not implemented | M |
| `expect(body.id).not.toBe(clientId)` | `FhirPathCriteria` with `NotEquals` operator | M |
| `expect(ids).toContain(pA)` | `FhirPathCriteria`: `entry.where(resource.id='${pA}').exists()` | M |
| `expect(ids).not.toContain(pB)` | `FhirPathCriteria` + `NotEquals` / `.exists() = false` | M |
| `expect((res.body as Bundle).total).toBe(3)` | `FhirPathCriteria`: `Bundle.total = 3` | M |
| `expect(idsOf(res.body)).toEqual([s2, s1, s3])` | `FhirPathCriteria` ordered match | H |
| `expect(idsOf(res.body).length).toBeLessThanOrEqual(2)` | `FhirPathCriteria` + `LessThanOrEqual` operator | M |
| `expect(res).toBeValid()` (OperationOutcome) | `FhirPathCriteria`: `OperationOutcome.issue.empty()` or `issue.where(severity='error').empty()` | M |
| `expect(res).toHaveIssueWithExpression("Patient.name")` | `FhirPathCriteria`: `issue.where(expression='Patient.name').exists()` | M |
| `instance.rest.operation("Patient", "$validate", body)` | `OperationType` enum missing `$validate` | S |
| `instance.rest.systemOperation("$validate", body)` | System-level custom operation missing | S |

Effort key: S = Small (< 4h), M = Medium (4-8h), H = Hard (8h+)

### Test Count Breakdown

| Test file | Tests | CRUD only | Needs FhirPath |
|---|---|---|---|
| CRUD/basic-test.ts | 9 | 4 | 5 |
| Search/basic.ts | ~30 | 0 | 30 |
| Search/intervals.ts | ~10 | 0 | 10 |
| validation/$validation-op.ts | 8 | 0 | 8 (+ $validate op) |
| validation/simple-cases.ts | ~5 | 0 | 5 |
| validation/primitives-test.ts | ~5 | 0 | 5 |
| validation/shapes-test.ts | ~5 | 0 | 5 |
| validation/primitive-extensions-test.ts | ~5 | 0 | 5 |
| **Total** | **~77** | **4** | **73** |

**Critical path:** FhirPathCriteria implementation unblocks 95% of tests. Everything else can be worked in parallel.

### FhirPathCriteria Implementation Path

We already have FHIRPath evaluation infrastructure (`IFhirSchemaProvider`, `element.Select()`, `element.IsTrue()`). The `FhirPathCriteria` case in `TestScriptEvaluator.VisitAssertAsync()` just throws `NotImplementedException`. The implementation shape:

```csharp
case FhirPathCriteria { Expression: var expr }:
    var source = /* last response body as TypedElement */;
    var result = source.IsTrue(expr);
    return new AssertionOutcome(result, !result ? $"FHIRPath '{expr}' evaluated to false" : null);
```

The tricky part: assertion `operator` (Equals, In, Contains, etc.) composes with FhirPath. The expression for `Contains` on entry IDs looks like:

```
entry.where(resource.id = '${pA}').exists()
```

Rather than exposing raw FhirPath to TestScript authors, we could add a `FhirPathValueCriteria` subtype that takes `(expression, operator, value)` and constructs the FhirPath internally — matching the FHIR TestScript spec's `expression` + `value` + `operator` combination.

### Testcontainers.NET Integration

```csharp
// impl/HapiImpl.cs
public class HapiServer : IFhirServerImpl
{
    public async Task<IFhirServerInstance> StartAsync(CancellationToken ct)
    {
        var postgres = new PostgreSqlBuilder().Build();
        await postgres.StartAsync(ct);
        var hapi = new ContainerBuilder()
            .WithImage("hapiproject/hapi:v8.8.0-1")
            .WithEnvironment("spring.datasource.url", postgres.GetConnectionString())
            .WithPortBinding(8080, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded("/fhir/metadata"))
            .Build();
        await hapi.StartAsync(ct);
        var baseUrl = new Uri($"http://localhost:{hapi.GetMappedPublicPort(8080)}/fhir");
        return new HapiServerInstance(hapi, postgres, baseUrl);
    }
}
```

This is the heaviest infrastructure piece. HAPI alone needs PostgreSQL as a companion container.

### Multi-Impl Runner Architecture

```
Ignixa.ConformanceMatrix.Cli/
  Program.cs            ← CLI entry: --impl, --tests, --out
  RunCommand.cs         ← orchestrates: start server → run scripts → write report
  ImplRegistry.cs       ← maps impl name → IFhirServerImpl
  MatrixBuilder.cs      ← merges per-impl reports → Run JSON
  ImplReport.cs         ← matches fhir262's ImplReport type

impl/
  HapiImpl.cs
  AidboxImpl.cs
  MedplumImpl.cs
  IgnixaImpl.cs         ← uses HttpTestRequestProvider to already-running ignixa
```

### TestScript Authoring Pattern

Example: `tests/CRUD/basic.json` — maps to fhir262's CRUD/basic-test.ts

```json
{
  "resourceType": "TestScript",
  "id": "crud-basic",
  "name": "PatientCrudBasic",
  "description": "FHIR R4 §2.36 — basic Patient CRUD semantics",
  "fixture": [
    {
      "id": "create-patient",
      "autocreate": false,
      "resource": {
        "extension": [{ "url": "http://ignixa.io/testscript/fhirfakes", "valueCode": "Patient" }]
      }
    }
  ],
  "variable": [
    { "name": "patientId", "path": "id", "sourceId": "create-response" }
  ],
  "test": [
    {
      "name": "Create returns 201 and assigns an id",
      "action": [
        {
          "operation": {
            "type": { "code": "create" },
            "resource": "Patient",
            "responseId": "create-response",
            "sourceId": "create-patient"
          }
        },
        { "assert": { "response": "created" } },
        { "assert": { "expression": "Patient.id.exists()", "sourceId": "create-response" } }
      ]
    },
    {
      "name": "Read returns the created resource",
      "action": [
        {
          "operation": {
            "type": { "code": "read" },
            "resource": "Patient",
            "params": "/${patientId}"
          }
        },
        { "assert": { "response": "okay" } },
        { "assert": { "expression": "Patient.id = '${patientId}'" } }
      ]
    }
  ]
}
```

### UI Option: Extend Docusaurus vs Adapt fhir262's React UI

| Option | Pros | Cons |
|---|---|---|
| Adapt fhir262's `ui/app.jsx` | Already works, proven schema | JSX with no build step is rough DX; orphaned from our docs site |
| Docusaurus page in `docs/site/` | Consistent with our docs; can use MDX + React | Docusaurus's table components aren't ideal for a 20-column matrix |
| Standalone Vite/React app | Best DX, modern tooling | Another build step, another toolchain |
| Port to Blazor WASM | C#-only stack | Overkill; WASM binary is large for a static table |

Recommendation: adapt fhir262's UI directly. It's 250 lines of JSX, no dependencies beyond React UMD, and already handles the exact JSON schema we'd produce.

### Comparison: fhir262 vs Our Approach

| Dimension | fhir262 (current) | Our approach |
|---|---|---|
| Test format | TypeScript/Jest | FHIR TestScript JSON |
| Test portability | JS ecosystem only | Any FHIR TestScript-capable runner |
| Data generation | Minimal inline JSON | FhirFaker (clinically realistic, demographic) |
| Server lifecycle | testcontainers-node (Node.js) | Testcontainers.NET (C#) |
| Assertions | Jest matchers (`toContain`, `toBe`) | AssertCriteria discriminated unions + FhirPath |
| Output format | fhir262 Run JSON | Same Run JSON schema (compatible UI) |
| Report format | Custom JSON only | Custom JSON + FHIR TestReport resource |
| CI | Bun + Docker | dotnet + Docker |
| Language | TypeScript | C# |

---

## Gaps to Close (Ordered by Blocking Impact)

### 1. FhirPathCriteria implementation (BLOCKS ~95% of test porting)
- Implement `FhirPathCriteria` case in `TestScriptEvaluator`
- Add `FhirPathValueCriteria` subtype for `expression + operator + value` assertions
- Update parser to handle FHIR TestScript `expression` + `value` + `operator` JSON
- **Effort**: 8-12 hours

### 2. Custom FHIR operation support (BLOCKS validation tests)
- Add `operation` and `systemOperation` OperationTypes
- Parser support for `$validate` and arbitrary operation names
- **Effort**: 4-6 hours

### 3. Multi-impl CLI runner (infrastructure)
- `Ignixa.ConformanceMatrix.Cli` project
- `IFhirServerImpl` interface + impl registry
- Per-impl report writer
- **Effort**: 6-8 hours

### 4. Testcontainers.NET integration (per-impl)
- One `IFhirServerImpl` per target server
- ~8-12 hours per complex impl (HAPI, Aidbox), ~4 hours for ignixa (already running)
- Start with HAPI as it's the reference open-source implementation

### 5. Matrix builder (C# port of build-run.ts)
- Merge per-impl `ImplReport[]` → `Run` JSON
- Historical run index (`runs/index.json`)
- **Effort**: 4-6 hours

### 6. TestScript files for fhir262 test suite
- ~77 tests across 8 test files
- Straightforward after engine gaps are closed
- **Effort**: 16-24 hours (including FhirFaker fixture setup)

### 7. UI and CI pipeline
- Adapt fhir262 UI (app.jsx)
- GitHub Actions parallel matrix job per impl
- **Effort**: 8-12 hours

**Total estimate**: 70-100 hours of implementation after design sign-off.

---

## Alternative Approaches

### A. Keep TypeScript tests, add C# impl adapters
Keep fhir262's Jest/TS test infrastructure as-is. Port only the impl adapter layer to C# (for ignixa), calling a locally-started ignixa server. Zero engine work. Gets ignixa into the comparison matrix quickly but doesn't leverage our TestScript investment.

### B. C# xUnit with TestScript types, not JSON
Write tests in C# using our model types directly (`OperationExpression`, `AssertExpression`), not JSON. We'd still generate `TestScriptReport` and the matrix. Better authoring DX than JSON, loses portability. Good if we care more about our own matrix than interoperability.

### C. Hybrid: FHIR TestScript + TypeScript runner
Keep fhir262's TypeScript/Jest runner but have it load and execute our TestScript JSON files via a thin TypeScript wrapper. Avoids Testcontainers.NET work (testcontainers-node already works). The "run" step stays in JS; our value-add is the test format and FhirFaker fixtures. Lowest infra cost, most portable tests.

---

## Verdict

*Pending evaluation — three approaches above should be compared before committing.*

The pure C# approach (described in main section) is the most architecturally coherent and leverages our existing engine investment most fully. The weakest links are FhirPathCriteria (critical path) and Testcontainers.NET setup time (high per-impl cost). The hybrid approach (C) would deliver value faster with less infrastructure risk.
