# SQL on FHIR 2.1.0-pre Compliance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the two core evaluation/model gaps from SQL on FHIR 2.1.0-pre: `%rowIndex` environment variable support in forEach iteration, and column `tag` passthrough; plus minor model completions for spec conformance.

**Architecture:** All changes are within `Ignixa.SqlOnFhir` (the core evaluator library). No API layer changes required. The evaluator is a pure visitor pattern — `%rowIndex` is injected into `EvaluationContext` per-iteration; `tag` flows through model → `ColumnExpression` → `ColumnSchema` as an immutable array of name/value pairs. Model completions (`fhirVersion`, `profile`, `where.description`) add properties to the deserialization model only with no evaluation impact.

**Tech Stack:** C# 13 / .NET 9, xUnit, `Ignixa.FhirPath.Evaluation.EvaluationContext` (immutable record with `WithEnvironmentVariable`), `System.Collections.Immutable`, `System.Text.Json`

**Out of scope:** New server operations (`$viewdefinition-run` enhancements, `$viewdefinition-export`, `$materialize`, `$sqlquery-run`), derived profiles (`TabularViewDefinition`, `ShareableViewDefinition`), and updating the embedded test suite package from v2.0.0 to v2.1.0-pre.

---

## File Map

| File | Action | Reason |
|------|--------|--------|
| `src/Core/Ignixa.SqlOnFhir/Evaluation/SqlOnFhirEvaluationVisitor.cs` | Modify | Inject `%rowIndex` into context during forEach/forEachOrNull loop |
| `src/Core/Ignixa.SqlOnFhir/Models/ColumnTag.cs` | **Create** | New model for tag name/value pair |
| `src/Core/Ignixa.SqlOnFhir/Models/ViewColumnDefinition.cs` | Modify | Add `IList<ColumnTag>? Tag` property |
| `src/Core/Ignixa.SqlOnFhir/Expressions/ViewDefinitionExpression.cs` | Modify | Add `Tags` parameter to `ColumnExpression` record |
| `src/Core/Ignixa.SqlOnFhir/Parsing/ViewDefinitionExpressionParser.cs` | Modify | Parse `tag` children in `ParseColumns` |
| `src/Core/Ignixa.SqlOnFhir/Evaluation/SqlOnFhirSchemaEvaluator.cs` | Modify | Add `Tags` to `ColumnSchema` record |
| `src/Core/Ignixa.SqlOnFhir/Evaluation/SqlOnFhirSchemaVisitor.cs` | Modify | Pass tags through in `Visit(ColumnExpression)` |
| `src/Core/Ignixa.SqlOnFhir/Models/ViewDefinition.cs` | Modify | Add `FhirVersion` and `Profile` properties |
| `src/Core/Ignixa.SqlOnFhir/Models/WhereClause.cs` | Modify | Add `Description` property |
| `test/Ignixa.SqlOnFhir.Tests/FhirPathColumnEvaluatorTests.cs` | Modify | Add `%rowIndex` tests |
| `test/Ignixa.SqlOnFhir.Tests/SqlOnFhirSchemaEvaluatorTests.cs` | Modify | Add column tag tests |

---

## Task 1: `%rowIndex` in forEach/forEachOrNull Evaluation

The spec adds `%rowIndex` as a 0-based integer environment variable available during forEach and forEachOrNull iteration. Currently the evaluator loops without tracking position.

**File:** `src/Core/Ignixa.SqlOnFhir/Evaluation/SqlOnFhirEvaluationVisitor.cs` (~line 190)

**Key context:**
- `_currentContext` is an `EvaluationContext` record — `WithEnvironmentVariable(name, IElement)` creates a new immutable context
- Constants are already injected via `context.WithEnvironmentVariable(constant.Name, element)` in `CreateEvaluationContext`
- `PrimitiveValueElement` is a private nested class in the same file that wraps primitives as `IElement`
- `%rowIndex` must be scoped to each iteration; restore `_currentContext` to base after the loop

---

- [ ] **Step 1: Write the failing test**

Add to the `ForEach Tests` region in `test/Ignixa.SqlOnFhir.Tests/FhirPathColumnEvaluatorTests.cs`:

```csharp
[Fact]
public void GivenForEach_WhenUsingRowIndex_ThenReturns0BasedIndex()
{
    // Arrange
    var patientJson = new Dictionary<string, object?>
    {
        { "resourceType", "Patient" },
        { "id", "P001" },
        { "name", new object[]
            {
                new Dictionary<string, object?> { { "family", "Smith" } },
                new Dictionary<string, object?> { { "family", "Jones" } },
                new Dictionary<string, object?> { { "family", "Brown" } }
            }
        }
    };
    var resource = CreateTypedElement(patientJson);

    var viewDef = new ViewDefinition
    {
        Resource = "Patient",
        Select = new List<SelectGroup>
        {
            new SelectGroup
            {
                ForEach = "name",
                Column = new List<ViewColumnDefinition>
                {
                    new ViewColumnDefinition { Name = "row_index", Path = "%rowIndex", Type = "integer" },
                    new ViewColumnDefinition { Name = "family", Path = "family", Type = "string" }
                }
            }
        }
    };

    // Act
    var rows = _evaluator.Evaluate(ConvertToSourceNode(viewDef), resource).ToList();

    // Assert
    Assert.Equal(3, rows.Count);
    Assert.Equal(0, rows[0]["row_index"]);
    Assert.Equal("Smith", rows[0]["family"]);
    Assert.Equal(1, rows[1]["row_index"]);
    Assert.Equal("Jones", rows[1]["family"]);
    Assert.Equal(2, rows[2]["row_index"]);
    Assert.Equal("Brown", rows[2]["family"]);
}

[Fact]
public void GivenForEachOrNull_WhenUsingRowIndex_ThenReturns0BasedIndex()
{
    // Arrange
    var patientJson = new Dictionary<string, object?>
    {
        { "resourceType", "Patient" },
        { "id", "P001" },
        { "name", new object[]
            {
                new Dictionary<string, object?> { { "family", "Smith" } },
                new Dictionary<string, object?> { { "family", "Jones" } }
            }
        }
    };
    var resource = CreateTypedElement(patientJson);

    var viewDef = new ViewDefinition
    {
        Resource = "Patient",
        Select = new List<SelectGroup>
        {
            new SelectGroup
            {
                ForEachOrNull = "name",
                Column = new List<ViewColumnDefinition>
                {
                    new ViewColumnDefinition { Name = "row_index", Path = "%rowIndex", Type = "integer" },
                    new ViewColumnDefinition { Name = "family", Path = "family", Type = "string" }
                }
            }
        }
    };

    // Act
    var rows = _evaluator.Evaluate(ConvertToSourceNode(viewDef), resource).ToList();

    // Assert
    Assert.Equal(2, rows.Count);
    Assert.Equal(0, rows[0]["row_index"]);
    Assert.Equal(1, rows[1]["row_index"]);
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test test/Ignixa.SqlOnFhir.Tests/Ignixa.SqlOnFhir.Tests.csproj -k "GivenForEach_WhenUsingRowIndex" --no-build -v n
```

Expected: FAIL — `%rowIndex` evaluates to null (no such env variable injected).

- [ ] **Step 3: Implement `%rowIndex` injection in `SqlOnFhirEvaluationVisitor.cs`**

Locate the `Visit(SelectExpression node)` method at ~line 190. Find this block:

```csharp
// forEach: unnest collection
var forEachExpr = node.ForEach ?? node.ForEachOrNull!;
var items = _evaluator.Evaluate(_currentResource, forEachExpr, _currentContext!);

var rows = new List<Dictionary<string, object?>>();

foreach (var item in items)
{
    // Temporarily switch context to the forEach item
    var previousResource = _currentResource;
    _currentResource = item;

    var row = EvaluateColumns(node.Columns);

    // Process nested SELECT and UnionAll WITHIN the forEach context
    // This ensures the context is correct for evaluating nested expressions
    var rowsForThisItem = ProcessNestedSelectsCartesian(new[] { row }, node.NestedSelect);
    rowsForThisItem = ProcessUnionAllConcat(rowsForThisItem, node.UnionAll);

    rows.AddRange(rowsForThisItem);

    _currentResource = previousResource;
}
```

Replace with:

```csharp
// forEach: unnest collection
var forEachExpr = node.ForEach ?? node.ForEachOrNull!;
var items = _evaluator.Evaluate(_currentResource, forEachExpr, _currentContext!).ToList();

var rows = new List<Dictionary<string, object?>>();
var baseContext = _currentContext!;

for (int rowIndex = 0; rowIndex < items.Count; rowIndex++)
{
    var item = items[rowIndex];
    var previousResource = _currentResource;
    _currentResource = item;
    _currentContext = baseContext.WithEnvironmentVariable("rowIndex", new PrimitiveValueElement(rowIndex));

    var row = EvaluateColumns(node.Columns);

    var rowsForThisItem = ProcessNestedSelectsCartesian(new[] { row }, node.NestedSelect);
    rowsForThisItem = ProcessUnionAllConcat(rowsForThisItem, node.UnionAll);

    rows.AddRange(rowsForThisItem);

    _currentResource = previousResource;
}

_currentContext = baseContext;
```

- [ ] **Step 4: Build to check for errors**

```
dotnet build src/Core/Ignixa.SqlOnFhir/Ignixa.SqlOnFhir.csproj
```

Expected: 0 warnings, 0 errors.

- [ ] **Step 5: Run the new tests to verify they pass**

```
dotnet test test/Ignixa.SqlOnFhir.Tests/Ignixa.SqlOnFhir.Tests.csproj -k "GivenForEach_WhenUsingRowIndex" -v n
```

Expected: 2 tests PASS.

- [ ] **Step 6: Run the full test suite to check for regressions**

```
dotnet test All.sln
```

Expected: All existing tests still pass.

- [ ] **Step 7: Commit**

```
git add src/Core/Ignixa.SqlOnFhir/Evaluation/SqlOnFhirEvaluationVisitor.cs
git add test/Ignixa.SqlOnFhir.Tests/FhirPathColumnEvaluatorTests.cs
git commit -m "feat(sqlonfhir): implement %rowIndex environment variable for forEach iteration"
```

---

## Task 2: Column `tag` Support

The 2.1.0-pre spec adds `select.column.tag[0..*]` with `name` and `value` string fields. Tags are implementation metadata (e.g., `{"name": "ansi/type", "value": "VARCHAR(100)"}`). Our model, parser, expression tree, and schema output all need to carry them through.

---

### Step 2.1 — Create `ColumnTag` model

- [ ] **Create `src/Core/Ignixa.SqlOnFhir/Models/ColumnTag.cs`**

```csharp
namespace Ignixa.SqlOnFhir.Models;

public class ColumnTag
{
    public required string Name { get; set; }
    public required string Value { get; set; }
}
```

- [ ] **Build to verify the new file compiles**

```
dotnet build src/Core/Ignixa.SqlOnFhir/Ignixa.SqlOnFhir.csproj
```

Expected: 0 errors.

---

### Step 2.2 — Add `Tag` to `ViewColumnDefinition`

- [ ] **Modify `src/Core/Ignixa.SqlOnFhir/Models/ViewColumnDefinition.cs`**

After the `Collection` property, add:

```csharp
    /// <summary>
    /// Implementation-specific metadata tags attached to this column.
    /// Each tag has a namespaced name (e.g., "ansi/type") and a string value.
    /// </summary>
#pragma warning disable CA2227
    public IList<ColumnTag>? Tag { get; set; }
#pragma warning restore CA2227
```

- [ ] **Build**

```
dotnet build src/Core/Ignixa.SqlOnFhir/Ignixa.SqlOnFhir.csproj
```

Expected: 0 errors.

---

### Step 2.3 — Add `Tags` to `ColumnExpression` record

- [ ] **Modify `src/Core/Ignixa.SqlOnFhir/Expressions/ViewDefinitionExpression.cs`**

Replace the `ColumnExpression` record:

```csharp
/// <summary>
/// Expression representing a column definition with compiled FHIRPath expression.
/// </summary>
public sealed record ColumnExpression(
    string Name,
    Expression Path,
    string? Type,
    bool Collection,
    ImmutableArray<(string Name, string Value)> Tags = default) : SqlOnFhirExpression
{
    public override TResult Accept<TResult>(ISqlOnFhirExpressionVisitor<TResult> visitor)
        => visitor.Visit(this);
}
```

- [ ] **Build**

```
dotnet build src/Core/Ignixa.SqlOnFhir/Ignixa.SqlOnFhir.csproj
```

Expected: 0 errors.

---

### Step 2.4 — Add `Tags` to `ColumnSchema` record

- [ ] **Modify `src/Core/Ignixa.SqlOnFhir/Evaluation/SqlOnFhirSchemaEvaluator.cs`**

Replace the `ColumnSchema` record:

```csharp
/// <summary>
/// Column schema information extracted from a ViewDefinition.
/// </summary>
public record ColumnSchema(
    string Name,
    string? Type,
    bool Collection,
    IReadOnlyList<(string Name, string Value)>? Tags = null);
```

- [ ] **Build**

```
dotnet build src/Core/Ignixa.SqlOnFhir/Ignixa.SqlOnFhir.csproj
```

Expected: 0 errors.

---

### Step 2.5 — Write the failing schema tag test

Add to `test/Ignixa.SqlOnFhir.Tests/SqlOnFhirSchemaEvaluatorTests.cs` (before the closing `}`):

```csharp
#region Tag Tests

[Fact]
public void GivenColumnWithTags_WhenExtractingSchema_ThenTagsIncludedInSchema()
{
    // Arrange
    var viewDef = new ViewDefinition
    {
        Resource = "Patient",
        Select = new List<SelectGroup>
        {
            new SelectGroup
            {
                Column = new List<ViewColumnDefinition>
                {
                    new ViewColumnDefinition
                    {
                        Name = "id",
                        Path = "id",
                        Type = "id",
                        Tag = new List<ColumnTag>
                        {
                            new ColumnTag { Name = "ansi/type", Value = "VARCHAR(64)" },
                            new ColumnTag { Name = "custom/indexed", Value = "true" }
                        }
                    }
                }
            }
        }
    };

    // Act
    var schema = _evaluator.GetSchema(ParseViewDefinition(viewDef));

    // Assert
    Assert.Single(schema);
    Assert.NotNull(schema[0].Tags);
    Assert.Equal(2, schema[0].Tags!.Count);
    Assert.Equal("ansi/type", schema[0].Tags[0].Name);
    Assert.Equal("VARCHAR(64)", schema[0].Tags[0].Value);
    Assert.Equal("custom/indexed", schema[0].Tags[1].Name);
    Assert.Equal("true", schema[0].Tags[1].Value);
}

[Fact]
public void GivenColumnWithNoTags_WhenExtractingSchema_ThenTagsIsNull()
{
    // Arrange
    var viewDef = new ViewDefinition
    {
        Resource = "Patient",
        Select = new List<SelectGroup>
        {
            new SelectGroup
            {
                Column = new List<ViewColumnDefinition>
                {
                    new ViewColumnDefinition { Name = "id", Path = "id", Type = "id" }
                }
            }
        }
    };

    // Act
    var schema = _evaluator.GetSchema(ParseViewDefinition(viewDef));

    // Assert
    Assert.Single(schema);
    Assert.Null(schema[0].Tags);
}

#endregion
```

- [ ] **Run the tag tests to verify they fail**

```
dotnet test test/Ignixa.SqlOnFhir.Tests/Ignixa.SqlOnFhir.Tests.csproj -k "GivenColumnWithTags" -v n
```

Expected: FAIL — `ColumnSchema.Tags` does not exist yet or is always null.

---

### Step 2.6 — Parse tags in `ViewDefinitionExpressionParser.cs`

Locate `ParseColumns` (~line 202). Find the block that ends with:

```csharp
            // Compile FHIRPath expression once during parsing
            var path = Parser.Parse(pathText);

            builder.Add(new ColumnExpression(
                Name: name,
                Path: path,
                Type: type,
                Collection: collection
            ));
```

Replace with:

```csharp
            // Compile FHIRPath expression once during parsing
            var path = Parser.Parse(pathText);

            // Parse tags (0..* per spec)
            var tagNodes = columnNode.Children("tag").ToList();
            var tags = ImmutableArray<(string Name, string Value)>.Empty;
            if (tagNodes.Count > 0)
            {
                var tagBuilder = ImmutableArray.CreateBuilder<(string Name, string Value)>(tagNodes.Count);
                foreach (var tagNode in tagNodes)
                {
                    var tagName = tagNode.Children("name").FirstOrDefault()?.Text
                        ?? throw new InvalidOperationException("Column tag must have a 'name' property");
                    var tagValue = tagNode.Children("value").FirstOrDefault()?.Text
                        ?? throw new InvalidOperationException("Column tag must have a 'value' property");
                    tagBuilder.Add((tagName, tagValue));
                }
                tags = tagBuilder.ToImmutable();
            }

            builder.Add(new ColumnExpression(
                Name: name,
                Path: path,
                Type: type,
                Collection: collection,
                Tags: tags
            ));
```

---

### Step 2.7 — Pass tags through the schema visitor

- [ ] **Modify `src/Core/Ignixa.SqlOnFhir/Evaluation/SqlOnFhirSchemaVisitor.cs`**

Replace the `Visit(ColumnExpression node)` method body:

```csharp
    public IReadOnlyList<ColumnSchema> Visit(ColumnExpression node)
    {
        IReadOnlyList<(string Name, string Value)>? tags = node.Tags.IsDefaultOrEmpty
            ? null
            : node.Tags;

        return new List<ColumnSchema>
        {
            new ColumnSchema(node.Name, node.Type, node.Collection, tags)
        };
    }
```

- [ ] **Build**

```
dotnet build src/Core/Ignixa.SqlOnFhir/Ignixa.SqlOnFhir.csproj
```

Expected: 0 errors.

- [ ] **Run tag tests to verify they pass**

```
dotnet test test/Ignixa.SqlOnFhir.Tests/Ignixa.SqlOnFhir.Tests.csproj -k "GivenColumnWithTags" -v n
```

Expected: 2 tests PASS.

- [ ] **Run full test suite to check for regressions**

```
dotnet test All.sln
```

Expected: All tests pass.

- [ ] **Commit**

```
git add src/Core/Ignixa.SqlOnFhir/Models/ColumnTag.cs
git add src/Core/Ignixa.SqlOnFhir/Models/ViewColumnDefinition.cs
git add src/Core/Ignixa.SqlOnFhir/Expressions/ViewDefinitionExpression.cs
git add src/Core/Ignixa.SqlOnFhir/Parsing/ViewDefinitionExpressionParser.cs
git add src/Core/Ignixa.SqlOnFhir/Evaluation/SqlOnFhirSchemaEvaluator.cs
git add src/Core/Ignixa.SqlOnFhir/Evaluation/SqlOnFhirSchemaVisitor.cs
git add test/Ignixa.SqlOnFhir.Tests/SqlOnFhirSchemaEvaluatorTests.cs
git commit -m "feat(sqlonfhir): add column tag support per SQL on FHIR 2.1.0-pre spec"
```

---

## Task 3: Model Completions

The 2.1.0-pre spec adds `fhirVersion[0..*]` and `profile[0..*]` to the root `ViewDefinition`, and `description[0..1]` to `where` clauses. These are metadata fields — they have no evaluation impact. The changes are to the deserialization model (`Models/`) only; no parser or expression tree changes are needed.

---

- [ ] **Step 1: Write a build-time verification test (schema test)**

Add to `test/Ignixa.SqlOnFhir.Tests/SqlOnFhirSchemaEvaluatorTests.cs`:

```csharp
[Fact]
public void GivenViewDefinitionWithFhirVersionAndProfile_WhenParsed_ThenModelAcceptsFields()
{
    // Verifies the model accepts these fields without errors (no evaluation impact)
    var viewDef = new ViewDefinition
    {
        Resource = "Patient",
        FhirVersion = new List<string> { "4.0.1", "5.0.0" },
        Profile = new List<string> { "http://hl7.org/fhir/StructureDefinition/Patient" },
        Where = new List<WhereClause>
        {
            new WhereClause { Path = "active = true", Description = "Only active patients" }
        },
        Select = new List<SelectGroup>
        {
            new SelectGroup
            {
                Column = new List<ViewColumnDefinition>
                {
                    new ViewColumnDefinition { Name = "id", Path = "id", Type = "id" }
                }
            }
        }
    };

    var schema = _evaluator.GetSchema(ParseViewDefinition(viewDef));

    Assert.Single(schema);
    Assert.Equal("id", schema[0].Name);
}
```

- [ ] **Step 2: Run the test to verify it fails (compilation error)**

```
dotnet test test/Ignixa.SqlOnFhir.Tests/Ignixa.SqlOnFhir.Tests.csproj -k "GivenViewDefinitionWithFhirVersion" -v n
```

Expected: Build error — `ViewDefinition` has no `FhirVersion` or `Profile`; `WhereClause` has no `Description`.

- [ ] **Step 3: Add `FhirVersion` and `Profile` to `ViewDefinition.cs`**

After the `Select` property, add (before the closing `#pragma restore`):

```csharp
    /// <summary>
    /// FHIR versions this ViewDefinition is compatible with (e.g., "4.0.1", "5.0.0").
    /// Informational only — does not affect evaluation.
    /// </summary>
#pragma warning disable CA2227
    public IList<string>? FhirVersion { get; set; }

    /// <summary>
    /// Canonical URLs of StructureDefinition profiles that constrain the resource.
    /// Informational only — does not affect evaluation.
    /// </summary>
    public IList<string>? Profile { get; set; }
#pragma warning restore CA2227
```

- [ ] **Step 4: Add `Description` to `WhereClause.cs`**

After the `Path` property:

```csharp
    /// <summary>
    /// Human-readable description of what this WHERE clause filters.
    /// Informational only — does not affect evaluation.
    /// </summary>
    public string? Description { get; set; }
```

- [ ] **Step 5: Build**

```
dotnet build src/Core/Ignixa.SqlOnFhir/Ignixa.SqlOnFhir.csproj
```

Expected: 0 errors.

- [ ] **Step 6: Run the test to verify it passes**

```
dotnet test test/Ignixa.SqlOnFhir.Tests/Ignixa.SqlOnFhir.Tests.csproj -k "GivenViewDefinitionWithFhirVersion" -v n
```

Expected: PASS.

- [ ] **Step 7: Run full suite**

```
dotnet test All.sln
```

Expected: All tests pass.

- [ ] **Step 8: Commit**

```
git add src/Core/Ignixa.SqlOnFhir/Models/ViewDefinition.cs
git add src/Core/Ignixa.SqlOnFhir/Models/WhereClause.cs
git add test/Ignixa.SqlOnFhir.Tests/SqlOnFhirSchemaEvaluatorTests.cs
git commit -m "feat(sqlonfhir): add fhirVersion, profile, and where.description model fields per 2.1.0-pre spec"
```

---

## Self-Review

**Spec coverage check:**

| 2.1.0-pre gap | Covered by |
|---|---|
| `%rowIndex` in forEach/forEachOrNull | Task 1 |
| Column `tag[0..*]` (name + value) | Task 2 |
| `ViewDefinition.fhirVersion[0..*]` | Task 3 |
| `ViewDefinition.profile[0..*]` | Task 3 |
| `where.description[0..1]` | Task 3 |
| New server operations | Out of scope (noted above) |
| Derived profiles (Tabular, Shareable) | Out of scope (noted above) |

**Placeholder scan:** No TBDs, no "handle edge cases" instructions. All code blocks are complete.

**Type consistency check:**
- `ColumnTag` in `Models/` is the deserialization model only. The expression layer uses `(string Name, string Value)` tuples (no reference to `ColumnTag`). These are parallel representations — JSON → `ColumnTag` via model → `ConvertToSourceNode` → ISourceNavigator → Parser → `(string Name, string Value)` tuples in `ColumnExpression`. This is consistent with how the rest of the models work.
- `ColumnSchema.Tags` type is `IReadOnlyList<(string Name, string Value)>?` — matches what `SqlOnFhirSchemaVisitor` assigns (`ImmutableArray<T>` implements `IReadOnlyList<T>`).
- `ColumnExpression.Tags` is `ImmutableArray<(string Name, string Value)>` throughout — consistent across expression definition, parser assignment, and schema visitor read.
