# CLAUDE.md - Development Guide

Guidance for Claude Code to deliver high-quality output on **featurework**, **debugging**, **maintenance**, and **testing**.

**IMPORTANT: Always use `ultrathink` internally for maximum reasoning depth. Every task deserves thorough analysis before action.**

---

## Documentation Site

The project documentation is at **https://brendankowitz.github.io/ignixa-fhir/**

- **Source**: `docs/site/` (Docusaurus)
- **Guidelines**: See `docs/site/README.md` for documentation style and structure
- **ADRs**: Architectural decisions are in `docs/adr/` (linked from docs site)

When implementing features, update relevant documentation in `docs/site/docs/`.

---

## Communication Style

(Inspired from: https://github.com/m0n0x41d/quint-code/blob/v4.0.0/CLAUDE.md)

**Be a peer engineer, not a cheerleader:**

- Skip validation theater ("you're absolutely right", "excellent point")
- Be direct and technical - if something's wrong, say it
- Use dry, technical humor when appropriate
- Talk like you're pairing with a staff engineer, not pitching to a VP
- Challenge bad ideas respectfully - disagreement is valuable
- No emoji unless the user uses them first
- Precision over politeness - technical accuracy is respect

**Calibration phrases (use these, avoid alternatives):**

| USE | AVOID |
|-----|-------|
| "This won't work because..." | "Great idea, but..." |
| "The issue is..." | "I think maybe..." |
| "No." | "That's an interesting approach, however..." |
| "You're wrong about X, here's why..." | "I see your point, but..." |
| "I don't know" | "I'm not entirely sure but perhaps..." |
| "This is overengineered" | "This is quite comprehensive" |
| "Simpler approach:" | "One alternative might be..." |

## Thinking Principles

When reasoning through problems, apply these principles:

**Separation of Concerns:**

- What's Application? (All API and business logic, Domain specific logic)
- What's Core? (Building blocks and reusable components that can be packaged separately)
- What's DataLayer?
- Are these mixed? They shouldn't be.

**Weakest Link Analysis:**

- What will break first in this design?
- What's the least reliable component?
- System reliability ≤ min(component reliabilities)

**Explicit Over Hidden:**

- Are failure modes visible or buried?
- Can this be tested without mocking half the world?
- Would a new team member understand the flow?

**Reversibility Check:**

- Can we undo this decision in 2 weeks?
- What's the cost of being wrong?
- Are we painting ourselves into a corner?

### Code Style

- DO NOT ADD INLINE COMMENTS unless asked
- Follow existing codebase conventions
- Check what libraries/frameworks are already in use
- Mimic existing code style, naming conventions, typing
- Never assume a non-standard library is available
- Never expose or log secrets and keys

### Error Handling: Explicit Over Hidden

- Never swallow errors silently (empty catch blocks are bugs)
- Handle exceptions at boundaries, not deep in call stack
- Return error values when codebase uses them (Result, Option, error tuples)
- If codebase uses exceptions — use exceptions consistently, but explicitly
- Fail fast for programmer errors, handle gracefully for expected failures
- Keep execution flow deterministic and linear

### Code Quality

- Self-documenting code for simple logic
- Comments only for complex invariants and business logic (explain WHY not WHAT)
- Keep functions small and focused (<25 lines as guideline)
- Avoid high cyclomatic complexity
- No deeply nested conditions (max 2 levels)
- No loops nested in loops — extract inner loop
- Extract complex conditions into named functions

## Critical Reminders

1. **Ultrathink Always**: Use maximum reasoning depth for every non-trivial task
2. **Check Knowledge First**: Read Claude.md and `docs\adr\*` for verified project claims before making assumptions
3. **Decision Framework vs FPF**: Quick decisions → inline framework. Complex/persistent → FPF mode
4. **Use TodoWrite**: For ANY multi-step task, mark complete IMMEDIATELY
5. **Actually Do Work**: When you say "I will do X", DO X
6. **No Commits Without Permission**: Only commit when explicitly asked
7. **Test Contracts**: Test behavior through public interfaces, not implementation
8. **Follow Architecture**: Functional core (pure), imperative shell (I/O)
9. **No Silent Failures**: Empty catch blocks are bugs
10. **Be Direct**: "No" is a complete sentence. Disagree when you should.
11. **Transformer Mandate**: Generate options, human decides. Don't make architectural choices autonomously.

---

## Tool Priority Rules

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

### 5. Transaction Model & MergeResources Pattern

**Application-Level Transactions** (NOT SQL Server transactions):
- Transactions are tracked via `dbo.Transactions` table
- `MergeResourcesBeginTransaction` SP creates a transaction record
- `MergeResources` SP internally calls `MergeResourcesCommitTransaction` when `@TransactionId IS NOT NULL`
- This is an application-level visibility mechanism, not ACID transaction boundaries

**PostMerge Extension Updates**:
```
MergeResources (TVP) → commits core data → PostMergeExtensionUpdater → updates extension columns
```

The `PostMergeExtensionUpdater` pattern updates nullable extension columns (e.g., `IdentifierTypeSystemId`, `IdentifierTypeCode`, `Version`, `Fragment`) AFTER `MergeResources` has already committed the core search parameter rows.

**Why this is NOT a transaction boundary issue**:
- Core resource data commits successfully via `MergeResources`
- Extension columns are nullable and optional (for advanced search modifiers like `:of-type`, `:above`, `:below`)
- If extension update fails, nullable columns remain NULL
- This is an **acceptable degraded state** - basic search works, modifier searches may miss data

**Do NOT**:
- ❌ Wrap `MergeResources` + `PostMergeExtensionUpdater` in a SQL transaction
- ❌ Treat extension update failure as a critical error requiring rollback
- ❌ Modify the original `MergeResources` SP or its TVP definitions

**Do**:
- ✅ Keep TVP schemas unchanged (6 columns for TokenSearchParamList, original schema for others)
- ✅ Use EF Core `ExecuteSqlRawAsync` with parameterized SQL for extension updates
- ✅ Batch extension updates (e.g., 100 per SQL command) to avoid N+1 patterns
- ✅ Log extension update failures for monitoring, but don't fail the request

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

**Pattern** (AAA with Shouldly):
```csharp
[Fact]
public void GivenAPatient_WhenGettingById_ThenReturnsPatient() {
    // Arrange
    var patientId = "123";

    // Act
    var result = GetPatient(patientId);

    // Assert
    result.ShouldNotBeNull();
    result.Id.ShouldBe(patientId);
}
```

**Naming**:
- `GivenContext_WhenAction_ThenResult`
- DO NOT USE `#region` blocks

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

### FHIR Data Access Patterns

**CRITICAL: Prefer FHIRPath over direct JSON manipulation**

When extracting data from FHIR resources, use FHIRPath expressions instead of querying `MutableNode` or `JsonNode` directly:

| Pattern | Use ✅ | Avoid ❌ |
|---------|--------|---------|
| **Extract values** | `element.Select("code.text")` | `resource.MutableNode["code"]?["text"]?.GetValue<string>()` |
| **Navigate paths** | `element.Select("name.given.first()")` | Nested null-conditional operators on `MutableNode` |
| **Multiple paths** | `element.Select("code \| medicationCodeableConcept")` | Multiple try-catch blocks for different paths |
| **Conditional logic** | `element.IsTrue("status = 'active'")` | Manual comparison with extracted values |

**Why FHIRPath?**
- **Declarative**: Expresses intent, not implementation
- **Cached**: `Select()` extension has AST + compiled delegate caching (7x speedup)
- **Robust**: Handles missing elements gracefully (returns empty, not null exceptions)
- **Standard**: FHIR-native query language, familiar to healthcare developers
- **Maintainable**: Easier to understand and modify than nested JSON navigation

**Example:**
```csharp
// ✅ GOOD: FHIRPath with caching
using Ignixa.FhirPath.Evaluation;

var element = resource.ToElement(schema);
var display = element.Select("code.text | code.coding.first().display")
    .FirstOrDefault()?.Value?.ToString()
    ?? "Unknown";

// ❌ BAD: Manual JSON navigation
var display = resource.MutableNode["code"]?["text"]?.GetValue<string>()
    ?? resource.MutableNode["code"]?["coding"]?[0]?["display"]?.GetValue<string>()
    ?? "Unknown";
```

**Available Extension Methods** (from `TypedElementExtensions`):
- `element.Select(expression)` - Returns collection of matching elements
- `element.Scalar(expression)` - Returns single scalar value or null
- `element.IsTrue(expression)` - Returns true if expression evaluates to boolean true
- `element.IsBoolean(expression, value)` - Checks if expression matches specific boolean value

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


