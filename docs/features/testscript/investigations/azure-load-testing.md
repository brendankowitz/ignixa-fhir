# Investigation: Azure Load Testing from TestScript

**Feature**: testscript
**Status**: Viable
**Created**: 2026-07-22

## Approach

Use Ignixa's existing TestScript parser and expression model as a compiler front end that emits a
Locust workload for Azure Load Testing. The generated workload is a performance-test derivative of
the TestScript, not a second conformance runner.

The proposed flow is:

1. Run the TestScript once with `Ignixa.TestScript` as a conformance preflight.
2. Parse the same resource with `TestScriptParser` and compile supported actions into a generated
   `locustfile.py`.
3. Upload the generated script, fixture JSON, supporting Python modules, and `requirements.txt` to
   Azure Load Testing.
4. Keep virtual users, spawn rate, duration, engine count, secrets, and performance failure criteria
   in Azure Load Testing configuration. TestScript has no load-profile model, and adding one would
   mix conformance semantics with deployment-specific performance policy.

A generated user flow would use Locust's HTTP client so every operation contributes native request
latency, throughput, and failure metrics. TestScript setup, test, and teardown actions would become
per-user sequential flows with isolated fixture and variable state. The compiler must require the
author to select the execution scope for setup and teardown because TestScript's once-per-suite
semantics do not automatically map to many virtual users.

The initial compiler should support the useful load-test subset:

- HTTP operations, headers, fixtures, variables, and sequential action ordering
- Status code, response code, content type, header, and basic expression assertions
- Ignixa `parametrize`, `fhirVersions`, and `requiresCapability` extensions resolved at compile or
  preflight time
- FHIRPath assertions through `fhirpathpy` only after compatibility tests establish the supported
  expression subset

Profile validation, cross-source comparison, multi-party origin/destination behavior, warning-only
reporting, and other assertions without a faithful Locust equivalent should fail compilation
explicitly rather than disappear. Generated code should mark supported assertion failures through
Locust's response failure API so Azure's aggregate error-rate criteria can gate the run.

## Azure Load Testing Fit

Azure Load Testing currently supports URL-based tests, Apache JMeter, and Locust. Locust is the only
Python framework option; the service does not run arbitrary Python test frameworks or custom load
engines. The managed engine currently uses Azure Linux, Python 3.9.19, Locust 2.33.2, and
Standard_D4d_v4 virtual machines.

Locust is the better target than JMeter for this feature:

| Target | Fit |
|---|---|
| Locust | Generated Python can preserve sequential state, fixtures, variables, dynamic extraction, and custom FHIR assertions without generating a large XML document or embedded JVM scripts. |
| JMeter | Viable for HTTP operations and simple assertions, but rich TestScript behavior would require generated JSR223 code and would be harder to inspect and maintain. |
| URL-based tests | Too limited for TestScript variables, fixtures, multi-step state, and FHIR-aware assertions. |
| Custom .NET runner | Not accepted as an Azure Load Testing engine. It would need self-hosted compute and would lose the managed service's native load orchestration and reporting. |

Azure's service-level failure criteria cover aggregate metrics such as response time, requests per
second, latency, and error percentage. They do not evaluate FHIRPath or TestScript assertions.
Semantic checks must execute inside the Locust workload and be reported as request failures.

Azure runs Locust independently on each engine instance. Workloads must not rely on shared in-memory
state or counters across engines. Uploaded data can be partitioned, but resource identifiers and
cleanup must still be designed to avoid collisions between users and engines.

## Python and .NET Reuse Options

| Option | Assessment |
|---|---|
| Generate Locust from Ignixa's .NET model | **Recommended.** Reuses the authoritative parser and expression tree at build time while producing a conventional, inspectable Locust workload at run time. |
| Pure-Python TestScript runner | Possible but not recommended. `fhir.resources` can model TestScript resources and `fhirpathpy` can evaluate many FHIRPath expressions, but no maintained Python TestScript execution engine was found. Fixture, variable, operation, assertion, and reporting semantics would be a second implementation that can drift from Ignixa. FHIR-version model coverage also needs verification. |
| `pythonnet` with bundled CoreCLR | Technically plausible, not product-supported or proven in Azure Load Testing. Generic binary artifacts and native wheels are accepted, so a custom wheel could carry CoreCLR and Ignixa assemblies. The runtime size, JIT startup, Python 3.9 compatibility, native dependency probing, and undocumented engine image details make this a fragile primary design. |
| Ignixa NativeAOT library loaded with `ctypes` | The most credible bridge experiment. A Linux shared library avoids an installed CLR, and Azure's official Locust examples already use packages with native shared objects. Ignixa has not been AOT-published, however, and would need a new C ABI, UTF-8/JSON marshaling, trimming validation, and an Azure-hosted proof. This may be useful for FHIRPath parity, but it should not carry HTTP execution because those requests would bypass Locust's client metrics unless separately instrumented. |
| .NET CLI or sidecar called from Python | Not a supported Azure Load Testing extension model. Process spawning is undocumented, and the service provides no lifecycle contract for a companion daemon. |

`Ignixa.FhirPath` and `Ignixa.TestScript` are packable .NET libraries, but NuGet packaging does not
make them directly importable from Python. `pythonnet`, CoreCLR hosting, or a native ABI is still
required. These bridges are useful on controlled developer or self-hosted CI machines; they are not
the simplest managed Azure Load Testing path.

## Tradeoffs

| Pros | Cons |
|------|------|
| Reuses Ignixa's existing TestScript parser, action model, fixtures, and extension semantics instead of parsing TestScript again in Python | Generated Locust is intentionally not a full-fidelity TestScript executor |
| Produces native Azure Load Testing latency, throughput, percentile, and error-rate metrics | Load-specific execution scope and data isolation are not expressed by TestScript and require separate configuration |
| Keeps conformance preflight authoritative in .NET while making the load artifact inspectable and portable | FHIRPath behavior can drift if generated code relies on `fhirpathpy`; compatibility tests and an explicit supported subset are required |
| Locust supports stateful sequential HTTP flows and custom response failure reporting | Azure engine instances do not share runtime state |
| The design is reversible: the compiler is an adapter and does not change TestScript execution | Unsupported assertions must stop compilation or be explicitly opted out, which limits the first version |
| NativeAOT offers a future route to exact Ignixa FHIRPath behavior without hosting CoreCLR | NativeAOT compatibility and loading on Azure Load Testing remain empirical unknowns |

## Alignment

- [x] Follows architectural layering rules - the compiler can live beside `Ignixa.TestScript` and
  depend only on Core libraries; Azure-specific upload/orchestration stays in a tool or CI layer
- [x] Developer Experience - authors retain TestScript as the source and receive an ordinary Locust
  artifact plus explicit diagnostics for unsupported semantics
- [x] Specification compliance - the source remains a FHIR TestScript; generated load configuration
  is deliberately external and the original evaluator remains the conformance authority
- [x] Consistent with existing patterns - reuses `TestScriptParser`, `TestScriptDefinition`,
  `ITestScriptActionVisitor`, fixtures, FHIRPath, and the conformance CLI's preflight model

## Evidence

### Ignixa

- `src/Core/Ignixa.TestScript/Parsing/TestScriptParser.cs` parses JSON TestScript resources into the
  typed model rooted at `TestScriptDefinition`.
- `src/Core/Ignixa.TestScript/Expressions/` and
  `src/Core/Ignixa.TestScript/Evaluation/ITestScriptActionVisitor.cs` provide a natural compiler
  visitor over operation and assertion nodes.
- `src/Core/Ignixa.TestScript/Evaluation/TestScriptEvaluator.cs` executes setup, tests, and teardown
  sequentially. It records individual operation durations but has no virtual-user, arrival-rate,
  ramp, duration, or percentile model.
- `src/Core/Ignixa.TestScript/Client/ITestRequestProvider.cs` is the HTTP execution seam, while
  Locust's own HTTP client must remain the generated workload's execution seam for native metrics.
- `src/Core/Ignixa.TestScript.FhirFakes/FhirFakesFixtureProvider.cs` and
  `src/Core/Ignixa.TestScript/Fixtures/` can materialize fixture artifacts before upload.
- `src/Core/Ignixa.FhirPath/` provides Ignixa's authoritative FHIRPath parser and evaluator.
- `tools/Ignixa.ConformanceMatrix.Cli/Commands/RunCommand.cs` provides one-server, one-pass
  conformance execution but no concurrency or load controls.
- `src/Core/Ignixa.TestScript.Suites/testscripts/` contains the existing CRUD, search, bundle,
  operation, validation, regression, subscription, and Microsoft-specific TestScript suites that
  can seed compiler coverage.
- ADR 2607 establishes the Ignixa TestScript extensions and notes that TestScript has no native
  repetition mechanism. Its `parametrize` extension expands data cases, not virtual-user load.

### Azure Load Testing

- [Azure Load Testing overview](https://learn.microsoft.com/en-us/azure/app-testing/load-testing/overview-what-is-azure-load-testing)
  documents JMeter and Locust as the supported script engines.
- [Load testing concepts](https://learn.microsoft.com/en-us/azure/app-testing/load-testing/concept-load-testing-concepts)
  documents the Azure Linux VM shape, Python and Locust versions, engine behavior, and the absence
  of other test frameworks.
- [Create a Locust load test](https://learn.microsoft.com/en-us/azure/app-testing/load-testing/quickstart-create-run-load-test-with-locust)
  documents `locustfile.py`, supporting Python files, and `requirements.txt`.
- [Failure criteria](https://learn.microsoft.com/en-us/azure/app-testing/load-testing/how-to-define-test-criteria)
  are aggregate performance conditions rather than response-content assertions.
- [High-scale load tests](https://learn.microsoft.com/en-us/azure/app-testing/load-testing/how-to-high-scale-load)
  describe multi-engine execution and virtual-user distribution.
- [Upload Test File REST API](https://learn.microsoft.com/en-us/rest/api/loadtesting/dataplane/load-test-administration/upload-test-file?view=rest-loadtesting-dataplane-2026-04-01)
  accepts binary `ADDITIONAL_ARTIFACTS`, has a 50 MB per-file limit, and defines zipped artifacts.
- The [official Locust Azure examples](https://github.com/locustio/locust/tree/master/examples/azure)
  use `grpcio` and `psycopg[binary]`, demonstrating that dependencies containing native shared
  libraries can run in the managed engine. This supports, but does not prove, a custom NativeAOT
  bridge.

### Python ecosystem

- [`fhirpathpy`](https://github.com/beda-software/fhirpath-py) is the strongest located pure-Python
  FHIRPath evaluator and supports compiled expressions and multiple FHIR models. No published
  conformance score against the official HL7 FHIRPath test suite was found.
- [`fhir.resources`](https://pypi.org/project/fhir.resources/) provides typed Python resource
  models, including TestScript in supported release packages, but no execution engine.
- [`pythonnet`](https://pythonnet.github.io/) and
  [`clr-loader`](https://pythonnet.github.io/clr-loader/) can host CoreCLR from Python when an
  appropriate runtime and assemblies are available.
- [.NET Native AOT libraries](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/libraries)
  can expose unmanaged entry points from a self-contained native shared library.
- No maintained TestScript-to-Locust converter or Python TestScript execution library was found.

## Alternatives Worth Investigating

1. A minimal `TestScriptDefinition` to Locust compiler spike covering CRUD, variable extraction,
   status assertions, and fixture isolation.
2. A NativeAOT FHIRPath bridge spike that publishes a Linux shared library and loads it from a real
   Azure Load Testing Locust run.
3. A self-hosted .NET load runner using Ignixa directly if exact TestScript semantics are more
   important than Azure Load Testing's managed orchestration and reporting.

## Verdict

**Viable as a compiler, not as direct TestScript execution.** Azure Load Testing cannot consume
Ignixa TestScript resources or run the Ignixa .NET evaluator as a supported engine. The practical
design is a build-time .NET compiler that reuses Ignixa's parser and emits a deliberately constrained
Locust workload, with the original TestScript run retained as a conformance preflight.

Do not build a second full TestScript engine in Python. Use pure Python only for the generated
runtime subset and validate `fhirpathpy` compatibility before depending on it. Treat NativeAOT as a
bounded proof of concept for exact FHIRPath reuse; treat pythonnet plus bundled CoreCLR as a fallback
experiment, not an architecture.
