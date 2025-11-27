# CLAUDE.md - Development Guide

Guidance for Claude Code to deliver high-quality output on **featurework**, **debugging**, **maintenance**, and **testing**.

## Quick Start

| Task | Pattern | File |
|------|---------|------|
| **Add feature** | Query/Handler → Endpoint | `Features/{Name}/`, `Infrastructure/*Endpoints.cs` |
| **Fix bug** | Reproduce → Test → Fix → Verify | Run `dotnet test` first |
| **Refactor** | Impact analysis → Preview → Apply | Use Roslyn tools |
| **Debug** | Find issue → Write test → Fix → Regression test | Follow AAA pattern |

---

## Tool Priority Rules

### Leverage Roslyn MCP for Rich Semantic Metadata

**IMPORTANT**: Roslyn MCP Server provides a rich semantic model of the codebase that Claude Code doesn't have direct access to. Use Roslyn proactively to gather semantic information that informs your work with built-in tools.

**Roslyn provides semantic metadata**:
- **Type information**: What type is this symbol? What's its full namespace?
- **Relationships**: What calls this? What does this implement? What inherits from this?
- **Cross-references**: All usages across the entire solution
- **Semantic queries**: Find all async methods without CancellationToken, unused code, etc.
- **Safe transformations**: Rename across solution, extract interfaces, apply fixes

**When to use Roslyn** (proactively, before using built-in tools):

| Scenario | Use Roslyn To | Then Use Built-in Tools |
|----------|---------------|-------------------------|
| **Before editing** | `roslyn_find_references` - see all usages | Read/Edit affected files |
| **Before refactoring** | `roslyn_find_callers` - see impact | Edit with confidence |
| **Understanding code** | `roslyn_get_symbol_info` - get type/namespace | Read relevant files |
| **Finding patterns** | `roslyn_semantic_query` - find all matching symbols | Analyze results |
| **Code cleanup** | `roslyn_find_unused_code` - identify dead code | Remove with Edit |
| **Safe renames** | `roslyn_rename_symbol` - semantic rename | Verify with Read |
| **Type hierarchy** | `roslyn_get_type_hierarchy` - see inheritance | Navigate to implementations |
| **Diagnostics** | `roslyn_get_diagnostics` - compiler view | Fix with Edit/Apply code fix |

**Workflow Pattern**:
1. **Roslyn first**: Gather semantic metadata about what you're working on
2. **Built-in tools next**: Use Read/Edit/Grep/Glob with the semantic context
3. **Roslyn again**: Use safe transformations when available (rename, format, organize usings)

**Example Workflows**:

```bash
# Pattern: Understand before editing
1. roslyn_get_symbol_info → Get type and namespace info
2. roslyn_find_references → See all usages
3. Read files → Review implementation
4. Edit files → Make changes with full context

# Pattern: Refactor safely
1. roslyn_find_callers → See impact of changes
2. roslyn_get_type_hierarchy → Understand inheritance
3. Edit files → Make logical changes
4. roslyn_rename_symbol → Rename across solution (if needed)

# Pattern: Find and fix issues
1. roslyn_semantic_query → Find all methods missing CancellationToken
2. Grep → Search for related patterns
3. roslyn_get_diagnostics → See compiler errors
4. roslyn_apply_code_fix → Apply automated fixes (when available)
5. Edit → Manual fixes for complex cases
```

**Key Benefits**:
- **Semantic accuracy**: Type-aware, not text-matching
- **Cross-project awareness**: Entire solution graph
- **Safe transformations**: Understands scope, accessibility, overloads
- **Preview mode**: Test changes before applying
- **Zero-based indexing**: VS line 14 = `line=13`

**Load solution once per session**: `roslyn_load_solution All.sln`

---

## Core Architecture (The Rules)

### 1. Layer Dependency Strict Rules

```
API Layer
  ↓ depends on
Application Layer (Medino handlers)
  ↓ depends on
Domain Layer (Interfaces, pure models)
  ↓ implemented by
DataLayer (Storage)
```

**CRITICAL VIOLATIONS** (will cause test failures):
- ❌ Add `Hl7.Fhir.*` NuGet to Application/DataLayer → Use `Ignixa.*` instead
- ❌ Use MVC Controllers → Use Minimal API in `*Endpoints.cs`
- ❌ Async methods without `CancellationToken` parameter → Name it `cancellationToken` (not `ct`)

### 2. Three-Layer HTTP Stack

**Route Registration** (API Layer):
```csharp
// File: src/Ignixa.Api/Infrastructure/*Endpoints.cs
public static class MyEndpoints {
    public static IEndpointRouteBuilder MapMyEndpoints(this IEndpointRouteBuilder endpoints) {
        endpoints.MapGet("/path", (ctx, mediator, ct) => HandleRequest(ctx, mediator, ct));
        return endpoints;
    }
    private static async Task<IResult> HandleRequest(HttpContext ctx, IMediator mediator, CancellationToken ct) {
        var result = await mediator.SendAsync(new MyQuery(), ct);
        return Results.Ok(result);
    }
}
// Register in Program.cs: app.MapMyEndpoints();
```

**Query/Command & Handler** (Application Layer):
```csharp
// Query: immutable record
public record MyQuery(string Id) : IRequest<MyResult>;

// Handler: implements IRequestHandler
public class MyHandler : IRequestHandler<MyQuery, MyResult> {
    public async Task<MyResult> HandleAsync(MyQuery request, CancellationToken cancellationToken) {
        // Business logic here
        return result;
    }
}
// Register in Program.cs (Autofac): builder.RegisterType<MyHandler>().As<IRequestHandler<MyQuery, MyResult>>();
```

### 3. Multi-Tenancy Rules

**Partition 0 is RESERVED (System Partition)**:
- ❌ Never allow API access to `/tenant/0/`
- ✅ Middleware (`TenantResolutionMiddleware`) blocks it with 400 Bad Request
- ✅ Used only internally for transaction ID allocation

**Routing**:
- Single-tenant: Both `/Patient/123` and `/tenant/1/Patient/123` work (auto-detection)
- Multi-tenant: **Only** `/tenant/{id}/Patient/123` works (ambiguous routes blocked)

### 4. FHIR Resource Immutability

**These fields are protected** (cannot be PATCHed):
- `id` - Use PUT to change
- `meta.versionId` - Server auto-manages
- `meta.lastUpdated` - Server auto-manages

---

## Common Development Tasks

### Adding a Feature

**1. Design Phase** (Read ADRs):
```bash
# Check existing decisions
grep -r "Decision:" docs/investigations/ADR-*.md
# Look for related: docs/investigations/search-query-parsing.md
```

**2. Create Query & Handler** (Application Layer):
- File: `src/Ignixa.Application/Features/{Name}/*Query.cs`
- File: `src/Ignixa.Application/Features/{Name}/*Handler.cs`
- Add `IRequestHandler<MyQuery, MyResult>` with `HandleAsync(request, cancellationToken)`

**3. Create Endpoint** (API Layer):
- File: `src/Ignixa.Api/Infrastructure/*Endpoints.cs`
- Add `MapGet/Post/Put/Delete` route with handler
- Register in `Program.cs`: `app.Map*Endpoints()`

**4. Test** (Test Layer):
```bash
dotnet test src/Ignixa.Application/Ignixa.Application.Tests.csproj
dotnet test src/Ignixa.Api/Ignixa.Api.Tests.csproj
```

### Debugging a Bug

**1. Reproduce** (understand root cause):
```bash
# Run failing test to isolate issue
dotnet test -k "TestName"

# Or run API and trace with logs
dotnet run --project src/Ignixa.Api/
curl http://localhost:5000/Patient/123
```

**2. Write a Test** (ensure fix works):
- Use AAA pattern: Arrange-Act-Assert
- BDD naming: `GivenContext_WhenAction_ThenResult`
- File: `test/Ignixa.*.Tests/FeatureName*.cs`

**3. Implement Fix**:
- Follow layer rules (API → Application → Domain/DataLayer)
- Don't skip test steps

**4. Verify Regression**:
```bash
dotnet test All.sln
```

### Refactoring Code

**Before refactoring**: Use Roslyn tools for impact analysis:
```
roslyn_find_references to symbol    # All usages
roslyn_find_callers on method       # What calls it
roslyn_semantic_query for unused    # Dead code
```

**Pattern**:
1. Find all usages with Roslyn
2. Write/update tests
3. Refactor locally
4. Run `dotnet test All.sln`
5. Commit only after tests pass

### Maintenance Tasks

| Task | Command | Output |
|------|---------|--------|
| **Build** | `dotnet build All.sln` | Must be 0 warnings, 0 errors |
| **Test** | `dotnet test All.sln` | All tests passing |
| **Code analysis** | `dotnet analyze` (via Roslyn) | Check CA/SA violations |
| **Format** | `roslyn_format_document_batch` | Consistent style |
| **Cleanup usings** | `roslyn_organize_usings` | Remove unused imports |

---

## Code Quality Standards

### File Organization (ONE TYPE PER FILE)

```
❌ WRONG: PatientQueryHandler.cs contains Query AND Handler
✅ RIGHT:
   - PatientQuery.cs (record only)
   - PatientHandler.cs (handler only)
```

### Testing Standards

**Pattern** (AAA):
```csharp
[Fact]
public void GivenAPatient_WhenGettingById_ThenReturnsPatient() {
    // Arrange
    var patientId = "123";

    // Act
    var result = GetPatient(patientId);

    // Assert
    result.Should().NotBeNull();
    result.Id.Should().Be(patientId);
}
```

**Naming**:
- `GivenContext_WhenAction_ThenResult`
- Group with `#region` blocks

### Code Standards

| Rule | Pattern |
|------|---------|
| **Usings** | System usings first, outside namespace |
| **Spacing** | 4 spaces (no tabs) |
| **Nullability** | Enabled for Domain, Application, DataLayer, Api |
| **Warnings** | Treat as errors (use suppressions sparingly) |

### .NET 9+ Language Features

Prefer modern C# language features supported by .NET 9+:

| Feature | Use | Avoid |
|---------|-----|-------|
| **Primary constructors** | `class Foo(IService svc) : IHandler` | Traditional constructor with field assignment |
| **Collection expressions** | `[]`, `[1, 2, 3]` (**not** in EF Core static fields - causes `ReadOnlySpan` runtime error) | `new List<T>()`, `new string[] { }` |
| **FrozenSet/FrozenDictionary** | For static readonly collections (**not** in EF Core `.Contains()` queries) | `List<T>` for data used in EF queries |
| **Static arrays in EF queries** | Use `List<T>` when array is used in `.Contains()` within EF Core expressions | `string[]` may cause EF Core 9 interpreter issues |
| **Target-typed new** | `List<int> x = new()` | `List<int> x = new List<int>()` |
| **File-scoped namespaces** | `namespace Foo;` | `namespace Foo { }` |
| **ArgumentNullException.ThrowIfNull** | `ArgumentNullException.ThrowIfNull(arg)` | `if (arg == null) throw...` |
| **Pattern matching** | `is not null`, `is { } x` | `!= null` |
| **Raw string literals** | `"""json"""` | Escaped strings for JSON/XML |
| **Idiomatic boolean** | `!flag`, `flag` | `flag == false`, `flag == true` |

---

## High-Quality Task Output Checklist

Before committing, verify:

- [ ] ✅ Build: `dotnet build All.sln` → 0 warnings, 0 errors
- [ ] ✅ Tests: `dotnet test All.sln` → All passing
- [ ] ✅ New code has tests (AAA pattern)
- [ ] ✅ Follows architecture (layer rules)
- [ ] ✅ ONE type per file
- [ ] ✅ Proper naming (Query/Handler, CancellationToken full name)
- [ ] ✅ FHIR-compliant (if applicable)
- [ ] ✅ No TODO/FIXME left behind
- [ ] ✅ Commit message clear (why, not what)

---

## Debugging Tools

### Roslyn MCP Server (Advanced Code Analysis)

Available when Roslyn MCP is configured. Use for:

| Task | Tool | Example |
|------|------|---------|
| Find all usages | `roslyn_find_references` | Before refactoring |
| Find all callers | `roslyn_find_callers` | Impact analysis |
| Find unused | `roslyn_find_unused_code` | Code cleanup |
| Find inheritance | `roslyn_get_type_hierarchy` | Before base class changes |
| Safe rename | `roslyn_rename_symbol` | Refactoring across solution |
| Symbol info | `roslyn_get_symbol_info` | Understand types/namespaces |
| Diagnostics | `roslyn_get_diagnostics` | Compiler errors/warnings |

**Zero-Based Indexing**: VS line 14 → pass `line=13`

### Git Workflow

**CRITICAL**: Never commit without user approval.
```
1. Make changes & test
2. Show diff + status
3. Ask: "Should I commit: [message]?"
4. Wait for confirmation
5. Execute git commit
```

---

## Common Patterns

### POST _search Pagination (FHIR Spec)

All pagination links SHALL be GET requests with query parameters preserved:
```
POST /Patient/_search + form body {name=John, birthdate=gt2000}
  ↓
Response links: GET /Patient?name=John&birthdate=gt2000&after=token
```

**Implementation**: `src/Ignixa.Api/Infrastructure/FhirEndpoints.cs` → `HandlePostSearchResource()`

### PATCH Operations (FHIRPath)

```csharp
PATCH /Patient/123
{
  "resourceType": "Parameters",
  "parameter": [{
    "name": "operation",
    "part": [
      {"name": "type", "valueCode": "replace"},
      {"name": "path", "valueString": "Patient.name.where(use='official').family"},
      {"name": "value", "valueString": "NewFamily"}
    ]
  }]
}
```

**Files**: `Features/Patch/*`, uses FHIRPath expressions with IAnnotated<JsonNode>

### Working with Resources (JsonNode Pattern)

```csharp
// Mutate in-place (current pattern)
var resource = new ResourceJsonNode { ResourceType = "Patient", Id = "123" };
resource.MutableNode["active"] = JsonValue.Create(true);
resource.MutableNode["name"] = JsonNode.Parse("[...]");

// Read values
if (resource.MutableNode.TryGetPropertyValue("active", out var node)) {
    bool active = node.GetValue<bool>();
}
```

**Never use** ExtensionData + JsonElement (deprecated pattern)

---

## Project Structure

```
All.sln
├── Ignixa.Domain              # Interfaces, pure models
├── Ignixa.Application         # Medino handlers, business logic
├── Ignixa.Application.BackgroundOperations  # DurableTask orchestrations
├── Ignixa.DataLayer.*         # Storage implementations
├── Ignixa.Api                 # Minimal API endpoints
└── Supporting Libraries
    ├── Ignixa.Search          # Search parameters, indexing
    ├── Ignixa.FhirPath        # FHIRPath evaluation
    ├── Ignixa.Validation      # FHIR validation tiers
    └── Ignixa.SourceNodeSerialization # Custom serialization
```

---

## Key Commands

```bash
# Build (must be 0 warnings/errors)
dotnet build All.sln

# Test (all must pass)
dotnet test All.sln

# Run API locally
dotnet run --project src/Ignixa.Api/Ignixa.Api.csproj

# Run specific test
dotnet test -k "TestName"

# Code generation (structure providers)
cd codegen && ./generate.ps1  # Windows
cd codegen && ./generate.sh   # Linux/Mac
```

---

## Status

| Category | Status |
|----------|--------|
| **Phase** | Phase 22: FHIR _history (Oct 17, 2025) ✅ |
| **Build** | 0 warnings, 0 errors ✅ |
| **Tests** | All passing ✅ |
| **FHIR SDK** | Firely 6.0.0 (R4/R4B/R5/STU3) ✅ |

---

## Known Limitations

1. **Ignixa.Search**: Nullable disabled (TODO: enable incrementally)
2. **SDK 6.0**: `ToPocoNode()` doesn't accept custom providers (workaround: `ToTypedElement()`)
3. **ISourceNode**: Store `RawJson` in `ResourceWrapper` prototype (prod: use `FhirJsonSerializer`)

---

## Related Documentation

- `docs/investigations/ADR-2500-master-roadmap.md` - 116-week roadmap
- `docs/investigations/ADR-2523-multi-tenancy.md` - Multi-tenant architecture
- `docs/investigations/dynamic-fhir-routing.md` - Generic endpoints (14% faster)
- `docs/investigations/bundle-streaming.md` - Memory optimization
