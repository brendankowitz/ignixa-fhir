# Ignixa.Search.Sql gap closure — design

**Status:** approved in outline, ready for plan-level review
**Date:** 2026-07-25
**Branch:** `worktree-ignixa-datalayer-sqlserver` (tip `1e564c6f`, rebased onto current `origin/main`, pushed)

## Context

Phase E cut the search path over from the legacy EF engine to `SqlServerCompiledSearchService`, which drives the `Ignixa.Search.Sql` compiler directly. That cutover left 32 of 620 E2E tests failing. Until recently those failures were masked: a serializer defect turned them into truncated HTTP 200s, so they were undiagnosable. That defect is fixed, failures now surface as clean status-coded `OperationOutcome` responses, and a full root-cause analysis exists at `.superpowers/sdd/e2e-gap-analysis.md` — measured twice against fresh databases, with the same 32 tests failing byte-for-byte both times.

This phase closes 30 of those 32.

**This is not one feature.** It is six independent defects that share a test suite. There is no common mechanism to build, so the work decomposes by group, each group landing separately with its own evidence, and the phase can stop cleanly at any point without leaving half-built state.

## Scope

**In scope — six groups, 30 failures:**

| Group | Count | Nature |
|---|---|---|
| `identifier:of-type` lowering | 13 | Missing compiler feature |
| Date/precision oracle | 11 | Stale tests; compiler is spec-correct |
| URI `:below`/`:above` separator | 1 | Compiler bug |
| Single-value `:not` | 2 | Compiler bug |
| `:count` with `_include`/`_revinclude` | 2 | Compiler bug |
| System-level `_type` filter | 1 | Compiler bug |

**Out of scope, deliberately:**

- **The two architectural guards** (`ChainingSearchTests` reverse-chain-multi-target; `SortTests` `_lastUpdated` datetime-sort). These are not failures — the compiler deliberately refuses them. Cross-type `_has` chains need a concrete target type by construction, and `_lastUpdated` is a point column with no partial-precision comparator formula. Both need genuine architectural design, and folding them in would make this two projects wearing one name.
- **Porting upstream's number/quantity comparator fixes into the compiler.** Upstream commit `c054f8d9` improved range-comparator correctness in `Ignixa.DataLayer.SqlEntityFramework/Search/*` — the legacy engine this branch's cutover bypasses. Those fixes therefore do not reach our search path. This is a real latent divergence, but no failing test demonstrates it, and speculative porting without a reproduction belongs in its own investigation.

## The date/precision group — why this is test work, not compiler work

FHIR R4 defines the `eq` prefix for dates as: *"the range of the search value fully contains the range of the target value"* (verified against `hl7.org/fhir/R4/search.html` this session). Full containment, one-directional.

The compiler implements exactly that. The failing tests expect **bidirectional overlap** — e.g. a month-level search (`2013-01`) matching a year-only stored value (`2013`), which containment correctly rejects because January does not contain all of 2013. That expectation is inherited from the legacy EF engine the tests were originally written against.

So the compiler is right and the oracle is stale.

**The binding requirement for this group: each of the 11 is verified individually against the spec, with the reasoning recorded per case** — the search range, the target range, and which direction containment runs. A blanket "update the oracle" pass would close all 11 and silently bury any genuine compiler bug sitting inside the group. Where a case turns out to be a real compiler defect rather than a stale expectation, it is fixed as a compiler change and called out as such.

This group must also state plainly that it is a **behaviour change** for any client relying on the legacy overlap matching, even though the new behaviour is the spec-conformant one.

## The five compiler-fix groups

**`identifier:of-type` (13).** The largest group but a single coherent gap. The supporting pieces already exist: `FieldName` declares `IdentifierTypeSystem` and `IdentifierTypeCode`; `TokenSearchParam.sql` declares the matching `IdentifierTypeCode NVARCHAR(256) NULL` and `IdentifierTypeSystemId INT NULL` columns; and `SqlCatalog` is a partial class whose `BuildFromDdl()` is source-generated from that DDL by `Ignixa.Search.Sql.Generators`. What is missing is the lowering. `TokenTextLoweringRule` — added upstream for `:text`, lowering a `StringExpression` over `FieldName.TokenText` — is the working model for the same shape.

*Planning must confirm* that the generated catalog genuinely exposes the two extension columns before assuming no catalog work is needed. The generator reads the DDL and the DDL has them, so this is expected, but it is an assumption rather than a verified fact.

Note these are nullable extension columns populated by `PostMergeExtensionUpdater` *after* `MergeResources` commits (see CLAUDE.md). Rows written before that updater ran, or where it failed, carry NULL. Lowering must behave sanely against NULLs rather than assuming population.

**URI `:below`/`:above` separator (1).** The implementation hardcodes `/` as the hierarchy separator, which breaks `urn:oid:` and `urn:uuid:` style values entirely. Fully root-caused and isolated.

**Single-value `:not` (2).** `:not` on `_id`/`_type` works for multi-value lists but throws for the single-value scalar case. The multi-value path already proves the SQL shape, so this is a narrow lowering fix rather than new capability.

**`:count` with includes (2).** `_count=1` combined with `_include`/`_revinclude` leaks the next page's included resource into the current page's count — a scoping bug. One fix likely closes both tests.

**System-level `_type` filter (1).** A bare system-level `_type=Patient` does not filter at all, returning all tagged resources. The only test exercising this exact path, so coverage here is thin and worth strengthening alongside the fix.

## Ordering

1. **URI separator**, then **single-value `:not`** — both isolated and fully root-caused. They establish the working rhythm cheaply and confirm the test/verification loop before anything larger.
2. **`identifier:of-type`** — largest, but one coherent gap with an existing rule to model on.
3. **`:count` scoping**, then **system-level `_type`**.
4. **Date/precision oracle last.** It is the group most likely to grow: if individual verification surfaces genuine compiler bugs, that is new work discovered late, and it is better discovered against a suite that is otherwise already improving.

## Testing

Each group closes with its own E2E tests passing, plus unit coverage at the compiler level where the fix is a lowering change — E2E alone proves the outcome but not the shape of the emitted SQL.

The full suite is re-measured after each group rather than only at the end, so any regression is attributable to a single change. Current baselines: `dotnet build All.sln` 0 warnings / 0 errors; `Ignixa.Search.Sql.Tests` 646/646 on both net9.0 and net10.0; `Ignixa.Application.Tests` 1118 passed / 1 skipped; `Ignixa.Api.Tests` 135/135; E2E 620 total / 568 passed / 32 failed / 20 skipped.

**Known flake:** `ChainingAndSortTests.GivenAChainedSearchPattern...` failed once with a count off by one (12 vs 13) and did not reproduce in a second full run or in isolation. Plausibly a `PostMergeExtensionUpdater` backfill race, consistent with the repo's documented transaction model. It is not one of the 32 and not in scope, but anyone measuring should know it exists rather than mistaking it for a regression they caused.

## Risks

- **The date group may not be purely test work.** Its 11 failures are grouped by symptom, not by cause. The individual-verification requirement exists precisely because a blanket pass would hide a real defect.
- **`identifier:of-type` depends on an unverified assumption** about the generated catalog exposing the extension columns. If wrong, that group grows to include generator or catalog work.
- **Extension columns are nullable by design.** Lowering that assumes population will behave incorrectly against rows written before or without `PostMergeExtensionUpdater`.
- **The date fix changes observable behaviour** for clients depending on legacy overlap matching, in the direction of spec conformance.
- **Thin existing coverage** on the system-level `_type` path — a single test — means the fix has little to hold it in place unless coverage is added with it.
