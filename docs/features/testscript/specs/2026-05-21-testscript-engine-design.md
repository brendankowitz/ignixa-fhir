# TestScript Execution Engine — Design Spec

**Date**: 2026-05-21
**Feature**: testscript
**Status**: Approved (revised after rubber-duck review)

## Summary

A three-phase TestScript execution engine following the Parser/Expression/Evaluator visitor pattern established across Ignixa.FhirPath, Ignixa.Search, and Ignixa.SqlOnFhir.

## Scope

- Parse FHIR TestScript resources (R4, R4B, R5) from JSON into an expression tree
- Evaluate the expression tree via an async visitor pattern with immutable execution state
- Support both HTTP and in-process execution modes via `IFhirClient` abstraction
- Pluggable FhirFakes integration for fixture generation (opt-in package)
- Output: FHIR TestReport resource + xUnit test integration
- Report output: console, TestReport resource, JUnit XML
- XML parsing deferred to a later phase

## Project Structure

```
src/Core/Ignixa.TestScript/                 — Core: parser, expressions, evaluator
src/Core/Ignixa.TestScript.FhirFakes/       — FhirFakes fixture provider (opt-in)
src/Core/Ignixa.TestScript.XUnit/           — xUnit adapter (TestScriptTheoryData, runner)
test/Ignixa.TestScript.Tests/               — Unit and integration tests
```

## Architecture

### Phase 1: Parse (JSON → Expression Tree)

```
TestScriptParser.Parse(json)
    → TestScriptDefinition
        ├── Metadata (name, status, description, url)
        ├── ProfileReference[] (canonical URLs for validation)
        ├── FixtureDefinition[] (id, resource?, autocreate, autodelete)
        ├── VariableDefinition[]
        ├── SetupPhase (ActionExpression[])
        ├── TestPhase[] (name, ActionExpression[])
        └── TeardownPhase (ActionExpression[])
```

**Key types:**

```csharp
// Abstract base for all actions (same pattern as FhirPath Expression)
public abstract record ActionExpression
{
    public ISourcePositionInfo? Location { get; init; }
    public abstract TOutput AcceptVisitor<TContext, TOutput>(
        ITestScriptActionVisitor<TContext, TOutput> visitor, TContext context);
}

// Concrete expression types
public sealed record OperationExpression : ActionExpression
{
    public required string Type { get; init; }        // "create", "read", "update", "delete", "search"
    public string? Resource { get; init; }            // "Patient", "Observation"
    public string? Url { get; init; }                 // "${base}/Patient/${patientId}"
    public string? Params { get; init; }              // query parameters (distinct from full url)
    public HttpMethod? Method { get; init; }          // explicit HTTP verb override
    public string? Accept { get; init; }              // expected response format
    public string? ContentType { get; init; }         // request body format
    public string? SourceId { get; init; }            // fixture ID for request body
    public string? TargetId { get; init; }            // fixture ID to store response
    public string? ResponseId { get; init; }          // ID to reference this response
    public string? RequestId { get; init; }           // ID to reference this request later
    public string? Label { get; init; }               // human-readable label
    public int? Destination { get; init; }            // target server index (multi-server)
    public int? Origin { get; init; }                 // origin server index (multi-server)
    public IReadOnlyList<HeaderExpression> Headers { get; init; }
    public string? Description { get; init; }
    public bool EncodeRequestUrl { get; init; } = true;
}

public sealed record AssertExpression : ActionExpression
{
    public string? Response { get; init; }            // "okay", "created", "noContent"
    public string? ResponseCode { get; init; }        // "200", "201"
    public string? ContentType { get; init; }
    public string? Expression { get; init; }          // FHIRPath expression
    public string? Path { get; init; }                // XPath/FHIRPath for extraction
    public string? Value { get; init; }               // expected value
    public string? SourceId { get; init; }            // fixture to assert against
    public string? CompareToSourceId { get; init; }
    public string? CompareToSourceExpression { get; init; }
    public string? CompareToSourcePath { get; init; }
    public string? ValidateProfileId { get; init; }
    public string? Resource { get; init; }            // expected resource type
    public string? MinimumId { get; init; }
    public string? HeaderField { get; init; }
    public string? RequestMethod { get; init; }       // assert on request method
    public string? RequestUrl { get; init; }          // assert on request URL
    public bool? NavigationLinks { get; init; }       // validate bundle nav links
    public AssertOperator Operator { get; init; }     // equals, notEquals, in, contains, greaterThan, lessThan, empty, notEmpty
    public bool WarningOnly { get; init; }
    public string? Label { get; init; }
    public string? Description { get; init; }
    public AssertDirection Direction { get; init; } = AssertDirection.Response;
}

public sealed record HeaderExpression
{
    public required string Field { get; init; }
    public required string Value { get; init; }
}
```

**Parser implementation:**
- Uses `System.Text.Json.Nodes.JsonNode` (consistent with Ignixa.Serialization)
- Validates required fields, emits parse errors for malformed TestScripts
- Normalizes version differences (R4 vs R5 field names) into unified expression tree
- Returns `ParseResult<TestScriptDefinition>` with errors/warnings

### Phase 2: Evaluate (Expression Tree → Results)

**Async visitor interface (with explicit CancellationToken):**

```csharp
public interface ITestScriptActionVisitor
{
    ValueTask<ExecutionContext> VisitOperationAsync(
        OperationExpression expression, ExecutionContext context, CancellationToken ct);
    ValueTask<ExecutionContext> VisitAssertAsync(
        AssertExpression expression, ExecutionContext context, CancellationToken ct);
}
```

**Evaluator:**

```csharp
public class TestScriptEvaluator : ITestScriptActionVisitor
{
    private readonly IFhirClientRegistry _clientRegistry;
    private readonly IFhirPathEvaluator _fhirPathEvaluator;
    private readonly IFixtureProvider _fixtureProvider;
    private readonly IFhirSchemaProvider _schemaProvider;
    private readonly IFhirResourceValidator _validator;
    private readonly ITestScriptResultRecorder _recorder;

    public async Task<TestScriptReport> ExecuteAsync(
        TestScriptDefinition definition,
        CancellationToken cancellationToken);
}
```

**Execution context (fully immutable — no mutable report):**

```csharp
public sealed record ExecutionContext
{
    public required IFhirClientRegistry ClientRegistry { get; init; }
    public FhirResponse? LastResponse { get; init; }
    public FhirRequest? LastRequest { get; init; }
    public ImmutableDictionary<string, string> Variables { get; init; }
    public ImmutableDictionary<string, JsonNode> Fixtures { get; init; }
    public ImmutableDictionary<string, FhirResponse> ResponseHistory { get; init; }
    public ImmutableDictionary<string, FhirRequest> RequestHistory { get; init; }

    public ExecutionContext WithResponse(string? responseId, FhirResponse response) => ...;
    public ExecutionContext WithRequest(string? requestId, FhirRequest request) => ...;
    public ExecutionContext WithVariable(string name, string value) => ...;
    public ExecutionContext WithFixture(string id, JsonNode resource) => ...;
}
```

**Result recording (separate mutable concern):**

```csharp
public interface ITestScriptResultRecorder
{
    void RecordOperationResult(string? label, OperationOutcome outcome);
    void RecordAssertionResult(string? label, AssertionOutcome outcome);
    void BeginPhase(TestPhaseType phase, string? name);
    void EndPhase();
    TestScriptReport Build();
}
```

### IFhirClient Abstraction

```csharp
public interface IFhirClient
{
    Task<FhirResponse> SendAsync(FhirRequest request, CancellationToken cancellationToken);
    string BaseUrl { get; }
}

public interface IFhirClientRegistry
{
    IFhirClient GetDestination(int? destination);  // null = default (destination 1)
}

public sealed record FhirRequest
{
    public required HttpMethod Method { get; init; }
    public required string Url { get; init; }
    public JsonNode? Body { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; }
}

public sealed record FhirResponse
{
    public required int StatusCode { get; init; }
    public JsonNode? Body { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; }
}
```

**Implementations:**
- `HttpFhirClient` — wraps `HttpClient`, sends real HTTP requests
- `InProcessFhirClient` — uses ASP.NET Core `WebApplicationFactory<T>.CreateClient()` for in-process testing (no network overhead)
- `SingleClientRegistry` — wraps one `IFhirClient` for the common single-server case
- Multi-server registry deferred to Phase 6

### Validation Abstraction

```csharp
public interface IFhirResourceValidator
{
    Task<ValidationResult> ValidateAsync(
        JsonNode resource,
        string? profileCanonical,
        CancellationToken cancellationToken);
}

public sealed record ValidationResult(
    bool IsValid,
    IReadOnlyList<ValidationIssue> Issues);
```

- Default implementation delegates to Ignixa.Validation
- `ValidateProfileId` assertions route through this interface
- No-op implementation available for environments without validation support

### Phase 3: Report (Results → Output)

**TestScriptReport** accumulates results during execution:
```csharp
public sealed class TestScriptReport
{
    public string TestScriptName { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public List<TestPhaseResult> SetupResults { get; }
    public List<TestCaseResult> TestResults { get; }
    public List<TestPhaseResult> TeardownResults { get; }
    public TestScriptOutcome OverallOutcome { get; }  // Pass, Fail, Error
}
```

**Output generators:**
- `TestReportResourceGenerator` — produces FHIR TestReport resource (JSON)
- `JUnitXmlGenerator` — produces JUnit XML for CI/CD integration
- `ConsoleReportWriter` — human-readable console output

### Fixture Management

**IFixtureProvider interface (async, explicit parameters):**
```csharp
public interface IFixtureProvider
{
    ValueTask<JsonNode?> ResolveFixtureAsync(
        FixtureDefinition fixture,
        FixtureResolutionContext context,
        CancellationToken cancellationToken);
}

public sealed record FixtureResolutionContext
{
    public required IFhirSchemaProvider Schema { get; init; }
    public string? ResourceType { get; init; }       // hint from operation context
    public string? BasePath { get; init; }           // for file-relative resolution
}
```

**Implementations:**
- `InlineFixtureProvider` — uses resource embedded in TestScript
- `FileFixtureProvider` — loads from file path relative to TestScript base
- `FhirFakesFixtureProvider` — generates via `SchemaBasedFhirResourceFaker` (in `Ignixa.TestScript.FhirFakes`)
- `CompositeFixtureProvider` — chains providers (inline → file → FhirFakes fallback)

**Fixture lifecycle:**
- `autocreate: true` — fixture is POSTed to the server during setup
- `autodelete: true` — fixture is DELETEd during teardown
- These are tracked in `FixtureDefinition` and handled by the evaluator automatically

**FhirFakes activation:**
Detected via extension on fixture definition:
```json
{
  "id": "generated-patient",
  "extension": [{
    "url": "http://ignixa.io/testscript/fhirfakes",
    "valueCode": "Patient"
  }]
}
```

Or when a fixture ID references a non-existent resource, FhirFakes can auto-generate it based on the resource type inferred from the operation context.

### Variable Resolution

**VariableResolver** extracts and substitutes variables:
- Source: response body (via FHIRPath), response headers, default values
- Substitution: `${variableName}` in URLs, params, header values, assertion values
- Scope: variables persist within a test execution (setup → test → teardown)

**Substitution policy:**
- Substitution occurs at use-time (lazy), not declaration-time
- Missing variable → execution error (test fails)
- Non-scalar FHIRPath result → execution error
- No recursive/nested substitution (`${${x}}` is invalid)
- Allowed locations: `operation.url`, `operation.params`, `requestHeader.value`, `assert.value`
- Literal `${` can be escaped via `\${` if needed

### xUnit Integration (Ignixa.TestScript.XUnit)

```csharp
// Discover TestScript files as xUnit theory data (file paths for safe serialization)
public sealed class TestScriptDataAttribute : DataAttribute
{
    public TestScriptDataAttribute(string globPattern) { ... }

    public override IEnumerable<object[]> GetData(MethodInfo testMethod)
    {
        // Discover TestScript JSON files matching pattern
        // Return file paths as theory data (avoids xUnit serialization issues)
    }
}

// Usage in test classes:
public class ConformanceTests(ITestOutputHelper output)
{
    [Theory]
    [TestScriptData("testscripts/**/*.json")]
    public async Task ExecuteTestScript(string testScriptPath)
    {
        var definition = TestScriptParser.ParseFile(testScriptPath);
        var evaluator = CreateEvaluator();
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);
        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
    }
}
```

**Design notes:**
- File paths as theory data avoids xUnit discovery/serialization issues with complex objects
- Test names derive from file path for clear identification in CI output
- `TestScriptParser.ParseFile()` convenience method handles file I/O + JSON parsing

### Dependencies

**Ignixa.TestScript depends on:**
- `Ignixa.Abstractions` (IElement, IFhirSchemaProvider)
- `Ignixa.FhirPath` (assertion evaluation via FHIRPath expressions)
- `Ignixa.Serialization` (JSON parsing)
- `Ignixa.Specification` (schema access for all versions)

**Ignixa.TestScript.FhirFakes depends on:**
- `Ignixa.TestScript` (implements `IFixtureProvider`)
- `Ignixa.FhirFakes` (fixture generation)

**Ignixa.TestScript.XUnit depends on:**
- `Ignixa.TestScript`
- `xunit.core` / `xunit.abstractions`

### Error Handling

- Parse errors → `ParseResult<T>` with structured error list (like FhirPath parser)
- Operation failures → captured in TestScriptReport (don't throw)
- Assertion failures → recorded as fail/warning based on `warningOnly` flag
- Network errors → wrapped as operation error, execution continues to teardown
- `CancellationToken` threaded through all async operations

### Testing Strategy

- **Parser tests**: round-trip official HL7 TestScript examples
- **Evaluator tests**: mock `IFhirClient` for deterministic testing
- **Integration tests**: run against Ignixa via `InProcessFhirClient`
- **Conformance tests**: validate against Touchstone-authored TestScripts
- **Naming**: `GivenContext_WhenAction_ThenResult` (standard)

## Implementation Phases

| Phase | Scope | Deliverable |
|-------|-------|-------------|
| 1 | Parser & domain model | `TestScriptParser`, expression types, visitor interface |
| 2 | Core evaluator | `TestScriptEvaluator`, `ExecutionContext`, `IFhirClient` |
| 3 | Operations & assertions | Full operation handler, assertion validator, variable resolver |
| 4 | FhirFakes integration | `FhirFakesFixtureProvider`, auto-generation |
| 5 | Reporting & xUnit | `TestReportResourceGenerator`, `JUnitXmlGenerator`, xUnit adapter |
| 6 | Advanced features | Batch/transaction, conditional ops, multi-server, $operations |

## Open Questions (Resolved)

| Question | Decision |
|----------|----------|
| Variable scope | Persist across setup → test → teardown within one execution |
| In-process vs HTTP | Both via `IFhirClient` abstraction |
| FhirFakes coupling | First-class (in core library, not a plugin) |
| xUnit discovery | `[TestScriptData]` attribute with glob pattern |
| FHIR version handling | Unified expression tree, parser normalizes version differences |
