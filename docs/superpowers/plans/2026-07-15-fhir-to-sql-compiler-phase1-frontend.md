# Phase 1 — Land the Search Parser Front-End Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring the already-built, already-CI-green search-parser front-end (scan → bind, PR #332) onto `feature/fhir-to-sql-compiler`, verify it holds together with Step 0's work, and resolve the one open question about the frozen legacy parser's fate.

**Architecture:** This is an integration task, not a build task. PR #332 (`brendankowitz-investigate-search-parser-superpower`, open on `main`, `MERGEABLE`/`CLEAN`, all CI green, 885 tests passing, BenchmarkDotNet-gated) already implements exactly what the design doc's step 1 calls for: handwritten span scanners (`SearchKeySyntaxParser`/`SearchValueSyntaxParser`) → schema-aware binders (`SearchKeyBinder`/`SearchExpressionBinder`), with the pre-rewrite parser frozen as unwired `Ignixa.Search.Expressions.Parsers.Legacy.*` classes (a deliberate two-line rollback lever, not dead code). This plan merges that branch **locally into `feature/fhir-to-sql-compiler`** — it does **not** touch GitHub PR #332 or `main`. Merging PR #332 into `main` is a separate, real, visible action (affects the shared repo, needs its own explicit go-ahead) and is out of scope here.

**Tech Stack:** Same as the rest of the repo — no new dependencies. PR #332 confirmed zero net dependency change (the Superpower parser-combinator library it evaluated and rejected was never left referenced).

## Global Constraints

- `dotnet build All.sln` must stay 0 warnings, 0 errors after every task.
- `dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"` must stay green (matches CI's convention, confirmed during Step 0's finishing work).
- No new production code is written in this plan — it is a merge + verification + one documented decision.
- This is a local integration onto `feature/fhir-to-sql-compiler` only. Do not run `gh pr merge`, do not push to `main`, do not touch GitHub PR #332's state.

---

### Task 1: Merge the front-end branch into `feature/fhir-to-sql-compiler`

**Files:**
- No files are hand-edited in this task — the change is the merge itself. Conflict resolution (expected: none, per the pre-flight check below) would touch whatever files conflict.

**Interfaces:**
- Consumes: `origin/brendankowitz-investigate-search-parser-superpower` (PR #332's branch), already fetched.
- Produces: `feature/fhir-to-sql-compiler` advanced to include `SearchKeySyntax`/`SearchValueSyntax`, `SearchKeyBinder`/`SearchExpressionBinder`, `Ignixa.Search.Expressions.Parsers.Legacy.*`, and `SearchParserOldVsNewParityTests.cs` — the types later Phase 2 work (semantic IR) builds on.

- [ ] **Step 1: Confirm the pre-flight check still holds**

This was already verified once during planning (2026-07-15); re-confirm since real time may have passed:

```bash
git fetch origin brendankowitz-investigate-search-parser-superpower main
git merge-base feature/fhir-to-sql-compiler origin/brendankowitz-investigate-search-parser-superpower
git merge-tree $(git merge-base feature/fhir-to-sql-compiler origin/brendankowitz-investigate-search-parser-superpower) feature/fhir-to-sql-compiler origin/brendankowitz-investigate-search-parser-superpower > /tmp/merge-tree-check.txt
grep -c "^<<<<<<<" /tmp/merge-tree-check.txt
```

**Expected:** the grep count is `0` (no conflict markers). Also re-check PR #332's current state hasn't changed underneath you:

```bash
gh pr view 332 --repo brendankowitz/ignixa-fhir --json state,mergeable,mergeStateStatus
```

**Expected:** `"state":"OPEN"`, `"mergeable":"MERGEABLE"`, `"mergeStateStatus":"CLEAN"`. If any of this has changed (PR merged/closed by someone else, new conflicts introduced by a push to either branch), STOP and report — the rest of this plan assumes this pre-flight check holds.

- [ ] **Step 2: Merge**

```bash
git checkout feature/fhir-to-sql-compiler
git merge origin/brendankowitz-investigate-search-parser-superpower --no-edit -m "$(cat <<'EOF'
Merge PR #332 (search parser front-end: scan + bind) into feature/fhir-to-sql-compiler

Brings in SearchKeySyntax/SearchValueSyntax scanners, SearchKeyBinder/
SearchExpressionBinder, the old-vs-new parity harness, and the frozen
Legacy.* rollback lever, per the fhir-to-sql-compiler roadmap's Phase 1.
Local integration only -- PR #332 itself is untouched, still targets main.
EOF
)"
```

**Expected:** fast-forward or a clean merge commit, no conflicts (per Step 1's dry-run).

- [ ] **Step 3: Verify build and tests**

```bash
dotnet build All.sln --nologo
dotnet test All.sln --filter "FullyQualifiedName!~E2ETests" --nologo
```

**Expected:** 0 warnings, 0 errors. Test run green — PR #332's own report claims 885 tests passing in `Ignixa.Application.Tests` alone; the full run should now include those plus everything already on `feature/fhir-to-sql-compiler` (including Step 0's `Ignixa.DataLayer.SqlEntityFramework.IntegrationTests`, which should still show its two tests `[SKIP]`, not attempt to run).

- [ ] **Step 4: Confirm no interaction with Step 0's work**

PR #332 adds `[assembly: InternalsVisibleTo("Ignixa.Application.Tests")]` to `Ignixa.Search.csproj`. Confirm this doesn't affect `Ignixa.DataLayer.SqlEntityFramework.IntegrationTests` (a different assembly, added in Step 0) — it shouldn't, but grep for any accidental overlap:

```bash
grep -rn "InternalsVisibleTo" src/Core/Ignixa.Search/*.cs test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/*.csproj
```

**Expected:** the `InternalsVisibleTo` attribute only names `Ignixa.Application.Tests`, not anything Step 0 touched. No action needed if so — this step exists to catch a surprise, not to make one.

- [ ] **Step 5: Push the merge (ask first — see plan note)**

Do not push automatically. Report the merge is complete locally and ask whether to push `feature/fhir-to-sql-compiler` to `origin` now or later — this changes a shared branch other people (or CI) may be watching.

---

### Task 2: Resolve the Legacy-parser-classes question and update the roadmap

**Files:**
- Modify: `docs/superpowers/plans/2026-07-15-fhir-to-sql-compiler-roadmap.md`

**Interfaces:**
- Consumes: nothing new.
- Produces: an explicit, recorded decision on whether `Ignixa.Search.Expressions.Parsers.Legacy.*` gets deleted now or retained as a rollback lever — this affects what later phases can assume exists.

**Context for whoever picks this up:** the roadmap (written before PR #332's actual content was inspected) says *"if PR #332 lands, delete the classes it supersedes... not just leave them"* — modeled on the general "freeze, don't delete during migration, delete once truly retired" pattern. Having now read PR #332's actual body, its own **already-reviewed, CI-green design deliberately keeps the old parser** as unwired `Legacy.*` classes specifically as a two-line rollback lever (`SearchOptionsBuilderFactory.cs`'s construction site is commented, not deleted), matching the *design doc's own* migration guidance verbatim ("Freeze, do not delete... Rollback is a two-line DI swap plus redeploy"). These are two different things: PR #332 already deleted the code that's genuinely dead (738 deletions, confirmed no old resource key or public API was removed without a replacement); what remains under `Legacy.*` is deliberately-kept-alive insurance, not leftover cruft.

- [ ] **Step 1: Read `Ignixa.Search.Expressions.Parsers.Legacy.*` to confirm its scope**

```bash
git -C /path/to/repo show HEAD -- src/Core/Ignixa.Search/Expressions/Parsers/Legacy/ | head -50
grep -rn "Legacy" src/Core/Ignixa.Search/Expressions/Parsers/*.cs | grep -i "class\|namespace"
```

Confirm: these classes are `public`, unwired from any DI/construction path (`SearchOptionsBuilderFactory.cs`'s legacy construction site is commented out, not active), and PR #332's stated rollback mechanism is "a two-line swap... followed by a redeploy."

- [ ] **Step 2: Record the decision in the roadmap**

Update `docs/superpowers/plans/2026-07-15-fhir-to-sql-compiler-roadmap.md`'s coordination checklist / Phase 1 row with:

> **Decision (2026-07-15, revised from the original checklist item):** Keep `Ignixa.Search.Expressions.Parsers.Legacy.*` as-is. PR #332's own reviewed design already implements the "freeze, don't delete" migration pattern the design doc itself recommends, as a rollback lever until this compiler project reaches cutover (design doc step 9/10). Deleting it now would remove the one cheap rollback path for the *parser* rewrite while a much larger, riskier compiler rewrite is still in progress on top of it — the two migrations should not be coupled. Revisit deletion of `Legacy.*` at the same time the design doc's "freeze, do not delete" `SearchParameterQueryGenerator` guidance is revisited (i.e., step 9/10 cutover), not before.

- [ ] **Step 3: Commit**

```bash
git add docs/superpowers/plans/2026-07-15-fhir-to-sql-compiler-roadmap.md
git commit -m "docs(roadmap): keep PR #332's frozen Legacy parser as a rollback lever, not delete-on-land"
```

## Self-Review

- **Spec coverage:** Task 1 covers "land PR #332" (the roadmap's Phase 1 scope). Task 2 covers and resolves the roadmap's own "delete superseded classes" checklist item, with a documented, reasoned reversal rather than a silent skip.
- **Placeholder scan:** none — this plan is short because the underlying work already exists and is verified; nothing here is deferred without a stated reason.
- **Type consistency:** N/A — no new types introduced by this plan.
