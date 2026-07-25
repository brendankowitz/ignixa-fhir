# Ignixa.Search.Sql unified foundation — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Merge two independently-developed extensions of the same FHIR-to-SQL compiler into one foundation, so Ignixa's data layer and the Microsoft FHIR Server both build on the same `Ignixa.Search.Sql`.

**Architecture:** This branch is PR #365 (branch B). Capabilities from `worktree-ignixa-datalayer-sqlserver` (branch A) are ported onto it, duplicates resolved per decisions already made. The plan fixes the shared seams first (`QueryPlan` tail, `LowerOptions`), decomposes `SqlBuilder.Run` before piling more features onto it, then ports capability, then gates on execution against a real database — the verification B has never had.

**Tech Stack:** C# / .NET 10 (`Ignixa.Search.Sql` multi-targets net9.0 and net10.0), xUnit + Shouldly, Microsoft.SqlServer.TransactSql.ScriptDom for grammar assertions, SQL Server 2025 for the execution gate.

**Design:** `docs/superpowers/specs/2026-07-25-search-sql-unified-foundation-design.md`
**Evidence base:** `docs/superpowers/specs/2026-07-25-pr365-reconciliation-analysis.md` — per-file comparison of both implementations. **Read it before Task 3.** It carries detail this plan does not repeat.

## Global Constraints

- **Environment:** unset `Platform`, `__DOTNET_PREFERRED_BITNESS`, `__DOTNET_ADD_32BIT` before any `dotnet` command (known CS8034 workaround in this repo).
- `dotnet build All.sln` must be **0 warnings, 0 errors**. Warnings are errors.
- **The signal for Tasks 1-9 is `Ignixa.Search.Sql.Tests` green on both net9.0 and net10.0.** Baseline at branch base `6605ec20`: **746 passed / 0 failed**, both TFMs.
- A bare `dotnet test All.sln` fails here for unrelated environmental reasons (uninitialized submodule content, missing conformance-suites directory, projects needing `TEST_SQL_CONNECTION_STRING`, E2E needing a live environment, a TFM-parallelism file-lock race). Read actual error text before assuming a failure is yours.
- **Branch A is a read-only source.** It lives at `C:\src\ignixa-fhir\.claude\worktrees\ignixa-datalayer-sqlserver`, branch `worktree-ignixa-datalayer-sqlserver` at `cee3e2a5`. Never commit to it. Read its implementations with `git --git-dir=../ignixa-datalayer-sqlserver/.git show worktree-ignixa-datalayer-sqlserver:<path>` or by reading files in that directory directly.
- **A's compiler diff is the port source:** `git -C ../ignixa-datalayer-sqlserver diff origin/main...worktree-ignixa-datalayer-sqlserver -- src/Core/Ignixa.Search.Sql/`
- Async parameters are named `cancellationToken`, never `ct`, for **new** parameters. Leave existing `ct` declarations alone.
- No inline comments except non-obvious invariants. One type per file. Test naming `GivenContext_WhenAction_ThenResult`, AAA with Shouldly, no `#region`.
- `StartsWith`/`EndsWith`/`Contains` on strings need an explicit `StringComparison` (CA1310 is enforced as an error). An `out` discard cannot appear inside an expression tree (CS8198) — use `Count(...).ShouldBe(0)` rather than `ShouldAllBe(x => !x.TryGet(out _))`.
- **Copyright headers:** B's new files carry Microsoft headers; existing `Ignixa.Search.Sql` files carry none. New files added by this plan carry **no** header, matching the project's existing convention. Do not add headers to files you touch, and do not strip them from B's existing files (that is a separate cleanup).
- **Corpus verdict drift is expected.** `test/Ignixa.Search.Sql.Tests/Corpus/` guards the *distribution* of compile verdicts. Tasks 3, 6 and 7 will move it legitimately. Update `DivergenceBaseline` when a task moves it, and record *why* in the commit message — never adjust it to make an unexplained failure disappear.

---

### Task 1: Fix the `QueryPlan` tail and extend `LowerOptions`

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Ast/QueryPlan.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/LowerOptions.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs` (the `Run` signature and its guard block)
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/LowerOptionsTests.cs` (create)

**Interfaces:**
- Produces: `LowerOptions` gains `SystemLevelSearch` (bool), `OffsetPage` (`OffsetSpec?`), `CountPhaseScoped` (bool). Every later task sets these by name.
- Produces: `QueryPlan`'s fixed tail order — `CountOnly, Visibility, Projection, SurrogateRange, SearchParameterHash, IncludesOnly, OffsetPage, CountPhaseScoped`. A's two new slots go **after** B's five.

**Why this is first.** The two branches appended to `QueryPlan`'s positional tail independently and collided: position 9 is `OffsetSpec?` on A and `ResourceVisibility?` on B; position 10 is `bool CountPhaseScoped` on A and `ProjectionSpec?` on B. Both are nullable-record-versus-record and record-versus-bool pairs, so a positional construction compiles and means something different. Fixing the order once, before any port, prevents every later task from building on a moving target.

The two `Lower.Run` signatures already share an identical positional prefix — `(expression, symbols, targetResourceType, includes, revIncludes, includeLimit, sort, sortPhase, page)`. Only the tail differs: B takes `LowerOptions? options = null`; A takes seven positional optionals. So this task is purely tail work.

- [ ] **Step 1: Add `OffsetSpec` to this branch.**

A defines it; B does not. It is **not** its own file — A declares it at the bottom of `src/Core/Ignixa.Search.Sql/Ast/SortSpec.cs` (line 79):

```csharp
public sealed record OffsetSpec(int Offset, int Limit);
```

This repo's convention is one type per file, and A's placement is a wart rather than a pattern to copy. Create `src/Core/Ignixa.Search.Sql/Ast/OffsetSpec.cs` containing just that record, with an XML doc:

```csharp
namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// An OFFSET/FETCH page: skip <paramref name="Offset"/> rows, return at most <paramref name="Limit"/>.
/// Mutually exclusive with keyset <see cref="PageSpec"/> and with a TOP cap — T-SQL rejects the
/// combination (error 10741).
/// </summary>
public sealed record OffsetSpec(int Offset, int Limit);
```

- [ ] **Step 2: Write the failing test**

```csharp
[Fact]
public void GivenLowerOptions_WhenSettingAsAddedInputs_ThenEachIsReadableByName()
{
    // Arrange & Act
    var options = new LowerOptions
    {
        SystemLevelSearch = true,
        OffsetPage = new OffsetSpec(20, 10),
        CountPhaseScoped = true,
    };

    // Assert
    options.SystemLevelSearch.ShouldBeTrue();
    options.OffsetPage!.Offset.ShouldBe(20);
    options.CountPhaseScoped.ShouldBeTrue();
}

[Fact]
public void GivenAQueryPlan_WhenConstructedWithNamedTailArguments_ThenEachSlotHoldsItsOwnValue()
{
    // Arrange
    var ctes = new List<CteDefinition>();
    var match = new CteRef("m");

    // Act -- named arguments are mandatory for the tail; this test exists to pin the order
    var plan = new QueryPlan(
        ctes,
        match,
        CountOnly: true,
        OffsetPage: new OffsetSpec(5, 10),
        CountPhaseScoped: true);

    // Assert
    plan.CountOnly.ShouldBeTrue();
    plan.OffsetPage!.Offset.ShouldBe(5);
    plan.CountPhaseScoped.ShouldBeTrue();
    plan.Visibility.ShouldBeNull();
    plan.Projection.ShouldBeNull();
}
```

Confirm `OffsetSpec`'s real constructor shape when you copy it in Step 1 and adjust these literals to match — do not guess at its parameter names.

- [ ] **Step 3: Run to verify it fails**

```
dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~LowerOptionsTests"
```
Expected: compile failure — `SystemLevelSearch`, `OffsetPage`, `CountPhaseScoped` do not exist.

- [ ] **Step 4: Add the three properties to `LowerOptions`**

Append to the record, each with an XML doc in the file's existing style:

```csharp
    /// <summary>
    /// Allows typed leaf predicates to lower without a single target resource type, for system-level
    /// search. Deliberately explicit rather than inferred from a null target type: a null type already
    /// means "wildcard compartment", and <see cref="ResourceTypes"/> is orthogonal — that shapes the
    /// base set, this gates cross-type lowering of the leaves themselves. Both together is legal and is
    /// exactly the <c>GET /?_type=A,B&amp;name=foo</c> case.
    /// </summary>
    public bool SystemLevelSearch { get; init; }

    /// <summary>An OFFSET/FETCH page; mutually exclusive with keyset <c>page</c> and <c>Top</c>.</summary>
    public OffsetSpec? OffsetPage { get; init; }

    /// <summary>
    /// Scopes a <see cref="CountOnly"/> count to the current sort phase's own join output rather than the
    /// whole match set. The compiler-side half of two-phase sort execution.
    /// </summary>
    public bool CountPhaseScoped { get; init; }
```

- [ ] **Step 5: Extend `QueryPlan`'s tail**

Append after `IncludesOnly`, keeping B's existing five in place:

```csharp
    OffsetSpec? OffsetPage = null,
    bool CountPhaseScoped = false)
```

- [ ] **Step 6: Add the cross-field guards to `Lower.Run`**

These stay in `Run`, not on the record, because each crosses into a parameter that is still positional. Add at the top of `Run`, after `options ??= new LowerOptions();`:

```csharp
        if (options.OffsetPage is not null && (page is not null || options.Top is not null))
        {
            throw new NotSupportedException(
                "OffsetPage cannot combine with keyset paging or Top: OFFSET/FETCH and TOP are mutually exclusive in T-SQL (error 10741).");
        }

        if (options.CountPhaseScoped && (!options.CountOnly || sort.Count == 0))
        {
            throw new NotSupportedException(
                "CountPhaseScoped requires CountOnly with at least one sort key: there is no sort phase to scope the count to otherwise.");
        }
```

Read A's `Lower.Run` guard block first (`../ignixa-datalayer-sqlserver/src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`) and carry across the exact message wording where A already has an equivalent guard.

- [ ] **Step 7: Run to verify it passes**

```
dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~LowerOptionsTests"
```
Expected: PASS.

- [ ] **Step 8: Add guard tests**

```csharp
[Fact]
public void GivenOffsetPageAndKeysetPage_WhenLowering_ThenThrowsNotSupported()
{
    // Arrange, Act, Assert -- see LowerTests for the established Run(...) call shape
}

[Fact]
public void GivenCountPhaseScopedWithoutCountOnly_WhenLowering_ThenThrowsNotSupported()
{
}
```

Fill both in using the `Lower.Run(...)` invocation shape already used in `test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs`; assert with `Should.Throw<NotSupportedException>`.

- [ ] **Step 9: Full suite + build**

```
dotnet build All.sln
dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj
```
Expected: 0/0 build; 746 + your new tests passing, both TFMs.

- [ ] **Step 10: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Ast/OffsetSpec.cs src/Core/Ignixa.Search.Sql/Ast/QueryPlan.cs src/Core/Ignixa.Search.Sql/Lowering/LowerOptions.cs src/Core/Ignixa.Search.Sql/Lowering/Lower.cs test/Ignixa.Search.Sql.Tests/Lowering/LowerOptionsTests.cs
git commit -m "feat(search-sql): fix the QueryPlan tail order and extend LowerOptions

Both branches appended to QueryPlan's positional tail independently and
collided -- position 9 was OffsetSpec? on one and ResourceVisibility? on the
other. Fixing the order once, before any capability port, stops every later
task building on a moving target."
```

---

### Task 2: Port `CompileFromOptionsAsync` and forward `AccessConstraints`

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Tracing/SearchCompiler.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Tracing/CompileFromOptionsTests.cs` (create)

**Interfaces:**
- Consumes: `LowerOptions.SystemLevelSearch` / `OffsetPage` / `CountPhaseScoped` (Task 1).
- Produces: `SearchCompiler.CompileFromOptionsAsync(...)` — the entry point taking a pre-built `SearchOptions`. Later tasks and branch A's adapter both call it.
- Produces: `SearchTrace.CompiledPlan` (the real `QueryPlan`, not the display-only `QueryPlanTrace`), `EmittedSqlTrace.Parameters`, and `SearchTrace.ResourceType` widened to `string?`.

**The security obligation in this task.** A's `CompileFromOptionsAsync` predates `AccessConstraints` and does not forward it. Ported as-is, a caller setting `AccessConstraints` on a `SearchOptions` gets **silent non-enforcement** — the same fail-open defect B's own review caught when it found `SearchOptions.AccessConstraints` "connected to nothing". The forwarding and its test are not optional extras; they are the point of doing this task before the capability ports.

- [ ] **Step 1: Read A's implementation**

```bash
cat ../ignixa-datalayer-sqlserver/src/Core/Ignixa.Search.Sql/Tracing/SearchCompiler.cs
```

Note what it passes to `Lower.Run` positionally — those become `LowerOptions` properties here.

- [ ] **Step 2: Write the failing enforcement test**

This is the non-vacuous test. B's own access-constraint tests were vacuous until stubbing the guard to `1 = 1` was shown to fail something; this test must fail if the forwarding is removed.

`AccessConstraint` is `public sealed record AccessConstraint(string ResourceType, Expression Predicate)` (`src/Core/Ignixa.Search/Models/AccessConstraint.cs:23`). `AccessConstraintTests.cs` builds them as `new AccessConstraint("Observation", TokenPredicate(statusParam, "final"))` using its own local `TokenPredicate` helper — reuse that construction shape.

```csharp
[Fact]
public async Task GivenSearchOptionsCarryingAccessConstraints_WhenCompilingFromOptions_ThenTheEmittedSqlEnforcesThem()
{
    // Arrange -- a status=final constraint on Observation, mirroring AccessConstraintTests' fixture
    var options = new SearchOptions { ResourceType = "Observation" };
    options.AccessConstraints = [new AccessConstraint("Observation", TokenPredicate(statusParam, "final"))];

    // Act
    var trace = await compiler.CompileFromOptionsAsync(options, CancellationToken.None);

    // Assert -- the constraint must reach the emitted SQL, not merely be accepted by the API
    trace.Sql!.Sql.ShouldContain("TokenSearchParam", Case.Sensitive);
    trace.CompiledPlan!.Ctes.Count.ShouldBeGreaterThan(1);
}
```

Copy `TokenPredicate` and the symbol-table fixture setup from `test/Ignixa.Search.Sql.Tests/Lowering/AccessConstraintTests.cs` rather than reinventing them. Before settling on the assertion, run the test with the constraint present and absent and pick a fragment that genuinely differs between the two — Step 6 verifies you chose one that discriminates.

- [ ] **Step 3: Run to verify it fails**

Expected: compile failure (no `CompileFromOptionsAsync`), or assertion failure once the method exists without forwarding.

- [ ] **Step 4: Port the method, forwarding `AccessConstraints`**

Port A's implementation, mapping its positional `Lower.Run` arguments onto `LowerOptions`, and add:

```csharp
            AccessConstraints = options.AccessConstraints,
```

- [ ] **Step 5: Port the trace additions**

`SearchTrace.CompiledPlan`, `EmittedSqlTrace.Parameters`, and widen `SearchTrace.ResourceType` to `string?`. A's versions are in the same file; `CompiledPlan` matters because `QueryPlanTrace` is display-only and can diverge from `options.Include` when a degenerate stage is dropped — an executing caller needs the real plan to choose its row shape.

- [ ] **Step 6: Run to verify it passes, then prove the test is non-vacuous**

Temporarily delete the `AccessConstraints = options.AccessConstraints,` line, re-run, and confirm the test **fails**. Restore it. Report both outcomes — a test that passes either way proves nothing.

- [ ] **Step 7: Full suite + build, then commit**

```bash
git add -A src/Core/Ignixa.Search.Sql/Tracing test/Ignixa.Search.Sql.Tests/Tracing
git commit -m "feat(search-sql): port CompileFromOptionsAsync with AccessConstraints forwarding

The ported entry point predates AccessConstraints. Forwarded here rather than
later: without it a caller setting constraints gets silent non-enforcement --
the same fail-open defect this branch's own review caught elsewhere."
```

---

### Task 3: Nullable-type leaf threading and `_type` extraction

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/LeafContext.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Leaf/*.cs` (every leaf rule taking a target type)
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Composite/*.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/ResourceColumnLoweringRule.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs`

**Interfaces:**
- Consumes: `LowerOptions.SystemLevelSearch` (Task 1).
- Produces: leaf and composite rules that lower without a concrete target resource type when `SystemLevelSearch` is set.

**Read the reconciliation analysis §1.1 before starting.** This is the synthesis decision: A's nullable-`ResourceTypeId` threading is the only implementation that can express `GET /?_type=A,B&name=foo` — B's typed-leaf path throws when the target type is null — but B's `MultiTypeResourceSource` is the better node for the no-expression base set.

- [ ] **Step 1: Read both implementations side by side**

```bash
git -C ../ignixa-datalayer-sqlserver diff origin/main...worktree-ignixa-datalayer-sqlserver -- src/Core/Ignixa.Search.Sql/Lowering/
git diff origin/main...HEAD -- src/Core/Ignixa.Search.Sql/Lowering/
```

A's version of this survived a rebase incident that dead-coded it; read its **current** state, not its history.

- [ ] **Step 2: Write the failing test — the case only A's mechanism handles**

```csharp
[Fact]
public void GivenMultipleTypesAndALeafPredicate_WhenLoweringSystemLevel_ThenBothTypesNarrowAndTheLeafApplies()
{
    // Arrange -- GET /?_type=Patient,Observation&name=foo
    // Act -- Lower.Run(..., options: new LowerOptions { SystemLevelSearch = true, ResourceTypes = ["Patient", "Observation"] })
    // Assert -- the plan lowers without throwing, the leaf CTE carries no single-type filter,
    //           and the base set narrows to the two types
}
```

Fill in using `LowerTests.cs`'s established call shape.

- [ ] **Step 3: Run to verify it fails** — expected: `NotSupportedException` from the typed-leaf path.

- [ ] **Step 4: Port A's nullable threading**

Thread the nullable target type through `LeafContext` and every leaf/composite rule, gated on `SystemLevelSearch`. Keep A's choke-point guards.

- [ ] **Step 5: Route the no-expression base set through B's `MultiTypeResourceSource`**

Where A used `ResourceSource(null)`, use B's `MultiTypeResourceSource.AllTypes()`; where types are named, `ForTypes(...)`. B's `AllTypes()` emits byte-identical SQL to A's `ResourceSource(null)`, so existing text assertions should not move for the all-types case — if they do, stop and investigate rather than re-baselining.

- [ ] **Step 6: Port A's `_type` Or-of-equalities extraction** into `ResourceColumnLoweringRule`, preserving B's `TryLowerResourceColumn` negation path for `_id:not`/`_type:not`. **Both must remain reachable** — a previous rebase orphaned one of them as dead code and it took four failing tests to notice.

- [ ] **Step 7: Run the full compiler suite**

Corpus verdict drift is expected here. Update `DivergenceBaseline` and state the cause in the commit message.

- [ ] **Step 8: Build + commit**

---

### Task 4: Sort expansion

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Ast/SortSpec.cs` (or wherever `SortKeyKind` lives — confirm)
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs` (`BuildSortSpec`)
- Modify: `src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs` (`EmitSortJoins`, `EmitOrderBy`)
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs`, `test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs`

**Interfaces:**
- Produces: `SortKeyKind.ResourceId` and `SortKeyKind.Aggregated`, `SentinelFor`, catalog-driven `SortKey.Table`/`Column`.

Removes the String/Date/`_lastUpdated`-only sort restriction. The FHIR Server supports Token/Number/Quantity/Reference/Uri sorts today through its own codegen, so this is compiler-completeness both consumers need.

**Carry A's Msg-145 fix.** A's `EmitOrderBy` deduplicates the case where a `LastUpdated` sort key duplicates the `m.Sid1` tiebreak column, which SQL Server rejects with error 145. This was found by *executing* the SQL; B structurally could not have found it and does not have it.

**Also travels in this task: `KeysetContinuationToken`.** A defines it; B does not. It encodes the compiler's own `PageSpec` boundary shape, so it belongs beside the paging/sort machinery rather than in an adapter. Its doc already disclaims compatibility with Ignixa's legacy offset token — that layering is correct and should be preserved verbatim.

- [ ] **Step 1: Read A's sort implementation** — `BuildSortSpec`, `SortKeyKind`, `SentinelFor`, `EmitSortJoins`, `EmitOrderBy`, and `KeysetContinuationToken`. Note `OffsetSpec` lives in `Ast/SortSpec.cs` on A; check whether `SortKeyKind` and `KeysetContinuationToken` are similarly co-located before assuming file paths.
- [ ] **Step 2: Write failing tests** — one per new `SortKeyKind`, plus one asserting a `_sort=_lastUpdated`-only plan emits **no duplicate ORDER BY column**.
- [ ] **Step 3: Run to verify they fail.**
- [ ] **Step 4: Port the sort expansion**, including `BuildSortSpec`'s `MissingPrimary` rejection for `ResourceId` (that phase is structurally impossible for resource-column keys).
- [ ] **Step 5: Port the Msg-145 dedup.**
- [ ] **Step 6: Port `KeysetContinuationToken`** with a round-trip test (encode a `PageSpec` boundary, decode it, assert equality).
- [ ] **Step 7: Run, build, commit.**

---

### Task 5: Decompose `SqlBuilder.Run` into per-shape emitters

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs`
- Test: existing `test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs`, `EmitSqlGrammarTests.cs`

**Interfaces:**
- Produces: per-terminal-shape emitters — CountOnly, no-includes, includes — replacing the single `Run` body.

**This task is deliberately placed before Tasks 6 and 7 rather than after.** B's own comment warns that a sixth optional feature should prompt decomposition; the merged method already carries roughly nine. B's `IncludesOnly` + `_sort` bug — `ORDER BY` bound to a nonexistent column, grammatically valid, failing at execution with error 207 — was caused by exactly this shape: three sites having to agree on a column contract. Decomposing now means Tasks 6 and 7 add features to a structure that can hold them, instead of colliding in the monolith and being untangled afterwards.

**This is a pure refactor. No behaviour change, no test assertion should move.** If an assertion moves, you have changed behaviour — stop and investigate.

- [ ] **Step 1: Run the suite and record the exact pass count** — this is your invariant.
- [ ] **Step 2: Extract the CountOnly terminal shape** into its own method. Run the suite; count must be unchanged.
- [ ] **Step 3: Extract the no-includes shape.** Run; unchanged.
- [ ] **Step 4: Extract the includes shape.** Run; unchanged.
- [ ] **Step 5: Verify `Run` is now dispatch-only** — shape selection plus delegation.
- [ ] **Step 6: Build + commit.**

---

### Task 6: `OffsetPage` and `CountPhaseScoped` emission

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs`

**Interfaces:**
- Consumes: `LowerOptions.OffsetPage` / `CountPhaseScoped` (Task 1), the decomposed emitters (Task 5).

Merge A's OFFSET/FETCH emission onto B's `NeedsResourceJoin`/clause-list skeleton (analysis §1.6: B's skeleton, A's clauses — this refactor was done twice).

- [ ] **Step 1: Read A's OFFSET/FETCH emission and its `countPhaseScoped` clause.**
- [ ] **Step 2: Write failing tests** — an offset-paged plan emits `OFFSET n ROWS FETCH NEXT m ROWS ONLY`; a `countPhaseScoped` count emits the phase's own join rather than the whole match set.
- [ ] **Step 3: Run to verify they fail.**
- [ ] **Step 4: Implement onto B's clause-list skeleton.**
- [ ] **Step 5: Run, build, commit.**

---

### Task 7: Replace `$everything` with A's traversal

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/EverythingLoweringRule.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Ast/CteDefinition.cs`, `PlanRowKind.cs`, `PlanExplainer.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Symbols/SymbolCollectingVisitor.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/EverythingLoweringRuleTests.cs`

The largest duplicate, and one neither branch's authors had flagged. A's traversal is a strict superset — referenced-type expansion, conditional clinical-date filter, `_since` — and is E2E-proven; B's self-declares "semantically incomplete". Keep A's, wrapped in B's wiring.

**Two things that are not straight ports:**

**Thread `ResourceVisibility` through A's three new emitters.** They were written when visibility was hardcoded: `EmitReferencedTypeExpansion` hardcodes `r.IsHistory = 0 AND r.IsDeleted = 0`; `EmitVisibleSinceFilter` and `EmitTableExistsPredicate` emit no visibility filter at all. Use B's `ResourceRowFilter` helper, as B did for `ChainJoin`/`IncludeStage`/`NotReferencedSource`. `$everything` never runs with relaxed visibility in practice, but leaving one CTE kind outside the contract reinstates the "hardcoded at six emitter sites" defect B just removed.

**Record the `_since` divergence in code.** A's `_since` filters on `Transactions.VisibleDate`; B's on a `lastUpdated` surrogate floor. **These return different rows.** A's matches the legacy engine and is kept — but a future reader will otherwise assume they were equivalent, so the choice belongs in a comment at the filter.

- [ ] **Step 1: Read both implementations and analysis §1.3.**
- [ ] **Step 2: Write failing tests** covering referenced-type expansion, the clinical-date filter, and `_since`.
- [ ] **Step 3: Run to verify they fail** against B's incomplete version.
- [ ] **Step 4: Port A's `LowerPatientEverything` and its three CTE kinds** (`TableExistsPredicate`, `VisibleSinceFilter`, `ReferencedTypeExpansion`) with their emitters and explainer rows.
- [ ] **Step 5: Thread `ResourceVisibility` through all three emitters.**
- [ ] **Step 6: Keep B's `ApplyToTypes` constraint wiring and its empty-compartment `Predicate.False` degrade.**
- [ ] **Step 7: Port A's `SymbolCollectingVisitor.VisitPatientEverything`** (superset of B's).
- [ ] **Step 8: Add the `_since` divergence comment.**
- [ ] **Step 9: Run** — corpus drift expected and legitimate; update the baseline and explain it.
- [ ] **Step 10: Build + commit.**

---

### Task 8: Swap surrogate-id range to B's shape

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs`

The one decision that goes to the unexecuted implementation. B's `SurrogateIdRange` plan input emits against `m.Sid1` with no forced `dbo.Resource` join, states the match-arm-only contract explicitly, and is visible in the plan. A's `(long, long)` tuple and its outer-predicate splice are deleted.

**This decision is gated on Task 9's `$export` partition tests**, which are the validation B could not perform. If they fail against B's shape, that is a real finding — report it rather than reverting silently.

- [ ] **Step 1: Confirm A's outer-predicate splice has no remaining callers** on this branch (it arrives only if a prior task ported it — check).
- [ ] **Step 2: Write a failing test** asserting a surrogate-bounded plan emits the range against `m.Sid1` with no `dbo.Resource` join added for the bound alone.
- [ ] **Step 3: Run, implement, run.**
- [ ] **Step 4: Build + commit.**

---

### Task 9: Execution gate

**Files:** none in this branch — this task changes nothing and proves everything.

This is the step B has never been able to run, and the reason this reconciliation exists rather than merging both branches and fixing the collision afterwards.

- [ ] **Step 1: Point branch A's data layer at this branch.**

In A's worktree, retarget `Ignixa.DataLayer.SqlServer`'s reference from A's own `Ignixa.Search.Sql` to this branch's. Do not commit that change to A — it is a measurement harness.

- [ ] **Step 2: Build A against the unified compiler.** Expect compile breaks where A's adapter used the old shapes (`surrogateIdRange` tuple, positional `Lower.Run` optionals). Fix them in A's working tree; record each as a delta A must re-apply when it rebases.

- [ ] **Step 3: Run A's integration suite.**

Environment: unset `Platform`/`__DOTNET_PREFERRED_BITNESS`/`__DOTNET_ADD_32BIT`; `TEST_SQL_CONNECTION_STRING` pointing at a **brand-new database name** (a stale database with schema drift silently produces ~590 bogus failures); `SqlServer__AutomaticSchemaDeploymentEnabled=true`; local SQL Server 2025, not Docker (the Docker 2022 image cannot deploy a `Sql170` DACPAC).

```
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj
```
Expected: 126 passing.

- [ ] **Step 4: Run A's E2E suite.**

```
dotnet test test/Ignixa.Api.E2ETests/Ignixa.Api.E2ETests.csproj
```
Expected: 588 of 620 passing — **with every delta from that baseline explained**.

- [ ] **Step 5: Check the two specific gates.**

- **`$export` partition tests** gate Task 8's decision. They must pass against B's surrogate-range shape.
- **Untyped-reference searches** gate B's declared-target narrowing at row level. B changed which rows `/Patient?organization=X` returns — narrowing away cross-type id collisions like `Practitioner/X`. Text assertions cannot confirm that is right; these can. A's E2E evidently never exercised the collision case, so **new coverage here is a legitimate outcome of this task**.

- [ ] **Step 6: Write the results to `docs/superpowers/specs/2026-07-25-unified-execution-gate-results.md`** — counts, every delta and its explanation, and the two gate outcomes. Commit that file to this branch. It is the evidence that this foundation has been executed, which is the whole argument for the branch.

- [ ] **Step 7: Revert A's harness change**, leaving A's worktree clean.

---

## Notes for whoever executes this

- **Branch A's compiler commits get dropped when it rebases onto this branch.** Do not try to preserve them. A re-applies only its adapter deltas: `SurrogateIdRange` construction, `AccessConstraints` pass-through, and the csproj catalog-source switch to the decomposed DDL.
- **The `97.sql` catalog source stays.** A's switch to decomposed DDL depends on A's Database project, which exists on neither main nor this branch. Every table and column the ported features read already exists in `97.sql`.
- **The gap-closure plan written against branch A is now partly stale.** Several of its 30 targeted E2E failures live in files this plan rewrites, and some may already be fixed here. Re-measure against this foundation before planning that work.
