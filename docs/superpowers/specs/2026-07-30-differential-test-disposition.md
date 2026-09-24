# Differential test disposition — Task 8.11

The precondition for deleting `Ignixa.DataLayer.SqlEntityFramework`. The ~50 facts under
`test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/` assert that the EF and SqlServer write and
search paths agree. They stop compiling the moment the EF project goes, and `DifferentialTestHarness`
hand-builds the entire EF stack against concrete classes, so there is no partial path: it is all or nothing.

Classified per `[Fact]` by reading each test body and then searching for a covering test that survives
deletion. Bucket (i) required **naming** the covering test; "probably covered by E2E" was not accepted.

## Totals

| Bucket | Count | Meaning |
|---|---|---|
| Harness self-tests | 9 | Assert `AssertEquivalent`/`AssertResourceContentEquivalent` against synthetic `RowStateSnapshot` data. Never touch either repository. No product fact, nothing to preserve. |
| Single-implementation by design | 2 | The two straddling-phase regression tests in `CompiledSearchSortPagingDifferentialTests` only ever invoke `NewSearchService`. Unaffected by deletion. |
| **(i) already covered** | **22** | A named SqlServer-only or E2E test asserts the same behaviour. |
| **(ii) convert** | **12** | Real standalone value on the new engine, not verified outside `Differential/`. |
| **(iii) lost as-is** | **5** | Nothing covers them today. Three are trivially convertible; two are worthless post-deletion. |

**Net: ~17 tests to convert to SqlServer-only assertions. Close to zero unavoidable loss.**

## Bucket (iii) — what a reviewer must weigh

1. **Hard delete's search-index sweep.** Nothing proves `HardDeleteResourceAsync` clears all 15 search-index
   tables. `SqlServerFhirRepositoryExpiryTests`' similarly-named test checks only `dbo.Resource` and
   `dbo.ResourceTtl` despite its name. This backs the TTL cleanup job. **Convertible.**
2. **`TokenNumberNumber` composite, row level.** Only a SQL-shape unit test exists; nothing executes it
   against real data. No E2E test uses `MolecularSequence`/`chromosome-window-coordinate`. **Convertible.**
3. **`code-value-date` (TokenDateTime) composite.** Zero coverage anywhere, at any level. **Convertible.**
4. **Composite `:missing=true` returning real rows.** Only a `:missing=false` plan-shape test exists, for a
   different composite type. **Convertible.**
5. **Compartment search's cross-type natural-ID collision safety.** A compartment query must not leak a
   same-natural-id resource of a *different* type. `CompartmentLoweringRuleTests` asserts the predicate's
   type but not that it filters on `ReferenceResourceTypeId`, and nothing seeds a colliding pair. This is a
   correctness/leakage property, and the most valuable single item in the table. **Convertible.**

Genuinely worthless after deletion, and no loss: the legacy `HardDeleteResourceAsync` bug characterization,
and the "identical to legacy" half of every parity assertion — those were migration-confidence checks, not
product behaviour.

## Bucket (ii) — convert to SqlServer-only

`dbo.Resource` RawResource content after create; the 15-table delete sweep; batch-write content equivalence;
composed create/update/batch/delete workflow; multi-type wildcard include refusal mapped to
`RequestNotValidException` at the service boundary; `PreconditionFailedException` on SQL error 50409;
missing-sort-key rows ordering **last** rather than first; `_sort=_id` ordering; offset paging across a page
boundary via `ContinuationToken`; untyped-reference narrowing at row level; plus the convertible (iii) items.

## The obstacle the plan did not anticipate

Task 8.11 assumed `RowStateSnapshot` was directly reusable as a golden file. It is not.

```csharp
public sealed record RowStateSnapshot(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows, string TableName);
```

Values are `object?` and `AssertEquivalent` compares with `object.Equals`. A JSON round-trip returns
`JsonElement`/`double`/`string` instead of `int`/`DateTimeOffset`/`bool`, so a frozen snapshot silently stops
matching. Fixing it means either a type-preserving converter or extending the existing `NormalizeValue`
stringification from `byte[]`/null to every value before comparison.

The second is preferable and worth doing regardless of golden files: today's comparison is latently fragile
for the same reason, and this codebase has consistently repaired silently-wrong comparison mechanisms rather
than routing around them.

## Lower-confidence classifications

- **Offset paging across a page boundary** — placed in (ii). `BasicSearchTests`' next-link traversal proves
  all resources appear exactly once, but with default sort and without exercising `ContinuationToken.Encode`.
  A reviewer could reasonably call that (i).
- **Multi-type wildcard include** — the `Lower`-level refusal is unit-tested; whether any E2E test asserts
  the 400 at the service boundary was not exhaustively checked.
- **Composed workflow** — judged not independently valuable since each operation is covered separately. A
  reviewer might want it kept as a smoke test.
