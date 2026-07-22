# Ignixa.Search.Sql README Gaps Design

## Goal

Implement every feature currently listed under `Ignixa.Search.Sql`'s "What's not implemented yet"
section while preserving the compiler's existing functional-core architecture:

1. System-qualified token matching, including `|code`.
2. Quantity system/code matching.
3. URI `:above` and `:below`.
4. The `:ap` comparator.
5. Absolute and external reference matching.
6. String `:contains` and boundary-width `:exact` across inline/overflow storage.

The work lands as four dependency-driven phases. Each phase is independently correct, tested, and
reviewable. Wiring the compiler into production search execution remains out of scope.

## Constraints

- `Resolve` remains the compiler's only I/O stage.
- `Lower` and `SqlBuilder` remain deterministic functions of explicit inputs.
- User-supplied values always become bound parameters.
- Unknown terminology values produce a valid empty match, not a client error or a broadened query.
- Unsupported cases continue to fail loudly.
- Existing SQL schema and row-generation conventions are reused; this work adds no migration.
- `ISymbolResolver` may change because the package is explicitly alpha. All implementations must be
  updated rather than receiving silent default behavior.

## Architecture

The implementation extends the existing pipeline rather than adding a parallel path:

```text
bound search IR
    |
    v
Resolve
  - collect search parameters, resource types, token systems, quantity codes
  - resolve all database surrogate IDs
    |
    v
SymbolTable
  - SearchParamId / ResourceTypeId
  - SystemId / QuantityCodeId, including known-missing results
    |
    v
Lower
  - build explicit predicate trees and CTEs
    |
    v
SqlBuilder
  - emit deterministic parameterized T-SQL
```

Terminology maps in `SymbolTable` must distinguish three states:

- a value was not collected, which is a compiler invariant violation and throws;
- a value resolved to an integer ID;
- a value was collected but does not exist in the lookup table, which lowers to an explicit false
  predicate and therefore an empty match.

This preserves the difference between an invalid compiler state and a valid search that happens to
match no indexed rows.

## Phase 1: Terminology Resolution and Qualified Values

### Resolve changes

`SymbolCollectingVisitor` collects:

- non-empty `TokenSearchValue.System` values;
- non-empty `QuantitySearchValue.System` values;
- non-empty `QuantitySearchValue.Code` values;
- the same values from every composite component.

Empty token systems are not looked up because `|code` means that the indexed row's `SystemId` must be
null. Empty quantity systems/codes mean "no constraint" and are also not looked up.

`ISymbolResolver` gains:

```csharp
Task<int?> GetSystemIdAsync(string system, CancellationToken cancellationToken);
Task<int?> GetQuantityCodeIdAsync(string code, CancellationToken cancellationToken);
```

`Resolve.RunAsync` deduplicates collected values, resolves each once, and stores both successful and
known-missing results in `SymbolTable`. The SQL Entity Framework implementation uses the same lookup
data that currently supplies the token and quantity row generators. Resolver I/O failures propagate;
they are not converted into empty matches.

### Predicate support

The predicate AST gains explicit shapes for:

- `IsNull(column)`;
- `False`.

`False` emits a deterministic false SQL fragment and is used only when a collected system or quantity
code has no database ID. It avoids magic sentinel IDs and makes the empty-result reason visible in plan
and SQL golden tests.

### Token behavior

| Query value | Lowered constraint |
|---|---|
| `code` | `Code = code`, with no system constraint |
| `|code` | `SystemId IS NULL AND Code = code` |
| `system|` | `SystemId = resolved(system)`, with no code constraint |
| `system|code` | `SystemId = resolved(system) AND Code = code` |
| unknown non-empty system | explicit false predicate |

System and code predicates apply to the same parameter-table row. Existing overflow handling for token
codes remains unchanged. A token with neither system nor code retains the existing unsupported
text-only-token behavior; this phase does not reinterpret display text as a code.

### Quantity behavior

The numeric comparator predicate is combined with each non-empty identity constraint:

- non-empty system: `SystemId = resolved(system)`;
- non-empty code: `QuantityCodeId = resolved(code)`;
- `value||code` constrains the code but does not require a null system;
- an unknown non-empty system or code produces an explicit false predicate.

The same helpers apply to every affected composite table:

- token-token;
- token-number-number;
- token-string;
- token-quantity;
- token-date;
- reference-token.

No composite may temporarily ignore a system or quantity code while leaf support is enabled.

## Phase 2: URI Hierarchy and Reference Base URIs

### URI hierarchy

URI matching follows the shipping backend's case-sensitive lexical-prefix behavior.

- `:below` means the indexed URI starts with the search value, including equality.
- `:above` means the indexed URI is a prefix of the search value, including equality.

`:below` reuses the existing escaped starts-with predicate. `:above` adds a dedicated
`PrefixOfParameter(column, parameter)` predicate rather than generating SQL text in the lowerer or using
the stored URI as an unescaped `LIKE` pattern. The emitted comparison remains case-sensitive and treats
stored `%`, `_`, `[`, and `\` characters as URI data, not SQL wildcard syntax.

This phase does not add URI path-segment boundary rules or canonical URL/version matching.

### Reference identity

Reference identity always includes `BaseUri`:

- a local/relative reference requires `BaseUri IS NULL`;
- an absolute/external reference requires binary exact equality on `BaseUri`;
- resource type and resource ID retain their existing constraints.

Adding the local null predicate fixes the current over-broad case where a relative search can match an
external reference with the same resource type and ID. External base A must not match external base B.
The same behavior applies to reference-token composites.

`ReferenceResourceVersion` remains outside the identity predicate, consistent with normal FHIR reference
search matching target identity rather than a specific history version.

## Phase 3: Complete String Matching Across Overflow Storage

The existing write path stores:

- values of at most the inline width in `Text`, with `TextOverflow = NULL`;
- longer values as a prefix in `Text` and the complete value in `TextOverflow`.

Lowering must query the complete logical value without changing that storage model.

For a `:contains` search value within the inline width:

```text
(TextOverflow IS NULL AND Text CONTAINS value)
OR TextOverflow CONTAINS value
```

This finds matches before, across, and after the inline boundary. For a longer search value, only
`TextOverflow` can match and remains the target column.

For `:exact` at or below the inline width:

```text
TextOverflow IS NULL AND Text = value
```

The null guard prevents a 256-character query from matching a longer value with the same stored prefix.
For a longer query, exact matching targets `TextOverflow`.

`:contains` retains the existing case-insensitive, accent-insensitive collation. `:exact` retains the
existing case-sensitive, accent-sensitive collation. All LIKE metacharacters remain escaped by
`SqlBuilder`.

## Phase 4: Approximate Comparators

The approximation tolerance is fixed at the FHIR-recommended 10 percent. It is not configurable in this
phase.

### Number and quantity

For search value `v`:

```text
tolerance = max(existing implied-precision tolerance, abs(v) * 0.10)
lower = v - tolerance
upper = v + tolerance
```

The match uses the same stored-range comparison model as equality, widened to `[lower, upper]`. Using
`abs(v)` keeps negative bounds ordered. The existing implied-precision tolerance gives zero and
near-zero values deterministic non-inverted behavior.

This shared logic applies to number and quantity leaves and to numeric/quantity composite components.
Quantity system/code constraints from Phase 1 remain conjunctive with the approximate range.

### Date and `_lastUpdated`

Date approximation receives an explicit reference time:

```text
midpoint = Start + ((End - Start) / 2)
tolerance = abs(referenceTime - midpoint) * 0.10
approximateRange = [Start - tolerance, End + tolerance]
```

The widened range uses the existing date overlap semantics. This preserves the interval represented by
a partial date before widening it.

`Lower.Run` accepts the reference time explicitly for approximate date lowering. Direct callers must
supply it when compiling a date `:ap`; ordinary searches remain unaffected. `SearchCompiler.CompileAsync`
accepts an optional `TimeProvider`, captures `GetUtcNow()` once at its imperative boundary, and passes
that value through, so every predicate in one compilation sees the same instant. The default is
`TimeProvider.System`; tests supply a fixed provider.

The shared date comparison applies to date leaves, token-date composites, and `_lastUpdated`.

## Error Handling

- Missing SearchParamId or ResourceTypeId remains a Resolve-stage failure.
- Unknown SystemId or QuantityCodeId is a valid no-match condition represented by `False`.
- Resolver exceptions propagate unchanged.
- Malformed FHIR search syntax remains the parser/binder's responsibility.
- Recognized modifiers or comparators outside the supported matrix continue to throw
  `NotSupportedException`; no lowerer may ignore one.
- AST and symbol-table misses throw as programmer/invariant errors.

## Testing

Every phase adds exact `Explain()` and emitted-SQL assertions, not loose substring checks.

### Phase 1

- collector deduplication across leaves and composites;
- successful and known-missing terminology resolution;
- invariant failure for an uncollected terminology lookup;
- bare token, `|code`, `system|`, `system|code`, and unknown system;
- quantity with no identity constraint, system only, code only, both, and unknown values;
- every affected composite slot;
- resolver implementation and test fakes.

### Phase 2

- URI equality, ancestor, descendant, unrelated, case variant, near prefix, and SQL wildcard characters;
- local, own-base absolute, and two distinct external bases sharing type/ID;
- reference-token composite parity;
- proof that local references cannot match external rows.

### Phase 3

- string lengths 255, 256, and 257;
- contains matches before, across, and after the inline boundary;
- exact-width prefix collision;
- case and accent behavior;
- escaped `%`, `_`, `[`, and `\`.

### Phase 4

- positive, negative, zero, and precision-sensitive numeric values;
- quantity identity constraints combined with `:ap`;
- fixed-clock past, future, instant, and partial-date searches;
- date leaf, token-date composite, and `_lastUpdated` parity.

Each phase runs the focused `Ignixa.Search.Sql` and affected resolver/generator tests, followed by the
solution's non-E2E test suite. Live SQL Server execution remains outside CI, matching the package's
current alpha constraints.

## Documentation and Completion Criteria

After each phase, the README support matrix and examples are updated. The implementation is complete
when:

- all six "not implemented" bullets are removed or moved to the supported matrix;
- no corresponding lowering rule retains a deliberate throw;
- all leaf and composite paths preserve every supplied constraint;
- plan and SQL output remain deterministic and injection-safe;
- the package and non-E2E solution tests pass with zero build warnings.

## Consequences

- `ISymbolResolver` and `SymbolTable` gain terminology identity responsibilities, but I/O remains isolated
  in Resolve.
- The predicate AST becomes slightly richer while remaining table-local and renderer-owned.
- Unknown terminology values become inspectable empty plans instead of exceptions or magic IDs.
- Local reference matching becomes stricter and fixes an existing false-positive path.
- Approximate date compilation has one new explicit environmental input, preserving deterministic Lower
  behavior.
- Production adoption, canonical URI version matching, reference version matching, and configurable
  approximation policies remain future work.
