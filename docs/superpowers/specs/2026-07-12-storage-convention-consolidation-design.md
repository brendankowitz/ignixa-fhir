# Design: Storage Convention Consolidation (Phase 3, Step 1)

Date: 2026-07-12
Status: Approved (Fable-reviewed, resolutions incorporated)
Predecessor: `docs/superpowers/specs/2026-07-11-composite-structure-preservation-design.md` (Phase 2, merged)
Reference: `docs/features/sql-datalayer-architecture/investigations/staged-query-compiler.md` ("Phase 3" scope, audit finding 4)

## Problem

Ignixa's SQL search-index layer has ~14 real `SearchParamType`s/composite shapes (7 single-parameter +
6 composite + TokenText), each with its storage facts — table, columns, widths, collation, normalization
rules — independently re-encoded on two sides that never reference a shared source of truth:

- **Write path**: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/*.cs` (19 files; 3 are
  dead/stub/non-catalog — see "Dead code" below).
- **Read path**: `Search/SearchParameterQueryGenerator.cs`, `Search/CompositeSearchParameterQueryGenerator.cs`,
  `Search/ComparisonPredicates.cs`.

This duplication has already produced live, confirmed drift bugs:

1. **Composite token codes silently stop matching above 128 characters — and the threshold itself is
   wrong.** The composite TVP columns (`Code1`/`Code2` on `TokenTokenCompositeSearchParamList` and every
   sibling composite type, spanning `97.sql` lines ~99-231) are declared `VARCHAR(256) COLLATE
   Latin1_General_100_CS_AS` — the same width as the single-token `TokenSearchParam.Code` column, which
   `TokenCodeStorage.MaxInlineCodeLength = 256` already governs correctly. But every composite row
   generator (`RowGenerators/TokenTokenCompositeRowGenerator.cs` and its `TokenQuantity`/`TokenString`/
   `TokenDateTime`/`RefToken`/`TokenNumberNumber` siblings) hand-rolls its own `SqlMetaData(..., 128)` and
   splits at a hardcoded `128`, wasting half the inline column and truncating incorrectly relative to its
   own DDL. The read side then compounds this: `CompositeSearchParameterQueryGenerator`'s
   `Generate*QueryAsync` methods filter `t.Code1 == token.Code` against the *un-truncated* search value and
   never read the `CodeOverflow1`/`CodeOverflow2` columns back — so even within the (wrong) 128-char
   threshold, an overflowed code never matches. The single-token read path already does this correctly
   (`SearchParameterQueryGenerator.cs`, ~lines 1292-1302: `code.Length > MaxInlineCodeLength` →
   concatenate `Code + CodeOverflow`) — that logic exists, it was just never extended to composites.
2. **Token code comparison is case-insensitive for single search parameters, case-sensitive for composite
   ones.** Single-token reads apply `EF.Functions.Collate(sp.Code, "Latin1_General_100_CI_AS")`
   (commit `721f8ff2`, deliberate, pinned by `test/Ignixa.Api.E2ETests/Search/DataTypes/TokenSearchTests.cs`'s
   `GivenATokenSearchParameterWithTwoValuesThatOnlyDifferInCase_...`). Composite-token reads use ordinal
   `t.Code1 == token.Code`, which is not a deliberate policy — it's an accident of inheriting the
   `Code1`/`Code2` columns' default `Latin1_General_100_CS_AS` collation from `Resources/97.sql`. Same
   logical field, two different comparison behaviors depending only on whether it's searched standalone or
   as a composite component.
3. **String normalization strategy differs by search context, not by declared type.** Single `String`
   search parameters store the original case and rely on query-time collation (`CI_AI` by default,
   `CS_AS` override for the `:exact` modifier). Composite `Token|String` stores `ToUpperInvariant()` on
   write (`RowGenerators/TokenStringCompositeRowGenerator.cs:117`) and folds identically on read
   (`CompositeSearchParameterQueryGenerator.cs:297`, ordinal `StartsWith` at line 298) — which cannot
   support `:exact` and, since the `TokenStringCompositeSearchParam` table's `Text2`/`TextOverflow2`
   columns are themselves declared `Latin1_General_CI_AI` (`97.sql:909-910`), is provably *redundant for
   default-search matching today*: dropping the fold on both sides yields byte-identical results against
   freshly indexed data. Its only real effect is destroying original case, permanently foreclosing
   `:exact` on composite string.
4. **The same 128-vs-256 width bug from finding 1 also exists on the string component, independent of the
   normalization-policy question in finding 3.** `TokenStringCompositeRowGenerator.cs:20` declares
   `StringColumnMaxLength = 128` and splits `Text2` at that threshold (lines 118-122) — but
   `TokenStringCompositeSearchParam.Text2`/`TextOverflow2` are `NVARCHAR(256)` columns (matching
   `StringSearchParameterRowGenerator.cs:35`'s correct 256 width for the single-`String` case). The read
   side compounds this exactly like finding 1: it filters on `Text2` alone and never reads
   `TextOverflow2` back. This is the same bug class shipping on both components of the one composite type
   that has two variable-length text-ish fields (Token's `Code2` and String's `Text2`), not a separate,
   unrelated issue.

## Goals

- Extract the storage conventions each of these bugs exposes into small, shared static helper classes —
  extending the existing `TokenCodeStorage.cs` precedent from Phase 0 — consumed by both the write and
  read paths, so each convention has exactly one home instead of two independently-drifting copies.
- Fix all four confirmed drift bugs as part of this consolidation (not just document them): composite
  token overflow (correct 256-char threshold, overflow-aware reads), composite token case-sensitivity
  (converge to case-insensitive, matching the single-param policy), composite string normalization
  (converge to original-case + collation, matching the single-param policy) — and, found during spec
  review, the identical overflow bug on the string component (`Text2`/`TextOverflow2`, also 128-vs-256).
- Delete genuinely dead write-path code discovered during investigation (`RowGenerators/QuantityCodeRowGenerator.cs`).
- Retag two TODO comments whose "Phase 3" reference now collides confusingly with this cleanup's own
  Phase 3 numbering.

## Non-goals (deferred to a future Step 2 investigation)

- **A full per-type declarative storage descriptor** (`{ table, EF entity set, columns, widths, collation,
  normalization strategy, range-encoding kind }` consumed by both `RowGenerators` and `Search/*QueryGenerator`
  through a shared lookup) — this is the complete answer to audit finding 4 ("one declarative source per
  type"), but it's a materially larger design surface than this spec's scope: 14 different EF entity
  types, a real question about how far declarative data can replace hand-written EF predicate/population
  code before fighting EF Core's LINQ-to-SQL translator (confirmed literal-lambda-body requirement — see
  `ComparisonPredicates.cs`'s own class doc comment and `ComparisonPredicates.ApplyTtlComparison`'s comment,
  which records an actual failed attempt at a more data-driven alternative: "verified — it throws 'could
  not be translated'"), and a real migration-sequencing decision (incremental per-type vs. any bigger
  step). This mirrors exactly how Phase 1 was separated from Phase 2 in this same effort's own staged
  adoption: ship the near-zero-risk piece, re-scope the next increment once its real cost is known. Once
  this spec's helpers exist, they *are* the raw material Step 2's descriptor would be built from — so this
  work is a prerequisite for Step 2, not a detour from it.
- **A fully generic, reflection/expression-tree-driven read-and-write pipeline** (rejected outright, not
  just deferred) — hits the same EF Core literal-lambda-body wall as any Step 2 design, at a much larger
  blast radius, against this codebase's own explicit YAGNI stance in CLAUDE.md. Not revisited unless new
  evidence emerges that Step 2's incremental approach is insufficient.
- **Finishing `ResourceWriteClaimRowGenerator`** (its `yield break` stub is not dead code — it supplies the
  mandatory-but-currently-empty `@ResourceWriteClaims` TVP parameter the `MergeResources` stored procedure
  requires, per `97.sql:3403`; actually populating it is an auth/audit *feature*, unrelated to storage-fact
  duplication).
- **Implementing versioned-reference search** (`ReferenceSearchParameterRowGenerator.cs`'s
  `ReferenceResourceVersion` TODO) **or reindex bookkeeping** (`ResourceEntity.SearchParamHash` TODO) — both
  are real, separately-scoped features, not storage-duplication cleanup. This spec only retags their TODO
  comments so they stop colliding with this effort's own "Phase 3" numbering in future greps.
- **Fixing the `COLLATE`-defeats-index-seek performance cost** introduced by extending case-insensitive
  collation to composite token reads. This cost already exists on the single-token path today (accepted
  there since commit `721f8ff2`); converging composites onto the same policy extends an already-accepted
  tradeoff, not a new one. The clean long-term fix (changing the `Code1`/`Code2` column collations to
  `CI_AS` directly in `97.sql`, since this project has no production deployments to protect — see
  "Pre-production status" below) trades away DDL parity with the upstream Microsoft FHIR Server schema
  this file derives from. Tracked as a follow-up, not fixed here.
- **Resource-level pseudo-parameters** (`_id`, `_lastUpdated`, `_type`, `_ttl`) — these query `Resources`/
  surrogate-ID ranges directly, not a per-type index table, and their write/read pairing is already
  unified where it matters (`ComparisonPredicates.ApplyTtlComparison` etc.). Out of scope for a
  storage-index-table consolidation.

## Pre-production status (context for the normalization convergence decisions)

This codebase is explicitly not a production product today: `README.md` describes it as "Advanced
Research / Reference Implementation... not a supported production product," and its schema is created
fresh from the embedded `Resources/97.sql` on empty databases (`DatabaseInitializer.cs`) with no
migration-from-production-data story. This is why Goals above can converge existing normalization
behavior (composite token case-sensitivity, composite string case-folding) rather than treating it as a
breaking-change decision requiring a rollout plan — there is no real indexed data whose search behavior
would silently change underneath a live deployment. If this status changes before this work ships, that
assumption needs to be re-checked.

## Design

### 1. Extend `TokenCodeStorage` (existing file, `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/TokenCodeStorage.cs`)

Read the current file in full before implementing — it already has `MaxInlineCodeLength = 256`,
`IsExplicitNoSystem`, and a code-splitting method (confirm the exact current method name/signature by
reading the file; do not assume). Add:

- **A shared collation constant**: `public const string CaseInsensitiveCollation = "Latin1_General_100_CI_AS";`
  — extracted from its 5+ independent string-literal appearances in `SearchParameterQueryGenerator.cs`
  (including the `identifier:of-type` extension-column path, ~line 1276), replacing each with a reference
  to this constant. This is what converges composite-token reads onto the single-token policy (Goal 2):
  every `Generate*QueryAsync` method in `CompositeSearchParameterQueryGenerator.cs` that currently compares
  `Code1`/`Code2` with ordinal equality switches to `EF.Functions.Collate(t.Code1, TokenCodeStorage.CaseInsensitiveCollation) == code`,
  matching the single-token pattern exactly.
- **Reuse, not reimplementation, of the existing 256-char split/overflow logic** in every composite row
  generator that currently hand-rolls a 128-char `SqlMetaData`/split (confirmed instances:
  `TokenTokenCompositeRowGenerator.cs`, `RefTokenCompositeRowGenerator.cs`, `TokenDateTimeCompositeRowGenerator.cs`,
  `TokenQuantityCompositeRowGenerator.cs`, `TokenStringCompositeRowGenerator.cs`, `TokenNumberNumberCompositeRowGenerator.cs`
  — verify each file's exact current threshold/split code before changing it, the investigation found the
  pattern repeated but each file's exact local variable names may differ). This fixes Goal-1's overflow bug:
  correct 256-char threshold matching the actual `97.sql` column widths, `CodeOverflow1`/`CodeOverflow2`
  populated identically to how `TokenSearchParameterRowGenerator` already does it for single tokens.
- **Overflow-aware composite reads**: each affected `Generate*QueryAsync` method in
  `CompositeSearchParameterQueryGenerator.cs` needs the same length-check-then-concatenate pattern
  `SearchParameterQueryGenerator.cs` (~lines 1292-1302) already uses for single tokens, applied to
  `Code1`/`CodeOverflow1` and/or `Code2`/`CodeOverflow2` depending on which composite type has the token
  component in which position. Read that existing code before writing the composite equivalent — this is
  a direct pattern match, not new design.

### 2. New `StringStorage` helper (`src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/StringStorage.cs`)

Converges composite `Token|String` onto single `String`'s normalization policy (Goal 3), **and** fixes
the same width/overflow bug finding 1 identified, on the string component this time:

- `RowGenerators/TokenStringCompositeRowGenerator.cs:117`'s `ToUpperInvariant()` call is removed — store
  the original-case value, same as `StringSearchParameterRowGenerator` already does.
- `TokenStringCompositeRowGenerator.cs:20`'s `StringColumnMaxLength = 128` constant and the split at
  lines 118-122 are wrong relative to the actual `TokenStringCompositeSearchParam.Text2`/`TextOverflow2`
  columns, which are `NVARCHAR(256)` — the identical bug class as finding 1, on `Text2` instead of
  `Code2`. Fix to 256, matching `StringSearchParameterRowGenerator.cs:35`'s correct single-`String`
  width.
- `CompositeSearchParameterQueryGenerator.cs:297-298`'s matching uppercase fold + ordinal `StartsWith` is
  removed from the read side; replace with the same collation-based, overflow-aware comparison pattern
  `SearchParameterQueryGenerator.cs`'s single-`String` read path already uses (~lines 1380-1470: default
  collation for normal search, `CS_AS` override for `:exact`, `Text`/`TextOverflow` concatenation above
  the inline threshold) — read that existing method to confirm the exact current pattern and mirror it
  for `Text2`/`TextOverflow2`, don't reinvent. Today's composite read only ever queries `Text2`, never
  `TextOverflow2` — this is the same never-read-the-overflow-column gap as finding 1's `Code2`/`CodeOverflow2`.
- The shared inline width (`256`, matching finding 1's `TokenCodeStorage.MaxInlineCodeLength`) belongs in
  `StringStorage` alongside the collation constants (default + `:exact`), so both single and composite
  String consult one source for width and one for each collation mode.

### 3. Delete dead code

`RowGenerators/QuantityCodeRowGenerator.cs` — confirmed via repo-wide grep to have no reference anywhere
except its own declaration; not instantiated by `SqlMergeRepository` or any other caller. Its
`Code.GetHashCode()`-as-placeholder-ID pattern is a correctness landmine if anyone ever wires it up by
mistake. Delete the file and its test coverage (if any exists — check for a corresponding test file before
deleting only the production file).

### 4. Retag two TODO comments (comment-only change, no behavior change)

- `ReferenceSearchParameterRowGenerator.cs:90`: `// TODO Phase 3: Extract version if available` → retag
  to name the actual feature instead of a phase number that will collide with this cleanup's own Phase 3
  in future greps, e.g. `// TODO(versioned-references): Extract version if available`.
- `RowGenerators/ResourceRowGenerator.cs:114`: `// SearchParamHash: TODO Phase 2` (not on `ResourceEntity`
  itself, and references Phase 2, not Phase 3 — corrected citation) → retag similarly, e.g.
  `// SearchParamHash: TODO(reindex)`, naming the reindex-bookkeeping feature it's waiting on.

## Testing strategy

**Critical infrastructure constraint, confirmed during spec review**: the EF Core InMemory provider used
by `TestBase.cs` (`UseInMemoryDatabase`) cannot translate `EF.Functions.Collate` — this is a documented,
pre-existing limitation (see `SearchExpressionQueryBuilderVisitorTests.cs`'s doc comment, which explains
why that test class deliberately uses Number-typed parameters to dodge the String path's use of
`EF.Functions.Collate`). This phase's whole point is converging composite token/string comparison onto
the same `EF.Functions.Collate`-based pattern the single-parameter paths already use — which means, once
the read-path changes in this phase land, **every composite query that touches a Token or String
component will throw `InvalidOperationException` at query materialization under EF InMemory**, not fail
an assertion. Since every one of the 6 composite shapes pairs with Token, this affects essentially the
entire existing `CompositeSearchParameterQueryGeneratorTests.cs` read-path test surface, not just the
new case-insensitivity/`:exact` cases.

Disposition:

- **Write-path (`RowGenerators`) unit tests are unaffected** — `SqlDataRecord` population never touches
  `EF.Functions.Collate`; these can stay as ordinary InMemory-backed (or collate-free) unit tests. The
  overflow-threshold fix (128→256, both `Code2` and `Text2`) is plain string-length/split logic with no
  collation involved, so write-side characterization tests for it belong here.
- **Read-path characterization tests that exercise `EF.Functions.Collate`** (composite token
  case-insensitivity, composite string `:exact`-vs-default) **move to `test/Ignixa.Api.E2ETests`**,
  against a real SQL Server via the existing `IgnixaApiFixture` — the same venue that already pins
  single-token case-insensitivity (`Search/DataTypes/TokenSearchTests.cs`). Add composite-shape
  equivalents there, following that file's existing pattern.
- **The existing composite read-path unit tests in `CompositeSearchParameterQueryGeneratorTests.cs`
  that will start throwing once this phase's read-method changes land must be re-homed to
  `Ignixa.Api.E2ETests` alongside the new characterization tests**, not left in place to fail — the
  implementation plan must explicitly enumerate which existing cases move (Fable's review confirmed all
  6 composite types pair with Token, so expect this to be most or all of that file's read-path
  assertions) versus which, if any, can be restructured to avoid the collate-bearing path and stay
  InMemory. Do not discover this by running the suite and reacting to failures — plan the disposition of
  each existing test case explicitly before implementing the read-path changes.
- **Overflow-length characterization** (a composite token/string value over 256 characters now matches,
  previously silently didn't) can remain as InMemory unit tests if the specific query path being
  exercised doesn't also require a case-insensitive comparison in the same materialized query — verify
  this per test at implementation time; if the generator method mixes overflow-read and collation-based
  comparison in one query (likely, since both land in the same phase), that test moves to E2E too.
- **`dotnet build All.sln`** must be 0 warnings/0 errors after every task (repo-wide convention).
- **Full-solution `dotnet test All.sln`**, run fresh (never `--no-build` — this bit Phase 2's Task 5 and
  cost a full review-and-fix cycle; do not repeat that mistake), at the end of the implementation plan, to
  confirm no regressions against the current pre-existing-failure baseline (SqlOnFhir conformance drift
  ×2 TFMs, the DataLayer EF-InMemory-provider-limitation failures already documented in prior phases'
  plans) — expect the InMemory-limitation baseline itself to shift once affected composite tests are
  re-homed to E2E; document the new baseline explicitly rather than comparing against the stale one.

## Risks

| Risk | Mitigation |
|---|---|
| This phase's read-path changes make `EF.Functions.Collate` load-bearing for composite token/string queries, which EF InMemory cannot translate — most or all of `CompositeSearchParameterQueryGeneratorTests.cs`'s existing read-path assertions will throw, not fail, once the changes land | Explicitly planned, not discovered by surprise (see Testing strategy): affected existing tests are re-homed to `Ignixa.Api.E2ETests` alongside new characterization tests, before/as part of implementing the read-path changes, not after. |
| Extending case-insensitive collation to composite reads slows those queries (index-seek defeated by `COLLATE`) | Accepted — same tradeoff already made for single-token reads since commit `721f8ff2`; tracked as a follow-up to fix at the DDL level, not blocking this phase. |
| Six composite row generators each hand-roll slightly different 128-char split code — a mechanical "replace with `TokenCodeStorage`" pass could miss a subtle per-file difference | Read each file's current implementation individually before changing it (spec deliberately does not claim they're identical); each gets its own task-level verification against its own current behavior via characterization tests. |
| Composite string normalization convergence changes default-search behavior if `Text2`'s actual default collation differs from what this spec assumes | Confirm `97.sql`'s exact `Text2`/`TextOverflow2` collation declaration before implementing. The read path's default-search behavior is governed by the `TokenStringCompositeSearchParam` **table** column declaration (`Latin1_General_CI_AI`, `97.sql:909-910`) — this is what finding 3 cites and what the fold-is-redundant argument depends on. The TVP declaration (`_100_CI_AI_SC`, `97.sql:213-214`) only governs merge-time comparisons inside the stored procedure and is a separate fact; don't conflate the two when re-verifying. |
| `QuantityCodeRowGenerator` deletion breaks something the grep missed | Grep was repo-wide (not scoped to `src/`), and confirmed via direct read that `SqlMergeRepository` doesn't instantiate it; still, run the full build after deletion before committing — a compile error would surface any missed reference immediately. |
