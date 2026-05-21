# Investigation: TestScript Execution Engine

**Date:** 2026-05-21
**Status:** Investigation
**Author:** AI Assistant

## Executive Summary

This investigation explores implementing a **pure C# / Ignixa-native TestScript execution engine** to parse and execute FHIR TestScript resources. TestScript is the FHIR standard for defining automated tests of FHIR servers, but no mature C#/.NET execution engine currently exists in the open-source ecosystem.

**Key Findings:**
- No existing mature C#/.NET TestScript execution engines (Java/Ruby dominate)
- TestScript resources are well-specified with clear structure (fixtures, variables, setup, test, teardown, operations, assertions)
- Natural integration opportunities with Ignixa.FhirFakes for test data generation
- Architecture can leverage existing Ignixa components (FhirPath, Serialization, HTTP client patterns)

## Context

### What is FHIR TestScript?

FHIR TestScript is a resource type defined in the HL7 FHIR specification that describes automated tests for FHIR servers. It enables:
- Declarative test definitions (no code required)
- Portable tests across FHIR implementations
- Standardized assertions and operations
- Multi-step test workflows with fixtures and variables

**Official Specification:** https://www.hl7.org/fhir/testscript.html

### Current Ecosystem

**Existing Execution Engines:**
1. **Touchstone (AEGIS)** - Java-based, commercial/SaaS platform
   - Most widely used for HL7 connectathons
   - https://touchstone.aegis.net/touchstone/

2. **Inferno** - Ruby-based, open source
   - Used by ONC for certification testing
   - https://github.com/onc-healthit/inferno
   - Core execution: `/lib/inferno/apps/onfhir/test_script_executor.rb`

3. **Firely .NET SDK** - Has TestScript *resource models* but NO execution engine
   - https://github.com/FirelyTeam/firely-net-sdk
   - Can parse TestScript, but doesn't execute them

**Gap:** No mature, open-source C#/.NET TestScript execution engine exists.

### Why Build a Pure C# Engine?

1. **Native Integration** - Seamlessly integrate with Ignixa's architecture
2. **Test Data Generation** - Leverage Ignixa.FhirFakes for realistic test fixtures
3. **Developer Experience** - F5-runnable tests without external dependencies
4. **Validation Integration** - Use Ignixa.Validation for assertion validation
5. **FhirPath Assertions** - Leverage Ignixa.FhirPath for complex assertions
6. **Self-Testing** - Use TestScripts to validate Ignixa server conformance

## TestScript Resource Structure

### Core Components

```
TestScript
├── metadata (name, status, description)
├── fixture[] (test data resources)
├── variable[] (dynamic values extracted during execution)
├── setup (actions before tests - typically create test data)
│   └── action[]
│       ├── operation (HTTP request)
│       └── assert (validate response)
├── test[] (main test cases)
│   └── action[]
│       ├── operation
│       └── assert
└── teardown (cleanup actions after tests)
    └── action[]
```

### Key Elements

#### 1. **Fixture**
Test data resources embedded in or referenced by the TestScript.

```json
{
  "id": "patient-fixture",
  "resource": {
    "resourceType": "Patient",
    "name": [{ "family": "Test", "given": ["Peter"] }],
    "gender": "male",
    "birthDate": "1990-01-01"
  }
}
```

#### 2. **Variable**
Dynamic values captured from responses or headers.

```json
{
  "name": "patientId",
  "path": "Patient.id",
  "sourceId": "patient-fixture"
}
```

Or from HTTP headers:
```json
{
  "name": "patientId",
  "headerField": "Location"
}
```

#### 3. **Operation**
HTTP request to execute against the FHIR server.

```json
{
  "type": { "code": "create" },
  "resource": "Patient",
  "description": "Create a new Patient",
  "url": "${base}/Patient",
  "requestHeader": [
    { "field": "Content-Type", "value": "application/fhir+json" }
  ],
  "sourceId": "patient-fixture"
}
```

**Common Operation Types:**
- `create` - POST to create resource
- `read` - GET to read resource
- `update` - PUT to update resource
- `delete` - DELETE to remove resource
- `search` - GET with search parameters
- `batch` / `transaction` - Bundle operations

#### 4. **Assert**
Validation of operation results.

```json
{
  "response": "created",
  "description": "Patient successfully created",
  "resource": "Patient"
}
```

**Common Assertions:**
- `response` - HTTP status code check ("okay", "created", "noContent", etc.)
- `contentType` - Validate Content-Type header
- `path` - FHIRPath expression validation
- `compareToSourceId` - Compare response to fixture
- `minimumId` - Validate resource ID format
- `validateProfileId` - Validate against FHIR profile

### Complete Example

See web research results for full CRUD TestScript example with Patient resource.

## Example TestScript Sources

### Official HL7 Examples
- **GitHub:** https://github.com/HL7/fhir (search for `testscript-example`)
- **R4 Examples:** https://www.hl7.org/fhir/r4/examples.html (filter for TestScript)
- **R5 Examples:** https://hl7.org/fhir/examples.html

### Touchstone Public Repository
- Public TestScripts: https://touchstone.aegis.net/touchstone/testscripts
- Da Vinci Project TestScripts: https://github.com/HL7/fhir-us-core/tree/master/tests

## Proposed Architecture

### High-Level Design (Aligned with Parser/Expression/Evaluator Pattern)

Following the established Ignixa patterns from `FhirPath`, `Search`, and `SqlOnFhir`, the TestScript engine uses a **three-phase architecture**:

```
┌─────────────────────────────────────────────────────────────────┐
│           PHASE 1: PARSE (Text → Expression Tree)               │
├─────────────────────────────────────────────────────────────────┤
│  TestScriptParser                                               │
│  └─> Deserializes JSON/XML → TestScriptDefinition (AST root)   │
│      ├─> FixtureDefinition[]                                    │
│      ├─> VariableDefinition[]                                   │
│      ├─> SetupDefinition (ActionExpression[])                   │
│      ├─> TestDefinition[] (ActionExpression[])                  │
│      └─> TeardownDefinition (ActionExpression[])                │
│                                                                  │
│  ActionExpression (abstract base)                               │
│  ├─> OperationExpression                                        │
│  └─> AssertExpression                                           │
└─────────────────────────────────────────────────────────────────┘
         ↓
┌─────────────────────────────────────────────────────────────────┐
│         PHASE 2: EVALUATE (Expression Tree → Results)           │
├─────────────────────────────────────────────────────────────────┤
│  TestScriptEvaluator : ITestScriptActionVisitor                 │
│  └─> Executes ActionExpression tree via visitor pattern         │
│      ├─> VisitOperation(OperationExpression, context)           │
│      │    └─> HttpClient, VariableResolver                      │
│      └─> VisitAssert(AssertExpression, context)                 │
│           └─> FhirPath, Validation, Comparisons                 │
│                                                                  │
│  ExecutionContext (immutable, threaded through visitor)         │
│  ├─> CurrentResponse (HttpResponseMessage)                      │
│  ├─> Variables (Dictionary<string, string>)                     │
│  ├─> Fixtures (Dictionary<string, ResourceJsonNode>)            │
│  └─> TestReport (accumulates outcomes)                          │
└─────────────────────────────────────────────────────────────────┘
         ↓
┌─────────────────────────────────────────────────────────────────┐
│          PHASE 3: REPORT (Results → OperationOutcome)           │
├─────────────────────────────────────────────────────────────────┤
│  TestScriptReporter                                             │
│  └─> Generates execution reports                                │
│      ├─> FHIR OperationOutcome (standard)                       │
│      ├─> JUnit XML (CI/CD integration)                          │
│      ├─> Console output (developer experience)                  │
│      └─> Markdown report (documentation)                        │
└─────────────────────────────────────────────────────────────────┘
```

### Architecture Alignment with Existing Patterns

This architecture mirrors the **proven pattern** used across Ignixa:

| Component | FhirPath | Search | SqlOnFhir | **TestScript** |
|-----------|----------|--------|-----------|----------------|
| **Parser** | `FhirPathParser` | `ExpressionParser` | `ViewDefinitionExpressionParser` | **`TestScriptParser`** |
| **Expression Tree** | `Expression` subclasses | `Expression` subclasses | `SqlOnFhirExpression` | **`ActionExpression` subclasses** |
| **Evaluator** | `FhirPathEvaluator` | `SearchQueryInterpreter` | `SqlOnFhirEvaluator` | **`TestScriptEvaluator`** |
| **Visitor Interface** | `IFhirPathExpressionVisitor` | `IExpressionVisitor` | (implicit) | **`ITestScriptActionVisitor`** |
| **Context** | `EvaluationContext` | `TContext` generic | `SqlOnFhirSchemaVisitor` | **`ExecutionContext`** |
| **Output** | `IEnumerable<IElement>` | `TOutput` generic | `ViewDefinition` | **`TestScriptReport`** |

**Key Benefits of This Pattern:**
- ✅ **Separation of Concerns**: Parse (structure) → Evaluate (execution) → Report (output)
- ✅ **Testability**: Expression trees can be unit tested without HTTP calls
- ✅ **Composability**: Actions can be combined, transformed, optimized
- ✅ **Extensibility**: New action types via new Expression subclasses + visitor methods
- ✅ **Immutable Context**: Thread-safe, pure functional evaluation (like FhirPath)
- ✅ **Consistent API**: Developers familiar with FhirPath/Search will recognize the pattern

### Expression Tree Definition

Following the Ignixa pattern, TestScript actions are modeled as an **expression tree** with a visitor interface:

```csharp
namespace Ignixa.TestScript.Expressions;

/// <summary>
/// Base class for all TestScript action expressions.
/// Follows the same pattern as Ignixa.FhirPath.Expressions.Expression
/// and Ignixa.Search.Expressions.Expression.
/// </summary>
public abstract class ActionExpression
{
    /// <summary>
    /// Location information for debugging (line number in TestScript JSON/XML).
    /// </summary>
    public ISourcePositionInfo? Location { get; set; }

    /// <summary>
    /// Accepts a visitor for traversing the action expression tree.
    /// Core method enabling the visitor pattern.
    /// </summary>
    public abstract TOutput AcceptVisitor<TContext, TOutput>(
        ITestScriptActionVisitor<TContext, TOutput> visitor,
        TContext context);
}

/// <summary>
/// Represents an HTTP operation (create, read, update, delete, search, etc.).
/// </summary>
public class OperationExpression : ActionExpression
{
    public string Type { get; init; }              // "create", "read", "update", etc.
    public string? Resource { get; init; }         // "Patient", "Observation", etc.
    public string? Url { get; init; }              // "${base}/Patient/${patientId}"
    public string? SourceId { get; init; }         // Fixture ID for request body
    public string? TargetId { get; init; }         // Fixture ID for storing response
    public List<HeaderExpression> RequestHeaders { get; init; } = [];
    public string? Description { get; init; }

    public override TOutput AcceptVisitor<TContext, TOutput>(
        ITestScriptActionVisitor<TContext, TOutput> visitor,
        TContext context)
    {
        return visitor.VisitOperation(this, context);
    }
}

/// <summary>
/// Represents a validation assertion on an operation result.
/// </summary>
public class AssertExpression : ActionExpression
{
    public string? Response { get; init; }         // "okay", "created", "noContent"
    public string? ContentType { get; init; }      // "application/fhir+json"
    public string? Expression { get; init; }       // FHIRPath expression
    public string? CompareToSourceId { get; init; } // Fixture comparison
    public string? Path { get; init; }             // FHIRPath extraction
    public string? ValidateProfileId { get; init; } // Profile validation
    public string? Description { get; init; }
    public bool? WarningOnly { get; init; }        // Continue on failure

    public override TOutput AcceptVisitor<TContext, TOutput>(
        ITestScriptActionVisitor<TContext, TOutput> visitor,
        TContext context)
    {
        return visitor.VisitAssert(this, context);
    }
}

/// <summary>
/// Visitor interface for TestScript action expressions.
/// Follows the same pattern as IFhirPathExpressionVisitor and IExpressionVisitor.
/// </summary>
public interface ITestScriptActionVisitor<in TContext, out TOutput>
{
    TOutput VisitOperation(OperationExpression expression, TContext context);
    TOutput VisitAssert(AssertExpression expression, TContext context);
}
```

### Execution Context (Immutable)

Like `FhirPath.EvaluationContext`, the execution context is **immutable** and threaded through visitor calls:

```csharp
/// <summary>
/// Immutable context for TestScript execution.
/// Follows the same pattern as FhirPath.EvaluationContext.
/// </summary>
public class ExecutionContext
{
    // Current HTTP response from last operation
    public HttpResponseMessage? LastResponse { get; init; }
    public ResourceJsonNode? LastResponseBody { get; init; }

    // Variables captured during execution (e.g., ${patientId})
    public ImmutableDictionary<string, string> Variables { get; init; }
        = ImmutableDictionary<string, string>.Empty;

    // Fixtures loaded from TestScript or FhirFakes
    public ImmutableDictionary<string, ResourceJsonNode> Fixtures { get; init; }
        = ImmutableDictionary<string, ResourceJsonNode>.Empty;

    // Target FHIR server base URL
    public string TargetBaseUrl { get; init; }

    // HTTP client for operations
    public HttpClient HttpClient { get; init; }

    // Test report accumulator (mutable, but encapsulated)
    public TestScriptReport Report { get; init; }

    /// <summary>
    /// Creates a new context with updated response.
    /// Fluent API for immutable updates.
    /// </summary>
    public ExecutionContext WithResponse(HttpResponseMessage response, ResourceJsonNode? body)
    {
        return this with
        {
            LastResponse = response,
            LastResponseBody = body
        };
    }

    /// <summary>
    /// Creates a new context with updated variable.
    /// </summary>
    public ExecutionContext WithVariable(string name, string value)
    {
        return this with
        {
            Variables = Variables.SetItem(name, value)
        };
    }

    /// <summary>
    /// Creates a new context with added fixture.
    /// </summary>
    public ExecutionContext WithFixture(string id, ResourceJsonNode fixture)
    {
        return this with
        {
            Fixtures = Fixtures.SetItem(id, fixture)
        };
    }
}
```

### Core Components

#### 1. TestScriptParser
**Responsibility:** Deserialize TestScript JSON/XML into C# domain objects.

**Dependencies:**
- `Ignixa.Serialization` - JSON parsing via JsonNode
- `Ignixa.Specification` - FHIR schema validation

**Key Classes:**
- `TestScriptDefinition` - Root domain model
- `FixtureDefinition` - Test data resources
- `OperationDefinition` - HTTP operations
- `AssertDefinition` - Validation rules

#### 2. TestScriptEvaluator
**Responsibility:** Execute TestScript expression tree via visitor pattern.

**Implementation Pattern** (like `FhirPathEvaluator`):

```csharp
namespace Ignixa.TestScript.Evaluation;

/// <summary>
/// Evaluates TestScript action expressions via the visitor pattern.
/// Follows the same architecture as FhirPathEvaluator and SearchQueryInterpreter.
/// </summary>
public class TestScriptEvaluator : ITestScriptActionVisitor<ExecutionContext, ExecutionContext>
{
    private readonly IFhirPathEvaluator _fhirPathEvaluator;
    private readonly IValidator _validator;
    private readonly IFhirSchemaProvider _schemaProvider;

    public TestScriptEvaluator(
        IFhirPathEvaluator fhirPathEvaluator,
        IValidator validator,
        IFhirSchemaProvider schemaProvider)
    {
        _fhirPathEvaluator = fhirPathEvaluator;
        _validator = validator;
        _schemaProvider = schemaProvider;
    }

    /// <summary>
    /// Main entry point: Execute a TestScript definition.
    /// </summary>
    public async Task<TestScriptReport> ExecuteAsync(
        TestScriptDefinition testScript,
        string targetBaseUrl,
        CancellationToken cancellationToken)
    {
        var context = new ExecutionContext
        {
            TargetBaseUrl = targetBaseUrl,
            HttpClient = new HttpClient(),
            Report = new TestScriptReport()
        };

        // Load fixtures into context
        context = LoadFixtures(testScript, context);

        // 1. Setup phase
        context = await ExecutePhaseAsync(testScript.Setup, context, cancellationToken);

        // 2. Test phase
        foreach (var test in testScript.Tests)
        {
            context = await ExecuteTestAsync(test, context, cancellationToken);
        }

        // 3. Teardown phase
        context = await ExecutePhaseAsync(testScript.Teardown, context, cancellationToken);

        return context.Report;
    }

    private async Task<ExecutionContext> ExecutePhaseAsync(
        IEnumerable<ActionExpression> actions,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        foreach (var action in actions)
        {
            // Use visitor pattern to dispatch to correct handler
            context = await action.AcceptVisitorAsync(this, context, cancellationToken);
        }
        return context;
    }

    /// <summary>
    /// Visitor method: Execute an HTTP operation.
    /// </summary>
    public async Task<ExecutionContext> VisitOperationAsync(
        OperationExpression operation,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        // 1. Resolve variables in URL
        var url = ResolveVariables(operation.Url, context);

        // 2. Build HTTP request
        var request = new HttpRequestMessage(
            GetHttpMethod(operation.Type),
            url);

        // 3. Add headers
        foreach (var header in operation.RequestHeaders)
        {
            request.Headers.Add(header.Field, header.Value);
        }

        // 4. Add body (for create/update)
        if (operation.SourceId != null)
        {
            var fixture = context.Fixtures[operation.SourceId];
            request.Content = new StringContent(
                fixture.ToJson(),
                Encoding.UTF8,
                "application/fhir+json");
        }

        // 5. Execute request
        var response = await context.HttpClient.SendAsync(request, cancellationToken);

        // 6. Parse response
        ResourceJsonNode? responseBody = null;
        if (response.Content != null)
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            responseBody = JsonSourceNodeFactory.Parse(json);
        }

        // 7. Extract variables (e.g., patientId from Location header)
        context = ExtractVariables(operation, response, responseBody, context);

        // 8. Store response in context (immutable update)
        return context.WithResponse(response, responseBody);
    }

    /// <summary>
    /// Visitor method: Execute an assertion.
    /// </summary>
    public ExecutionContext VisitAssert(
        AssertExpression assert,
        ExecutionContext context)
    {
        var issues = new List<string>();

        // 1. Response code assertion
        if (assert.Response != null)
        {
            var expectedCode = MapResponseCode(assert.Response);
            if ((int)context.LastResponse.StatusCode != expectedCode)
            {
                issues.Add($"Expected {expectedCode}, got {(int)context.LastResponse.StatusCode}");
            }
        }

        // 2. Content-Type assertion
        if (assert.ContentType != null)
        {
            var actualContentType = context.LastResponse.Content?.Headers.ContentType?.MediaType;
            if (actualContentType != assert.ContentType)
            {
                issues.Add($"Expected Content-Type {assert.ContentType}, got {actualContentType}");
            }
        }

        // 3. FHIRPath expression assertion
        if (assert.Expression != null && context.LastResponseBody != null)
        {
            var element = context.LastResponseBody.ToElement(_schemaProvider);
            var result = _fhirPathEvaluator.IsTrue(element, assert.Expression);
            if (!result)
            {
                issues.Add($"FHIRPath assertion failed: {assert.Expression}");
            }
        }

        // 4. Profile validation assertion
        if (assert.ValidateProfileId != null && context.LastResponseBody != null)
        {
            var outcome = _validator.Validate(
                context.LastResponseBody,
                assert.ValidateProfileId);
            if (outcome.HasErrors)
            {
                issues.AddRange(outcome.Errors.Select(e => e.Message));
            }
        }

        // 5. Fixture comparison assertion
        if (assert.CompareToSourceId != null && context.LastResponseBody != null)
        {
            var expectedFixture = context.Fixtures[assert.CompareToSourceId];
            if (!AreResourcesEqual(context.LastResponseBody, expectedFixture))
            {
                issues.Add($"Response does not match fixture {assert.CompareToSourceId}");
            }
        }

        // 6. Record assertion result
        var assertionResult = new AssertionResult(
            assert.Description ?? "Unnamed assertion",
            issues.Count == 0,
            issues,
            assert.WarningOnly ?? false);

        context.Report.AddAssertion(assertionResult);

        return context;
    }

    private string ResolveVariables(string template, ExecutionContext context)
    {
        // Replace ${variableName} placeholders
        foreach (var (key, value) in context.Variables)
        {
            template = template.Replace($"${{{key}}}", value);
        }
        return template;
    }

    private ExecutionContext ExtractVariables(
        OperationExpression operation,
        HttpResponseMessage response,
        ResourceJsonNode? responseBody,
        ExecutionContext context)
    {
        // Extract from Location header (common for create operations)
        if (response.Headers.Location != null)
        {
            var locationPath = response.Headers.Location.ToString();
            var parts = locationPath.Split('/');
            if (parts.Length >= 2)
            {
                var resourceId = parts[^1];
                context = context.WithVariable("lastCreatedId", resourceId);
            }
        }

        // Extract from response body via FHIRPath (if defined in TestScript)
        // This would be driven by variable definitions in the TestScript
        // e.g., <variable name="patientId" path="Patient.id" sourceId="response"/>

        return context;
    }
}
```

#### 3. FixtureManager
**Responsibility:** Load and manage test fixture resources.

**Capabilities:**
- Embed fixtures directly in TestScript
- Reference external fixture files
- Integrate with `Ignixa.FhirFakes` for dynamic generation
- Apply variable substitution to fixtures

**Example Integration:**
```csharp
public class FixtureManager
{
    private readonly SchemaBasedFhirResourceFaker _faker;
    private readonly Dictionary<string, ResourceJsonNode> _fixtures;

    public ResourceJsonNode GetFixture(string fixtureId)
    {
        if (_fixtures.TryGetValue(fixtureId, out var fixture))
        {
            return fixture;
        }

        // Generate via FhirFakes if not found
        return GenerateFixture(fixtureId);
    }

    private ResourceJsonNode GenerateFixture(string fixtureId)
    {
        // Use FhirFakes to generate realistic test data
        return _faker.CreatePatient(p => p
            .WithGivenName("Test")
            .WithFamilyName(fixtureId)
            .WithTag($"testscript-{fixtureId}"));
    }
}
```

#### 4. VariableResolver
**Responsibility:** Extract and resolve variables during execution.

**Variable Sources:**
- Path extraction (FHIRPath expressions on response bodies)
- Header extraction (Location, ETag, etc.)
- Default values
- Expression evaluation

**Example:**
```csharp
public class VariableResolver
{
    private readonly Dictionary<string, string> _variables = new();
    private readonly IFhirPathEvaluator _evaluator;

    public void ExtractVariable(
        VariableDefinition variable,
        HttpResponseMessage response,
        ResourceJsonNode? responseBody)
    {
        string? value = null;

        // Header field extraction
        if (!string.IsNullOrEmpty(variable.HeaderField))
        {
            value = ExtractFromHeader(response, variable.HeaderField);
        }
        // Path extraction (FHIRPath)
        else if (!string.IsNullOrEmpty(variable.Path) && responseBody != null)
        {
            var element = responseBody.ToElement(schema);
            value = _evaluator.Scalar(element, variable.Path)?.ToString();
        }

        if (value != null)
        {
            _variables[variable.Name] = value;
        }
    }

    public string Resolve(string template)
    {
        // Replace ${variableName} placeholders
        foreach (var (key, value) in _variables)
        {
            template = template.Replace($"${{{key}}}", value);
        }
        return template;
    }
}
```

#### 5. OperationHandler
**Responsibility:** Execute HTTP operations against target FHIR server.

**Dependencies:**
- `HttpClient` - HTTP communication
- `Ignixa.Serialization` - Request/response serialization
- `VariableResolver` - URL/header interpolation

**Example:**
```csharp
public class OperationHandler
{
    private readonly HttpClient _httpClient;
    private readonly VariableResolver _variableResolver;

    public async Task<OperationResult> ExecuteAsync(
        OperationDefinition operation,
        CancellationToken cancellationToken)
    {
        // Resolve variables in URL
        var url = _variableResolver.Resolve(operation.Url);

        // Build request
        var request = new HttpRequestMessage(
            GetHttpMethod(operation.Type),
            url);

        // Add headers
        foreach (var header in operation.RequestHeaders)
        {
            request.Headers.Add(header.Field, header.Value);
        }

        // Add body (for create/update)
        if (operation.SourceId != null)
        {
            var fixture = GetFixture(operation.SourceId);
            request.Content = new StringContent(
                fixture.ToJson(),
                Encoding.UTF8,
                "application/fhir+json");
        }

        // Execute request
        var response = await _httpClient.SendAsync(request, cancellationToken);

        // Parse response
        ResourceJsonNode? responseBody = null;
        if (response.Content != null)
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            responseBody = JsonSourceNodeFactory.Parse(json);
        }

        return new OperationResult(response, responseBody);
    }
}
```

#### 6. AssertionValidator
**Responsibility:** Validate operation results against assertions.

**Dependencies:**
- `Ignixa.FhirPath` - Complex assertions via FHIRPath
- `Ignixa.Validation` - Profile/schema validation
- `Ignixa.Serialization` - Resource comparison

**Example:**
```csharp
public class AssertionValidator
{
    private readonly IFhirPathEvaluator _evaluator;
    private readonly IValidator _validator;

    public AssertionResult Validate(
        AssertDefinition assert,
        OperationResult operationResult)
    {
        var issues = new List<string>();

        // Response code assertion
        if (assert.Response != null)
        {
            var expectedCode = MapResponseCode(assert.Response);
            if ((int)operationResult.StatusCode != expectedCode)
            {
                issues.Add($"Expected response {expectedCode}, got {(int)operationResult.StatusCode}");
            }
        }

        // Content-Type assertion
        if (assert.ContentType != null)
        {
            var actualContentType = operationResult.Response.Content?.Headers.ContentType?.MediaType;
            if (actualContentType != assert.ContentType)
            {
                issues.Add($"Expected Content-Type {assert.ContentType}, got {actualContentType}");
            }
        }

        // FHIRPath assertion
        if (assert.Expression != null && operationResult.Body != null)
        {
            var element = operationResult.Body.ToElement(schema);
            var result = _evaluator.IsTrue(element, assert.Expression);
            if (!result)
            {
                issues.Add($"FHIRPath assertion failed: {assert.Expression}");
            }
        }

        // Profile validation
        if (assert.ValidateProfileId != null && operationResult.Body != null)
        {
            var outcome = _validator.Validate(operationResult.Body, assert.ValidateProfileId);
            if (outcome.HasErrors)
            {
                issues.AddRange(outcome.Errors.Select(e => e.Message));
            }
        }

        return new AssertionResult(issues.Count == 0, issues);
    }
}
```

#### 7. TestScriptReport
**Responsibility:** Generate execution reports (OperationOutcome, test results).

**Output Formats:**
- FHIR OperationOutcome resource (standard)
- JUnit XML (CI/CD integration)
- Console output (developer experience)
- Markdown report (documentation)

## Integration with Ignixa.FhirFakes

### Use Cases

1. **Dynamic Fixture Generation**
   - TestScript references a fixture ID
   - FhirFakes generates realistic data on-the-fly
   - Enables large-scale testing without manual fixture creation

2. **Scenario-Based Testing**
   - Use FhirFakes scenarios (e.g., `DiabetesManagementScenario`)
   - Generate complete patient journeys with realistic data
   - TestScript validates end-to-end workflows

3. **Profile Conformance Testing**
   - Generate US Core / AU Base compliant resources
   - Validate server accepts profile-conformant data
   - Test profile-specific search parameters

### Example Integration

```csharp
// In TestScript fixture section
{
  "id": "seattle-patient",
  "extension": [{
    "url": "http://ignixa.io/testscript-fhirfakes-generation",
    "valueString": "faker.CreateSeattlePatient(p => p.WithAge(45))"
  }]
}

// Execution engine recognizes extension and calls FhirFakes
public ResourceJsonNode ResolveFixture(FixtureDefinition fixture)
{
    var generatorExtension = fixture.Extensions
        .FirstOrDefault(e => e.Url == "http://ignixa.io/testscript-fhirfakes-generation");

    if (generatorExtension != null)
    {
        // Evaluate FhirFakes expression
        return EvaluateFhirFakesExpression(generatorExtension.ValueString);
    }

    return fixture.Resource;
}
```

## Implementation Phases

### Phase 1: Parser & Domain Model (Week 1-2)
- [ ] Create TestScript domain models
- [ ] Implement JSON/XML deserialization
- [ ] Add TestScript parsing tests
- [ ] Support R4/R4B/R5 TestScript versions

### Phase 2: Core Execution Engine (Week 3-4)
- [ ] Implement TestScriptExecutor
- [ ] Add FixtureManager
- [ ] Add VariableResolver
- [ ] Support basic operations (create, read, update, delete)

### Phase 3: Operations & Assertions (Week 5-6)
- [ ] Implement OperationHandler with HttpClient
- [ ] Add AssertionValidator
- [ ] Support FHIRPath assertions
- [ ] Support response code/header assertions

### Phase 4: FhirFakes Integration (Week 7)
- [ ] Add FhirFakes-based fixture generation
- [ ] Support dynamic variable resolution
- [ ] Enable scenario-based testing

### Phase 5: Reporting & Tooling (Week 8)
- [ ] Generate OperationOutcome reports
- [ ] Add JUnit XML output for CI/CD
- [ ] Create CLI tool for TestScript execution
- [ ] Add xUnit integration for test runner

### Phase 6: Advanced Features (Future)
- [ ] Support batch/transaction operations
- [ ] Add custom operation support ($validate, $match, etc.)
- [ ] Implement conditional operations
- [ ] Support multi-server testing (origin vs destination)
- [ ] Add performance profiling

## Technical Considerations

### Architecture Alignment

**Leverage Existing Ignixa Components:**
- ✅ `Ignixa.Serialization` - JSON/XML parsing, resource models
- ✅ `Ignixa.FhirPath` - Expression evaluation for assertions
- ✅ `Ignixa.Validation` - Profile validation in assertions
- ✅ `Ignixa.FhirFakes` - Dynamic fixture generation
- ✅ `Ignixa.Specification` - Schema access for all FHIR versions
- ✅ Existing HTTP client patterns from Ignixa.Api

**New Components Required:**
- `Ignixa.TestScript` (new Core library)
- `Ignixa.TestScript.Cli` (new tool)
- `Ignixa.TestScript.XUnit` (xUnit integration)

### Performance Targets

- **Parser:** <10ms per TestScript
- **Simple Operation:** <100ms (setup + execute + assert)
- **Full Test Suite:** <5s for 50 operations
- **Parallel Execution:** Support concurrent test execution

### Error Handling

**Graceful Failures:**
- Continue execution after assertion failure (collect all issues)
- Timeout handling for HTTP operations
- Detailed error messages with context
- OperationOutcome for standardized error reporting

**Debugging Support:**
- Log all HTTP requests/responses
- Trace variable resolution
- Show FHIRPath evaluation steps
- Export raw execution data

### Testing Strategy

**Self-Hosting:**
- Use TestScripts to test Ignixa server conformance
- Validate against official HL7 TestScript examples
- Create Ignixa-specific TestScript suite

**Compatibility:**
- Test against Touchstone-authored TestScripts
- Validate against official US Core TestScripts
- Ensure R4/R4B/R5 version compatibility

## Comparison with Existing Patterns

### Similar to OfficialTestSuiteRunner (FhirPath)

The TestScript engine follows patterns from `Ignixa.FhirPath.Tests/OfficialTestSuiteRunner.cs`:

**Shared Patterns:**
1. Parse XML/JSON test definitions
2. Load input fixtures from files
3. Execute tests and collect outcomes
4. Generate reports (pass/fail/error)
5. Support predicate modes and special evaluation contexts

**Key Differences:**
- TestScript tests **FHIR servers** (HTTP operations)
- FhirPath tests **expression evaluators** (pure functions)
- TestScript has setup/teardown phases
- TestScript uses variables captured from responses

### Similar to Validation Architecture (ADR 2510)

Parallels with three-tier validation pipeline:

**TestScript Execution Phases:**
- **Tier 1:** Parse TestScript, validate structure
- **Tier 2:** Execute operations, basic assertions
- **Tier 3:** Profile validation, complex FHIRPath assertions

**Shared Components:**
- Use `Ignixa.Validation` for profile assertions
- Use `Ignixa.FhirPath` for complex expression evaluation
- Generate `OperationOutcome` for error reporting

## Open Questions

1. **Variable Scope:** Should variables persist across test cases or reset per test?
   - **Recommendation:** Reset per test for isolation, allow global variables via extension

2. **Fixture Storage:** Where should external fixture files be stored?
   - **Recommendation:** Support multiple sources:
     - Embedded in TestScript
     - Relative file paths
     - HTTP URLs
     - FhirFakes generation

3. **Multi-Server Testing:** How to support testing against multiple servers (e.g., source + destination)?
   - **Recommendation:** Support `server` element in operations to specify target

4. **Custom Operations:** How to handle FHIR operations ($validate, $match, etc.)?
   - **Recommendation:** Map to appropriate HTTP POST to `[base]/[ResourceType]/$operation`

5. **Authentication:** How to handle OAuth/SMART on FHIR authentication?
   - **Recommendation:** Support bearer tokens via configuration, integrate with Ignixa.Api.OpenIddict

## References

### Official FHIR Specification
- **TestScript Resource:** https://www.hl7.org/fhir/testscript.html
- **R4 Examples:** https://www.hl7.org/fhir/r4/examples.html
- **R5 Examples:** https://hl7.org/fhir/examples.html

### Existing Implementations
- **Touchstone:** https://touchstone.aegis.net/touchstone/
- **Inferno:** https://github.com/onc-healthit/inferno
- **Firely SDK:** https://github.com/FirelyTeam/firely-net-sdk

### Test Script Repositories
- **HL7 FHIR GitHub:** https://github.com/HL7/fhir
- **US Core Tests:** https://github.com/HL7/fhir-us-core/tree/master/tests
- **Touchstone Public Scripts:** https://touchstone.aegis.net/touchstone/testscripts

### Related Ignixa Documentation
- **Validation Architecture:** docs/adr/adr-2510-validation-architecture.md
- **FhirPath Test Suite:** docs/investigations/fhirpath-test-suite-schema.md
- **FhirFakes README:** src/Core/Ignixa.FhirFakes/README.md

## Conclusion

Implementing a pure C# TestScript execution engine is **feasible and valuable**:

**Strengths:**
- ✅ Well-specified standard with clear semantics
- ✅ Natural fit with existing Ignixa components
- ✅ Unique integration opportunity with FhirFakes
- ✅ Fills ecosystem gap (no mature C#/.NET engine exists)
- ✅ Enables self-testing of Ignixa server

**Challenges:**
- ⚠️ Significant scope (8+ weeks estimated)
- ⚠️ Requires comprehensive testing against official examples
- ⚠️ Must maintain version compatibility (R4/R4B/R5)
- ⚠️ Complex variable/fixture resolution logic

**Architecture Alignment:**
- ✅ **Parser/Expression/Evaluator pattern** - Consistent with FhirPath, Search, SqlOnFhir
- ✅ **Visitor pattern** - Extensible, testable, familiar to Ignixa developers
- ✅ **Immutable context** - Thread-safe, functional evaluation style
- ✅ **Expression tree** - Composable, optimizable, analyzable
- ✅ **Separation of concerns** - Parse → Evaluate → Report (clean phases)

**Recommendation:** **Proceed with phased implementation**, starting with Phase 1 (Parser) to validate feasibility and gather community feedback.

**Next Steps:**
1. Create ADR if decision to proceed is made
2. Create feature branch: `feature/testscript-execution-engine`
3. Implement Phase 1 (Parser & Domain Model with Expression Tree)
4. Implement Phase 2 (Evaluator with Visitor Pattern)
5. Validate against 10+ official HL7 TestScript examples
6. Gather feedback from Ignixa community
7. Proceed with Phases 3-6 if validation successful

---

## Appendix: Pattern Comparison

### Code Structure Comparison

**FhirPath Pattern:**
```
Ignixa.FhirPath/
├── Expressions/
│   ├── Expression.cs (abstract base)
│   ├── ConstantExpression.cs
│   ├── FunctionCallExpression.cs
│   └── ...
├── Visitors/
│   └── IFhirPathExpressionVisitor.cs
├── Parser/
│   └── FhirPathParser.cs
├── Evaluation/
│   ├── FhirPathEvaluator.cs
│   └── EvaluationContext.cs
```

**Search Pattern:**
```
Ignixa.Search/
├── Expressions/
│   ├── Expression.cs (abstract base)
│   ├── SearchParameterExpression.cs
│   ├── BinaryExpression.cs
│   └── ...
├── Expressions/
│   └── IExpressionVisitor.cs
├── Expressions/Parsers/
│   └── ExpressionParser.cs
├── Interpreters/
│   └── SearchQueryInterpreter.cs
```

**TestScript Pattern (Proposed):**
```
Ignixa.TestScript/
├── Expressions/
│   ├── ActionExpression.cs (abstract base)
│   ├── OperationExpression.cs
│   ├── AssertExpression.cs
│   └── ...
├── Visitors/
│   └── ITestScriptActionVisitor.cs
├── Parser/
│   └── TestScriptParser.cs
├── Evaluation/
│   ├── TestScriptEvaluator.cs
│   └── ExecutionContext.cs
├── Reporting/
│   └── TestScriptReporter.cs
```

**Consistency Benefits:**
- Onboarding: Developers familiar with FhirPath/Search immediately understand TestScript architecture
- Tooling: Common patterns enable shared utilities (expression tree visualization, optimization)
- Testing: Unit tests follow same patterns across all libraries
- Maintenance: Consistent structure reduces cognitive load when switching between libraries
