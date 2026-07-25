# Search SQL Phase 4 Approximate Comparator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement deterministic FHIR `:ap` comparison for number, quantity, date, composites, and `_lastUpdated`.

**Architecture:** Numeric approximation is a pure range calculation with a fixed 10 percent tolerance floor. Date approximation receives one explicit reference instant captured by `SearchCompiler` through `TimeProvider`; that instant is threaded through `Lower`, `StructuralContext`, and `LeafContext`.

**Tech Stack:** C# / .NET 9+, `TimeProvider`, xUnit, Shouldly.

**Prerequisite:** Phase 1 must be complete so quantity identity constraints compose with `:ap`.

---

### Task 1: Thread one approximation reference time through lowering

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Tracing/SearchCompiler.cs:32-98`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs:23-47`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs:17-35`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/LeafContext.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Tracing/SearchTraceTests.cs`

- [ ] **Step 1: Write reference-time propagation tests**

Add a `LeafContext` assertion through a date lowerer test and a trace test using:

```csharp
private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
```

The trace test supplies the provider and later asserts emitted date parameters derive from that exact instant.

- [ ] **Step 2: Run tests and verify no clock seam exists**

- [ ] **Step 3: Add optional trailing parameters**

Preserve existing positional callers:

```csharp
// SearchCompiler.CompileAsync, after cancellationToken
TimeProvider? timeProvider = null

// Lower.Run, after top
DateTimeOffset? approximationReferenceTime = null
```

`SearchCompiler` captures `(timeProvider ?? TimeProvider.System).GetUtcNow()` once and passes it to `Lower.Run`.

`StructuralContext` constructs `LeafContext(symbols, approximationReferenceTime)`. In `Lower.Run`, use `context.LeafContext` for resource-column extraction instead of constructing a second context.

Expose:

```csharp
public DateTimeOffset? ApproximationReferenceTime { get; }
```

- [ ] **Step 4: Run all Lower and trace tests**

```powershell
dotnet test test\Ignixa.Search.Sql.Tests\Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~LowerTests|FullyQualifiedName~SearchTrace"
```

- [ ] **Step 5: Commit**

```powershell
git add src\Core\Ignixa.Search.Sql\Tracing\SearchCompiler.cs src\Core\Ignixa.Search.Sql\Lowering test\Ignixa.Search.Sql.Tests
git commit -m "Thread approximation time through lowering"
```

### Task 2: Implement numeric approximation

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/NumericRangeComparison.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/NumberLoweringRuleTests.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/QuantityLoweringRuleTests.cs`

- [ ] **Step 1: Replace `:ap` throw tests with range tests**

Cover `5.4m`, `-50m`, `0m`, and `0.001m`. Assert:

```text
tolerance = max(value.GetPrescisionModifier(), abs(value) * 0.10m)
LowValue >= value - tolerance
HighValue <= value + tolerance
```

- [ ] **Step 2: Run tests and verify `NumericRangeComparison` throws**

- [ ] **Step 3: Add `BuildApproximate` and dispatch to it**

```csharp
private static Predicate BuildApproximate(
    LeafContext context,
    SqlColumnRef lowColumn,
    SqlColumnRef highColumn,
    decimal value)
{
    var tolerance = Math.Max(value.GetPrescisionModifier(), Math.Abs(value) * 0.10m);
    return new Predicate.And(
        new Predicate.GreaterThanOrEqual(lowColumn, context.Parameter(value - tolerance)),
        new Predicate.LessThanOrEqual(highColumn, context.Parameter(value + tolerance)));
}
```

Map `SearchComparator.Ap` to this helper.

- [ ] **Step 4: Run number and quantity tests**

- [ ] **Step 5: Commit**

```powershell
git add src\Core\Ignixa.Search.Sql\Lowering\NumericRangeComparison.cs test\Ignixa.Search.Sql.Tests\Lowering
git commit -m "Implement numeric approximate comparison"
```

### Task 3: Implement date approximation with fixed time

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Lowering/ApproximateDateRange.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/DateTimeRangeComparison.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/DateTimeLoweringRuleTests.cs`

- [ ] **Step 1: Replace the date `:ap` throw with fixed-time tests**

Cover past and future instants plus `DateTimeSearchValue.Parse("2023-06")`. Use:

```text
midpoint = Start + (End - Start) / 2
toleranceTicks = abs(referenceTime.UtcTicks - midpoint.UtcTicks) / 10
approximateStart = Start - tolerance
approximateEnd = End + tolerance
```

Also assert a direct date `:ap` call with no reference time throws `InvalidOperationException` naming `Lower.Run`.

- [ ] **Step 2: Run tests and verify the existing throw**

- [ ] **Step 3: Implement the shared range calculation**

Create `ApproximateDateRange` with:

```csharp
internal static (DateTimeOffset Start, DateTimeOffset End) Widen(
    DateTimeSearchValue value,
    DateTimeOffset? referenceTime)
```

It validates the reference time, computes midpoint/tolerance once, and returns widened endpoints. Checked arithmetic must guard `DateTimeOffset.MinValue`/`MaxValue` overflow with an explicit `ArgumentOutOfRangeException`, not silent clamping. `DateTimeRangeComparison` reads `context.ApproximationReferenceTime`, calls this helper, and returns the same overlap shape as date equality using the widened endpoints.

- [ ] **Step 4: Run date tests**

- [ ] **Step 5: Commit**

```powershell
git add src\Core\Ignixa.Search.Sql\Lowering\DateTimeRangeComparison.cs test\Ignixa.Search.Sql.Tests\Lowering\DateTimeLoweringRuleTests.cs
git commit -m "Implement date approximate comparison"
```

### Task 4: Implement `_lastUpdated:ap`

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/ResourceColumnLoweringRule.cs:87-135`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/ResourceColumnLoweringRuleTests.cs`

- [ ] **Step 1: Add instant and partial-date tests**

For `:ap`, calculate widened `Start`/`End` exactly as Task 3, convert each boundary with existing `ToSurrogateId`, and require:

```csharp
new Predicate.And(
    new Predicate.GreaterThanOrEqual(column, context.Parameter(lowerSurrogateId)),
    new Predicate.LessThanOrEqual(column, context.Parameter(upperSurrogateId)))
```

Keep the existing partial-precision throw for non-`ap` comparators.

- [ ] **Step 2: Run tests and verify `_lastUpdated:ap` throws**

- [ ] **Step 3: Handle `Ap` before the exact-instant guard**

Call `ApproximateDateRange.Widen` from Task 3 rather than duplicating clock arithmetic. Only surrogate-ID conversion remains resource-specific.

- [ ] **Step 4: Run resource-column tests**

- [ ] **Step 5: Commit**

```powershell
git add src\Core\Ignixa.Search.Sql\Lowering test\Ignixa.Search.Sql.Tests\Lowering\ResourceColumnLoweringRuleTests.cs
git commit -m "Implement last-updated approximate comparison"
```

### Task 5: Prove every composite path

**Files:**
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/TokenNumberNumberLoweringRuleTests.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/TokenQuantityLoweringRuleTests.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/TokenDateTimeLoweringRuleTests.cs`

- [ ] **Step 1: Add failing composite `:ap` tests**

Add one approximate component to each rule. Token-number-number must test each numeric slot independently; token-quantity must retain Phase 1 system/code predicates; token-date must use a fixed reference time.

- [ ] **Step 2: Run the composite tests**

Expected: PASS if shared comparator dispatch is complete. If a rule rewrites comparators locally, make the minimal change to call `NumericRangeComparison.Build` or `DateTimeRangeComparison.Build`; do not duplicate tolerance formulas.

- [ ] **Step 3: Pin complete SQL and parameter order**

Add emitted-SQL assertions showing token constraints precede approximate lower/upper bounds.

- [ ] **Step 4: Run all composite tests**

- [ ] **Step 5: Commit**

```powershell
git add src\Core\Ignixa.Search.Sql\Lowering\Composite test\Ignixa.Search.Sql.Tests\Lowering
git commit -m "Cover approximate composite searches"
```

### Task 6: Prove the compiler boundary and update documentation

**Files:**
- Modify: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`
- Modify: `test/Ignixa.Search.Sql.Tests/Tracing/SearchTraceTests.cs`
- Modify: `src/Core/Ignixa.Search.Sql/README.md`

- [ ] **Step 1: Add end-to-end fixed-clock cases**

Compile number, qualified quantity, date, and `_lastUpdated` `:ap` searches. Pin complete plans, SQL, and parameter values. Compile the same date twice with the same `FixedTimeProvider` and require byte-identical SQL/parameters.

- [ ] **Step 2: Run the compiler suite**

```powershell
dotnet test test\Ignixa.Search.Sql.Tests\Ignixa.Search.Sql.Tests.csproj
```

- [ ] **Step 3: Update the README**

Move `:ap` into the comparator support row and remove its final gap bullet. Document the fixed 10 percent policy, numeric precision floor, explicit date reference time, and `_lastUpdated` behavior.

- [ ] **Step 4: Run final validation**

```powershell
dotnet build All.sln
dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"
```

Expected: zero warnings/errors and all non-E2E tests pass. The README's "What's not implemented yet" list is now empty.

- [ ] **Step 5: Commit**

```powershell
git add src\Core\Ignixa.Search.Sql\README.md test\Ignixa.Search.Sql.Tests
git commit -m "Document approximate search support"
```
