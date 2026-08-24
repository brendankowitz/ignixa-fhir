# Investigation: PR #427 Known Residuals

**Feature**: fhirpath
**Status**: Superseded
**Created**: 2026-08-21

> Superseded on 2026-08-23. This was an implementation plan, and the work it planned has shipped —
> #423, #425, #426, #428 and #429 all landed, and #424 remains deferred on the trigger recorded in WI-5.
> Read it as a record of what was planned, not as a description of the code.
>
> Four statements in it are now false, which is why it is marked rather than left to rot:
>
> - The "adjacent finding" under WI-4 says `repeat()` "has **no iteration guard at all**". It has one
>   since #433 — a 10,000-iteration cap, and it throws `FhirPathEvaluationException` exactly as that
>   section asked the eventual fix to.
> - WI-1 says shipped usage of `repeat()` is "two invariant-style expressions ... all self-similar".
>   Re-measured: **three**, in R5 and R6 only — two on PlanDefinition, one on QuestionnaireResponse — and
>   they are not all self-similar. `CollectionFunctions.Repeat`'s remarks carry the current figure.
> - WI-4's falsification recipe says to revert the guard "at `CollectionFunctions.cs:520`". That line is
>   now `Repeat`'s missing-argument throw, so following the recipe mutates the wrong function;
>   `RepeatAll`'s guard is the one that recipe means.
> - Every line number in the file predates the changes above and has moved.
>
> Current behaviour lives in the XML remarks on `CollectionFunctions.Repeat` and
> `CollectionFunctions.RepeatAll`, which are maintained alongside the code. Prefer those.

Implementation plan: PR #427 known residuals (#423, #424, #425, #426, #428, #429)

All code references are to the PR #427 worktree at `C:/w427` (detached at `92c99541`); merge base for
base-commit measurements is `c66cc4a6` (origin/main tip).

## Summary

| Issue | Verdict | Work item | Effort | PR |
|---|---|---|---|---|
| #423 | Real analyzer fix + sweep measurement | WI-1 | M (2–4 days, sweep dominates) | PR 1 (alone) |
| #425 | Guard hardening; largely already landed at HEAD — residual scope is narrow | WI-2 (merged with #429) | S (0.5–1 day) | PR 2 |
| #429 | Same defect, same file as #425 — **one work item, not two** | WI-2 | — | PR 2 |
| #426 | Reflection census, behavioral cross-check | WI-3 | S (0.5–1 day) | PR 2 |
| #428 | **Affirm the demotion** and pin it | WI-4 | S (0.5 day) + one new issue to file | PR 3 |
| #424 | **Stays deferred.** No work now; trigger recorded | WI-5 | 0 | none |

---

## WI-1 — #423: `repeat()` type inference false always-empty

### Root cause, confirmed in code

The issue's diagnosis ("`repeat()`'s FHIR type inference, not namespace provenance") is correct, and the
mechanism is now fully located:

- `repeat` and `repeatAll` declare `ReturnType = "context"` —
  `src/Core/Ignixa.FhirPath/Evaluation/Functions/CollectionFunctions.cs:434` and `:499`.
- The source generator wires `"context"` to `ReturnsContext`
  (`src/Core/Ignixa.FhirPath.Generators/FhirPathFunctionGenerator.cs:281-284`), which returns
  `focus.Types` verbatim (`src/Core/Ignixa.FhirPath/Visitors/SymbolTable.cs:137-144`).
- But the evaluator returns **projection results, not the focus**
  (`src/Core/Ignixa.FhirPath/Evaluation/Functions/CollectionFunctions.cs:442-485`, and the spec note at `:430`: "Returns only the results of the
  projection, not the original focus items").
- So for `(name.repeat(family)).ofType(string)` the analyzer types the repeat result as `HumanName`,
  `HandleTypeFilterFunction` finds no match (`src/Core/Ignixa.FhirPath/Analysis/FhirPathAnalyzer.cs:797`,
  always-empty warnings at `:867` and `:879`), provenance is `None` (see below), and a false
  `AlwaysEmpty` is issued. That is the entire 28-row population.

Contrast: `select()` declares `"fromArgument"` → `ReturnsFromArgument` (argument's inferred type), and
`children()`/`descendants()` declare `"any"` → `ReturnsUnknown` (fail-open) —
`src/Core/Ignixa.FhirPath/Evaluation/Functions/TreeNavigationFunctions.cs:23` and `:45`.
`repeat` is semantically `descendants`-shaped recursion over a `select`-shaped projection, and it is the
only one of the three declared as if it were a pass-through.

### Fix — two options, minimal first

**Option A (recommended default): declare `repeat`/`repeatAll` as `ReturnType = "any"`.** One word per
function, matching the existing `descendants()` precedent. Downstream of a `repeat()` the type becomes
Unknown, which suppresses always-empty claims (`src/Core/Ignixa.FhirPath/Analysis/FhirPathAnalyzer.cs:866` and `:876` both check
`focusTypes.HasUnknown` before warning). That kills all 28 false rows and cannot introduce new ones —
Unknown fails open in both `ofType`/`as` and cast provenance. The cost is losing true always-empty
diagnostics downstream of `repeat()`; shipped-corpus usage of `repeat()` is nearly nil (two invariant-
style expressions in the R5/R6 generated schema, `repeat(item|answer)` shapes, all self-similar), so the
lost precision is close to zero in practice. The sweep decides whether that claim holds.

**Option B (only if the sweep shows Option A loses real diagnostics): a bounded fixpoint.** `repeat`'s
true static type is the closure of the projection's type under re-application. The `GetReturnType`
delegate signature (`def, focus, args, issues`) receives pre-computed argument types and cannot re-type
the projection against new foci, so a fixpoint cannot live in the delegate — it needs a special case in
`VisitFunctionCall` alongside the existing `ofType`/`as`/`is` special cases
(`src/Core/Ignixa.FhirPath/Analysis/FhirPathAnalyzer.cs:448-458`): re-type the projection with the accumulated type set as `$this` until
stable or N rounds (N≈10), adding Unknown on non-convergence. Do not build this speculatively; it is the
gold-plated version of a fix whose measured benefit may be zero.

### Interaction with provenance (this is the coupling the plan must get right)

`SystemTypeConstructionAnalyzer.AnalyzeFunction` dispatches on the **same** `DeclaredReturnType` string
(`src/Core/Ignixa.FhirPath/Analysis/SystemTypeConstructionAnalyzer.cs:190-231`). Today
`"context"` makes repeat's provenance `Analyze(focus)` (`:199-202`). Changing the attribute string
therefore changes the provenance surface as a side effect:

- With `"any"`, provenance becomes `SystemTypeConstruction.Any` (`:219-222`) — fail-open, sound.
- Incidentally, today's `"context"` provenance for `repeat` is **wrong in principle**, not just the FHIR
  type: `(1 'mg').repeat(value)` returns System.Decimal members, but provenance names `Quantity` from
  the focus. Option A closes that latent hole for free. Add one case to
  `AnalyzerCastProvenanceRegressionTests` pinning it.
- If Option B is chosen and a new keyword (e.g. `"projection"`) is introduced instead: an unrecognized
  keyword falls through `GetSystemPrimitiveRuntimeTypeName` (returns null) to
  `IsKnownFhirType("projection") → false → Any` (`:224-230`) — fail-open by accident. Make it fail open
  **on purpose**: add an explicit arm, and note it in the #426 census (WI-3) so the keyword set stays
  enumerated.

Also confirmed: the body-scanning guard `ValueConstructionReturnTypeTests`
(`test/Ignixa.FhirPath.Tests/Evaluation/ValueConstructionReturnTypeTests.cs`) does **not** catch
this defect — repeat's results arrive through the `evaluateExpression` callback, not from construction
reachable in its own body. Do not expect that census to defend this fix; the sweep and the new
regression tests are the defense.

### Falsification step

1. New regression tests (in `test/Ignixa.FhirPath.Tests/Analysis/`, following the
   evaluate-then-analyze-with-floors template at
   `test/Ignixa.FhirPath.Tests/Analysis/AnalyzerSystemValueCastAlignmentTests.cs:145-157`):
   `(name.repeat(family)).ofType(string)`, `.as(string)`, `.ofType(FHIR.string)` across R4–R6 assert
   evaluator returns items (`ShouldNotBeEmpty` floor), `HasAlwaysEmptySubexpression == false`, and —
   separately — `IsValid == true`.
   **Demonstrate red:** with the tests in place, revert only the production change (restore
   `ReturnType = "context"` on `repeat` at `src/Core/Ignixa.FhirPath/Evaluation/Functions/CollectionFunctions.cs:434`), run
   `dotnet test --filter "Repeat"` on `Ignixa.FhirPath.Tests`, assert the new tests fail, restore the fix.
2. `(1 'mg').repeat(value) is System.Decimal`-shaped provenance case in
   `AnalyzerCastProvenanceRegressionTests`; same revert demonstrates red.

### Measurement bar (issue-specified, non-negotiable)

Reconstruct the 36,180-row analyzer/evaluator sweep as a **scratch harness** (it is not checked in — I
searched; nothing in `tools/` or the test tree carries it). Uniform expression-shape sweep per version:

- Run at base `c66cc4a6` and at HEAD+fix. Ordinally join on (version, expression text).
- Columns kept **strictly separate** per row: `HasAlwaysEmptySubexpression`, `IsValid`,
  evaluator item count. A false-always-empty row is `alwaysEmpty=true ∧ evaluator count>0` — never
  derived from `IsValid`.
- **Pass bar: zero transitions into false-always-empty**, and the 28 known rows transition out.
- Secondary column: cast-provenance verdict at each `ofType`/`as` site, to confirm the keyword change
  produced no provenance regressions.
- Attach the joined summary (counts per transition class) to the PR description. Reading the diff does
  not close this item — four consecutive commits in this area self-certified clean and were not.

Effort: fix itself is under a day (Option A); sweep reconstruction + two runs + join is 1–2 days;
tests 0.5 day.

---

## WI-2 — #425 + #429: enumeration guards and the hollow `%resource` theory

**These are one work item.** #429 item 1 is explicitly "tracked in #425", they are the same defect
(a pinned row set nothing enumerates) in the same file, and the fix pattern for both was already
established in that file by commit `84cec49f`.

### What #425 asked for that is already done at HEAD — say so, don't rebuild it

- The sweep-driven divergence sets are fully guarded in both directions:
  `AssertPinned` fails on unpinned, moved, **and vanished** signatures
  (`test/Ignixa.FhirPath.Tests/Evaluation/Parity/FirelyVersusIgnixaDifferentialTests.cs:305-344`).
- The resource-backed corpus pins cardinality + membership both ways (`AssertCounts` with `ShouldBe`)
  plus floors on evaluations, resources, both-threw, both-empty, and value agreements
  (`test/Ignixa.FhirPath.Tests/Evaluation/Parity/ResourceBackedParityCorpusTests.cs:75-180`,
  constants in `ResourceBackedKnownDivergences.cs`). The doc-anchored counts the issue names
  (R4/R4B `Observation-code-value-date`, R5 instant carrier) are executable there and in
  `AsOperatorSearchParameterCardinalityTests`.
- `NormalisedTypeNames` has its independent-literal inventory fact (`84cec49f`,
  `FirelyVersusIgnixaDifferentialTests.cs:150-176`).

### Residual scope — the actual work

1. **`GivenAResourceVariable_WhenResolvedByBothEngines_ThenTheyAgree`**
   (`test/Ignixa.FhirPath.Tests/Evaluation/Parity/FirelyVersusIgnixaDifferentialTests.cs:236-252`, 5 `[InlineData]` rows):
   - Add the one-line floor exactly as sketched in #429 — the sketch is right, use it verbatim:
     `firely.Describe()`/`ignixa` each get a non-empty floor before the comparison, message per the
     `test/Ignixa.FhirPath.Tests/Analysis/AnalyzerSystemValueCastAlignmentTests:151` pattern. (Check what `Evaluate(...)` returns —
     if `Describe()` renders an empty marker, floor on the underlying collection, not the string.)
   - Convert `[InlineData]` to `[MemberData]` + a companion inventory fact with an independent literal
     array, copying the `NormalisedTypeNames` pattern in the same file.
2. **`GivenADateOfSomePrecision_WhenTakingItsHighBoundary_...`** (`:283-303`, 7 rows): enumeration
   guard only — the issue is right that it has no hollowness problem (it asserts concrete literals,
   and only against Ignixa; note in the inventory fact's remarks that this theory is a unilateral pin,
   not a parity check).
3. **Same-defect neighbors, same PR, cheap:** the two 2-row theories in
   `test/Ignixa.FhirPath.Phase3.Stu3.Tests/Stu3NativeFirelyDateAliasTests.cs:35-67` get the
   same MemberData+inventory treatment. Optional, do-if-trivial: an STU3 corpus census asserting the
   set of 11 shipped capitalised-cast parameter codes, giving the doc's "11/11" claim
   (`docs/features/fhirpath/resource-backed-parity-corpus.md:107`) an executable anchor. Do **not**
   fan out further (e.g. every `[InlineData]` in `AsOperatorSearchParameterCardinalityTests`) — that
   is a different, larger conversation; these theories at least assert concrete per-row behavior.

### Falsification step

- Enumeration guards: delete the `%rootResource.id` row from the new TheoryData, run the suite, assert
  the inventory fact goes red (this is the exact demonstration a reviewer performed for `1 'mg'` —
  repeat it deliberately), restore.
- The floor: temporarily change one row's expression to something empty on both engines
  (e.g. `missingElement.id`), observe the **old** assertion pass (proving the hollowness existed) and
  the **new** floor fail (proving the guard discriminates). Restore. Record both observations in the PR.

Rule carried over from `84cec49f`'s own doc comment: the inventory literal must be written
independently of the TheoryData it guards — an inventory computed from the collection agrees with any
edit and asserts nothing.

---

## WI-3 — #426: census over `[FhirPathFunction]` `ReturnType` declarations

The issue's sketch is right; build it as sketched, with one strengthening.

- New test beside `ValueConstructionReturnTypeTests` (which guards the complementary direction:
  constructing bodies must not declare `"context"` — this census guards declared-type entries against
  the `src/Core/Ignixa.FhirPath/Analysis/SystemTypeConstructionAnalyzer.cs:227-230` arm).
- Reflect over every `[FhirPathFunction]` attribute in the production assemblies (the pattern and
  assembly enumeration are already written —
  `test/Ignixa.FhirPath.Tests/Evaluation/SystemValueElementDeclarationTests.cs:95-125`), collect
  every distinct declared `ReturnType`, and require each to appear in a `Decisions`-style dictionary:
  keyword / constructs-System-value / constructs-no-System-value, with a rationale string. A new
  `ReturnType` value without an entry fails the build with a message telling the author what to decide.
- **Strengthening — cross-check behavior, not just the list.** `GetSystemPrimitiveRuntimeTypeName`
  (`src/Core/Ignixa.FhirPath/Analysis/SystemTypeConstructionAnalyzer.cs:290-317`) is private, but `Ignixa.FhirPath.Tests` has
  `InternalsVisibleTo` (`src/Core/Ignixa.FhirPath/Ignixa.FhirPath.csproj:32`). For each recorded
  decision, build a synthetic `FunctionCallExpression` for that function and call
  `SystemTypeConstructionAnalyzer.Analyze`, asserting the verdict class (named System type / `None` /
  `Any`) matches the recorded decision. That pins the mapping's behavior without mirroring the private
  switch — mirroring it would be a second copy of the same enumerated list, which is the defect shape
  this issue exists to retire.
- If WI-1 introduced a new keyword, its census entry lands here in the same PR.

### Falsification step

1. Temporarily add a scratch function to the production library:
   `[FhirPathFunction("censusProbe", ReturnType = "Money")] ...` — run the census filter, assert red
   (no recorded decision), remove.
2. Flip one recorded decision (e.g. mark `toString`'s `String` as constructs-no-System-value), assert
   the behavioral cross-check goes red, restore.

The issue's severity call is right: this is currently complete and currently fails open. The census is
cheap insurance against the one unsound direction (a future known-FHIR-type `ReturnType` that actually
constructs a System value resolving to a confident `None`), which is the exact shape that caused four
of the five commits ending at `e77eba1e`.

---

## WI-4 — #428: `repeatAll()` tier demotion — **AFFIRM, and pin it**

### The recommendation

Affirm Warning/`FailedToExtractValues`. Justification from how the indexer classifies everything else:

- The two-tier contract is written on `IsExpectedEvaluationFailure` itself
  (`src/Core/Ignixa.Search/Indexing/ElementSearchIndexer.cs:600-624`): Warning is for
  "FHIRPath evaluation failures the write path is expected to see against real-world data **or custom
  search parameters** … or any other expression-level rejection defined by
  `FhirPathEvaluationException`". Error is for indexer/converter code defects (NRE, InvalidCast,
  ArgumentNull/OutOfRange — `:614-621`).
- The iteration guard (`src/Core/Ignixa.FhirPath/Evaluation/Functions/CollectionFunctions.cs:518-520`) is a deliberate evaluator-side resource limit
  on tenant-controllable input: a tenant-supplied search parameter times a tenant-supplied resource
  shape. It is triggered by data, deterministically, at will. That is the same class as "bad literal"
  (`FormatException`, Warning) and "unsupported function" (`NotSupportedException`, Warning) — the
  comparable tenant-data failures the indexer already treats as containment, not alarm. An Error tier a
  tenant can ring on demand turns the Error channel — whose whole purpose here is "this is *our* bug" —
  into noise.
- The write outcome is identical in both tiers (value skipped, write survives); only alerting differs.
- Reverting has no clean implementation. The evaluator-side type is pinned as
  `FhirPathEvaluationException` (`RemainingCoverageTests`), consistent with the other 32 conversion
  sites. Keeping this one at Error would require the indexer to discriminate by message text or a new
  exception subtype. Message-matching is exactly the fragile enumerated-list shape #426 exists to kill.
  A subtype (`FhirPathResourceLimitException : FhirPathEvaluationException`) is defensible but buys
  nothing unless someone monitors it separately — and nothing does.

On the bare `InvalidOperationException` observation: its absence from the expected set is **correct and
should stay**. IOE is the BCL's "object in invalid state" (`Single()` on 2+, collection mutated during
enumeration) — a code-defect signal, correctly Error. The pre-#427 state, where a deliberate domain
rejection was spelled as a BCL state exception, was the anomaly; the sweep corrected the spelling and
the tier followed the spelling. The demotion is the type system starting to tell the truth, not a guard
being deleted. Say this in the pinning test's doc comment so the next sweep doesn't "fix" it back.

### The test (the pin)

In `SearchIndexerFailureContainmentTests` (patterns at
`test/Ignixa.Application.Tests/Search/Indexing/SearchIndexerFailureContainmentTests.cs:87-112`):
index a resource against a custom search parameter whose expression is `repeatAll($this)` (drains never;
trips the 100,000 guard in well under a second of trivial projections). Assert: Warning
`FailedToExtractValues` captured, **zero** Error entries, sibling parameters still index, write survives.

### Falsification step

Temporarily revert the guard's exception to its pre-#427 spelling
(`InvalidOperationException` at `src/Core/Ignixa.FhirPath/Evaluation/Functions/CollectionFunctions.cs:520`), run the new test, assert red (Error
observed where Warning asserted), restore. This is a genuine tier-discriminating mutation — it
reproduces the exact historical state the test exists to distinguish from.

### Adjacent finding — file a new issue, do not fix here

`repeat()` (`src/Core/Ignixa.FhirPath/Evaluation/Functions/CollectionFunctions.cs:442-485`) has **no iteration guard at all**. Its dedup cannot
terminate a projection that constructs fresh values each round (`repeat($this & 'x')` yields an
ever-growing string, never deep-equal to anything processed): unbounded loop and unbounded memory on the
write path, tenant-suppliable. `repeatAll` got the guard; `repeat` did not. Out of scope for all six
residuals — file it, referencing #428, and note that when it is fixed the new guard must throw
`FhirPathEvaluationException` so it lands in the tier this work item just pinned.

---

## WI-5 — #424: stays deferred

The deferral reasoning holds and is already encoded in the code: the fail-open at `AnalyzeChild`
(`src/Core/Ignixa.FhirPath/Analysis/SystemTypeConstructionAnalyzer.cs:47-76`) produces no false always-empty, the shipped-corpus cost is
0 of 8,827 pairs (verified positively: zero corpus expressions with a constructed-System-value
navigation focus), and the remarks block explicitly forbids tightening the arm without consulting the
focus. Both symptoms are pinned: the `type()`/`is` disagreement by
`test/Ignixa.FhirPath.Tests/Evaluation/SystemValueTypeMatchingTests.cs:GivenQuantityLiteral_WhenItsTypeIsReported_ThenItRemainsFhirQuantity`
(recorded in the `QuantityElement` decision rationale,
`test/Ignixa.FhirPath.Tests/Evaluation/SystemValueElementDeclarationTests.cs:80`), and the fail-open behavior by
`test/Ignixa.FhirPath.Tests/Analysis/AnalyzerCastProvenanceRegressionTests.cs:GivenAChildNavigatedOffAConstructedQuantity_...:396`.

**Trigger to revisit** (record on the issue, close nothing): the first population containing
quantity-rooted navigation — tenant/custom search parameters accepted into indexing, or FHIRPath drawn
from invariants rather than search parameters. Concretely: when custom search parameter ingestion ships,
run the corpus-analysis check in `SearchParameterExpressionCorpusAnalysisTests` over the new population;
a nonzero count of constructed-System-value navigation foci is the trigger. At that point the surface is
genuinely small — one type, two members (`value`, `unit`) — and the fix belongs in `AnalyzeChild`
(consult the focus's constructed set and the member name) plus the `type()` half in
`src/Core/Ignixa.FhirPath/Evaluation/Functions/CollectionFunctions.cs:Type`'s `isSystemLiteral` quantity special-case, and it must clear the same sweep
bar as WI-1.

Do not build the member map now. See "Do not do this".

### Sequencing note (the question the plan was asked to answer)

**#424's fail-open neither masks nor conflicts with the #423 fix.** The 28 false-always-empty rows never
route through `AnalyzeChild`: their foci are plain navigation chains (`name`, root-property provenance
`None` via `AnalyzePropertyAccess`, `src/Core/Ignixa.FhirPath/Analysis/SystemTypeConstructionAnalyzer.cs:79-91`), and the false claim is
manufactured on the **FHIR-type axis** (`ReturnsContext`) at the cast site, with provenance a correct
`None`. Conversely a future #424 member map touches only `ChildExpression` handling; `repeat` is a
`FunctionCallExpression`. The only real coupling is the one described in WI-1: changing repeat's
`DeclaredReturnType` string changes which `AnalyzeFunction` arm fires. Fix #423 first, decide the
provenance arm deliberately, and the #424 deferral is untouched by it.

---

## Sequencing and PR batching

1. **PR 1 — #423 alone.** A production analyzer semantics change with a measurement bar; its PR
   description carries the base/HEAD joined sweep evidence. Nothing else rides with it — reviewers
   must be able to see the sweep answer the one question this PR asks.
2. **PR 2 — #425 + #429 + #426.** Test-only guard hardening, no production code, one theme ("pinned
   sets that nothing enumerates"). If PR 1 introduced a new `ReturnType` keyword, PR 2's census must
   land after PR 1 (add the keyword's decision entry); otherwise the two are order-independent.
3. **PR 3 — #428.** Test-only, but it carries a policy affirmation that must be visible in the PR
   title and description, not buried in a guard-hardening batch. Half a day. File the `repeat()`
   unguarded-loop issue alongside it. If the maintainer signs off on "affirm" before PR 2 goes up, this
   can fold into PR 2 — but the default is separate, because the decision is the deliverable.
4. **#424 — no PR.** Comment the revisit trigger onto the issue; leave it open.

Total: roughly 4–6 working days across three small PRs, of which the #423 sweep is the long pole.

---

## Do not do this

1. **Do not conflate the two analysis axes.** `HasAlwaysEmptySubexpression` and `IsValid` are separate
   columns in every sweep, join, and assertion. Every `is` expression evaluates to exactly one boolean,
   so any `IsValid=false` row superficially resembles a false always-empty the moment the axes merge —
   this manufactured phantom violations on every `is` expression and cost a reviewer a full pass.
   The regression tests in WI-1 assert the two properties in separate statements with separate messages.
2. **Do not build the System.Quantity member map (#424).** The population it would serve is measured at
   zero in shipped data; 7,696-of-18,240 is synthetic-weighting, not a regression — the remarks at
   `src/Core/Ignixa.FhirPath/Analysis/SystemTypeConstructionAnalyzer.cs:57-66` say exactly this. Building it now is precision for a
   corpus that does not exist, plus a new enumerated member list to keep sound.
3. **Do not tighten `AnalyzeChild` while fixing #423.** The 28 rows do not come from there. The arm's
   over-approximation is load-bearing soundness; the remarks forbid asserting a negative without
   consulting the focus.
4. **Do not derive an enumeration inventory from the TheoryData it guards.** Independent literal, or it
   agrees with every edit and asserts nothing (`84cec49f`'s own documentation).
5. **Do not pin #428's tier by exception message.** `"possible infinite loop"`-matching in the indexer
   is a string-keyed enumerated list — the shape five commits were spent killing. Pin behavior at the
   indexer boundary (log level + event), and pin the evaluator side by exception type only.
6. **Do not close any item on "reviewed the test/diff".** Every falsification step above is an executed
   mutation with an observed red. The historical base rate here is six tests that passed but could not
   fail, four self-certified-clean commits that were not, and a census blind exactly where the shipped
   defect sat — all caught only by execution.
7. **Do not sneak an iteration guard into `repeat()` in these PRs.** It is a real defect, it gets its
   own issue, and its exception type has tier consequences that #428's pin now makes explicit.
