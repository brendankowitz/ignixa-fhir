# TestScript Execution Engine — Design Spec

**Date**: 2026-05-21
**Feature**: testscript
**Status**: Approved

## Summary

A three-phase TestScript execution engine following the Parser/Expression/Evaluator visitor pattern established across Ignixa.FhirPath, Ignixa.Search, and Ignixa.SqlOnFhir.

## Scope

- Parse FHIR TestScript resources (R4, R4B, R5) into an expression tree
- Evaluate the expression tree via a visitor pattern with immutable context
- Support both HTTP and in-process execution modes via `IFhirClient` abstraction
- First-class FhirFakes integration for fixture generation
- Output: FHIR TestReport resource + xUnit test integration
- Report output: console, TestReport resource, JUnit XML

## Project Structure

```
src/Core/Ignixa.TestScript/                 — Core: parser, expressions, evaluator
src/Core/Ignixa.TestScript.XUnit/           — xUnit adapter (TestScriptTheoryData, runner)
test/Ignixa.TestScript.Tests/               — Unit and integration tests
```

## Architecture

### Phase 1: Parse (JSON/XML → Expression Tree)

```
TestScriptParser.Parse(json)
    → TestScriptDefinition
        ├── Metadata (name, status, description, url)
        ├── FixtureDefinition[]
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
    public string? SourceId { get; init; }            // fixture ID for request body
    public string? TargetId { get; init; }            // fixture ID to store response
    public string? ResponseId { get; init; }          // ID to reference this response
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
    public string? Path { get; init; }                // FHIRPath for extraction
    public string? Value { get; init; }               // expected value
    public string? CompareToSourceId { get; init; }
    public string? CompareToSourceExpression { get; init; }
    public string? ValidateProfileId { get; init; }
    public string? Resource { get; init; }            // expected resource type
    public string? MinimumId { get; init; }
    public string? HeaderField { get; init; }
    public string? Operator { get; init; }            // "equals", "notEquals", "in", "contains"
    public bool WarningOnly { get; init; }
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

**Visitor interface:**

```csharp
public interface ITestScriptActionVisitor<TContext, TOutput>
{
    TOutput VisitOperation(OperationExpression expression, TContext context);
    TOutput VisitAssert(AssertExpression expression, TContext context);
}
```

**Evaluator:**

```csharp
public class TestScriptEvaluator : ITestScriptActionVisitor<ExecutionContext, Task<ExecutionContext>>
{
    // Dependencies injected
    private readonly IFhirClient _fhirClient;
    private readonly IFhirPathEvaluator _fhirPathEvaluator;
    private readonly IFixtureProvider _fixtureProvider;
    private readonly IFhirSchemaProvider _schemaProvider;

    public async Task<TestScriptReport> ExecuteAsync(
        TestScriptDefinition definition,
        CancellationToken cancellationToken);
}
```

**Execution context (immutable):**

```csharp
public sealed record ExecutionContext
{
    public required IFhirClient FhirClient { get; init; }
    public FhirResponse? LastResponse { get; init; }
    public ImmutableDictionary<string, string> Variables { get; init; }
    public ImmutableDictionary<string, JsonNode> Fixtures { get; init; }
    public ImmutableDictionary<string, FhirResponse> ResponseHistory { get; init; }
    public TestScriptReport Report { get; init; }

    public ExecutionContext WithResponse(string? responseId, FhirResponse response) => ...;
    public ExecutionContext WithVariable(string name, string value) => ...;
    public ExecutionContext WithFixture(string id, JsonNode resource) => ...;
}
```

### IFhirClient Abstraction

```csharp
public interface IFhirClient
{
    Task<FhirResponse> SendAsync(FhirRequest request, CancellationToken cancellationToken);
    string BaseUrl { get; }
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

**IFixtureProvider interface:**
```csharp
public interface IFixtureProvider
{
    JsonNode? ResolveFixture(FixtureDefinition fixture, IFhirSchemaProvider schema);
}
```

**Implementations:**
- `InlineFixtureProvider` — uses resource embedded in TestScript
- `FileFixtureProvider` — loads from file path relative to TestScript
- `FhirFakesFixtureProvider` — generates via `SchemaBasedFhirResourceFaker`
- `CompositeFixtureProvider` — chains providers (inline → file → FhirFakes)

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
- Substitution: `${variableName}` in URLs, header values, assertion values
- Scope: variables persist within a test execution (setup → test → teardown)

### xUnit Integration (Ignixa.TestScript.XUnit)

```csharp
// Discover and run TestScript files as xUnit theories
public sealed class TestScriptDataAttribute : DataAttribute
{
    public TestScriptDataAttribute(string globPattern) { ... }

    public override IEnumerable<object[]> GetData(MethodInfo testMethod)
    {
        // Discover TestScript JSON files matching pattern
        // Parse each into TestScriptDefinition
        // Return as test data rows
    }
}

// Usage in test classes:
public class ConformanceTests(ITestOutputHelper output)
{
    [Theory]
    [TestScriptData("testscripts/**/*.json")]
    public async Task ExecuteTestScript(TestScriptDefinition script)
    {
        var evaluator = CreateEvaluator();
        var report = await evaluator.ExecuteAsync(script, CancellationToken.None);
        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
    }
}
```

### Dependencies

**Ignixa.TestScript depends on:**
- `Ignixa.Abstractions` (IElement, IFhirSchemaProvider)
- `Ignixa.FhirPath` (assertion evaluation)
- `Ignixa.Serialization` (JSON parsing)
- `Ignixa.Specification` (schema access for all versions)
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
