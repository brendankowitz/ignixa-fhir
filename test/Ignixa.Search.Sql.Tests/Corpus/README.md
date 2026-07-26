# Legacy SQL differential corpus

185 real FHIR searches, each paired with the SQL the **shipping** search engine actually executed for
it, captured during a TestScript conformance run. The suite compiles each URL with
`Ignixa.Search.Sql` and compares what the two engines ask the database for.

The point is **triage, not parity**. The shipping engine is a reference, not an oracle: a divergence
is a question — a feature the compiler lacks, a table read the compiler avoids, or a filter the
shipping engine applies for a reason worth understanding — and some divergences are wins we should
keep.

Six things are asserted: every captured legacy query still parses, no fewer queries compile than
`DifferentialBaseline.CompiledQueries`, and the verdict distribution stays within the four one-sided
guards in `DivergenceBaseline` (`Match >= 75`, `CompilerDoesLess <= 37`, `CompilerDoesMore <= 14`,
`Divergent <= 59`). The compile count alone was saturated — all 185 compiled before the guards
existed — so it could not detect a regression that kept a query compiling while changing what it
asked the database for. Note the four counts sum to exactly 185, which leaves the feasible region a
single point: any one query moving between verdicts fails at least two of the guards.

## Running it

```bash
dotnet test test/Ignixa.Search.Sql.Tests --filter "FullyQualifiedName~Corpus"
```

The report lands at `legacy-sql-differential-report.md` in the test output directory. A snapshot for
review lives in `reports/differential-report.md`.

## Refreshing the corpus

```bash
python Corpus/tools/extract-corpus.py <ignixa-sql-capture-artifact-dir>
```

The capture correlates SQL to requests by timestamp window, so a request can pick up a neighbour's
SQL. Every SQL event of a request carries the whole batch text, so the request's own query appears on
several events while a leaked neighbour appears on one — the extractor keeps the modal text per URL
and drops entries with fewer than two corroborating events.

## What comparison ignores, and why

Byte comparison is meaningless here: the two dialects express the same set algebra with different
syntax, and the compiler's result contract differs from the shipping engine's by design. Comparison
therefore runs on whole-query multisets of *tables read* and *semantic filters applied*, with these
deliberately erased:

| Erased | Why |
|---|---|
| Row hydration (`JOIN dbo.Resource` + `IsHistory`/`IsDeleted` in the terminal SELECT) | The compiler returns identity columns and leaves the fetch to its caller. Resource-column filters in that same SELECT (`_id`, `_type`, `_lastUpdated`) still count. |
| CTE boundaries | The shipping engine folds an intersection into the next source CTE as a correlated `EXISTS`; the compiler emits a separate `INNER JOIN` CTE. Same semantics. |
| Subquery vs CTE placement (`sub:` marker) | `NOT IN (SELECT ...)` versus a dedicated except CTE — same tables, same filters. |
| Literal values, and parameter-versus-literal | The captured ids come from a live catalog that no longer exists, so both sides' integers are erased. Whether a value is bound or inlined is parameterization policy. |
| Column-to-column comparisons | Row correlation plumbing, which the two dialects do differently for identical semantics. |
| Set operators | Reported, never verdict-deciding: `EXISTS` vs `INNER JOIN`, `NOT IN` vs `NOT EXISTS`. |

## Known divergences worth keeping

- **`Patient/$everything`** (3 captured queries). The corpus compiles these as the real operation — it
  builds a `PatientEverythingExpression` from the URL and lowers it through the compartment traversal
  (see `CorpusCompiler`), rather than stripping the `$everything` segment and compiling a bare
  `GET /Patient?…`. All three land as **Divergent**, and the cause is semantic, not merely a different
  shape. The shipping engine's paged `$everything` reads `dbo.ReferenceSearchParam` **exactly twice**, and
  both reads follow the seed patient's **outbound** references (`refSource.ResourceTypeId IN (Patient)`,
  joined through `dbo.Resource` to materialize each target) — referenced-resource inclusion, the resources
  the patient points to. The compiler instead emits the **inbound** compartment-membership traversal — one
  `dbo.ReferenceSearchParam` read per Patient-compartment membership parameter (many in real R4), matching
  resources that point *at* the patient. So the divergence is opposite graph direction plus the
  paging/hydration machinery, not a windowed-vs-unwound batching of the same membership reads. (The
  capture's opaque `SearchParamId`s can't be name-mapped, so *which* two patient reference parameters the
  engine expanded isn't verifiable; the outbound direction is.) This is why
  `DivergenceBaseline.DivergingQueries` was raised from 56 to 59 when the harness was wired; per the
  convention below, the reason is recorded rather than the count being suppressed. An earlier note here
  claimed "same semantics, different shape"; reading the captured SQL showed that was wrong, and it has
  been corrected.

  A second clause of that note is now obsolete: the compiler no longer "emits no referenced-resource union
  at all". `StructuralContext.LowerPatientEverything` emits a `ReferencedTypeExpansion` — the outbound
  Practitioner/Organization/Location/Medication follow, seeded from the filtered compartment set — so the
  two engines now agree on that half, and the compiled shape for all three entries changed accordingly.
  The four guarded counts did **not** move (still 75/37/14/59): a verdict is categorical, all three
  entries were already `Divergent` for several independent reasons, and closing one of them cannot flip a
  query out of that bucket. The unclosed remainder is the inbound compartment traversal the engine's
  capture does not contain, plus paging.

  One harness limitation is worth knowing when reading these three entries: `_since=3000` is not a
  parseable instant, so `CorpusCompiler` leaves `SinceDate` unset (by its own documented choice) and no
  `VisibleSinceFilter` reaches the compiled shape. The `_since` path is covered by the unit suite, not
  here.

## What it can't tell you

- **Search-parameter identity.** Legacy ids are opaque integers with no name map in the capture, so
  "both sides filter *a* SearchParamId" is verifiable; "both sides filter *the same* parameter" is
  not. Unifying the ids across the corpus would fix this and has not been done.
- **Row-level correctness.** Nothing executes. Two shapes that read the same tables with the same
  filters can still return different rows.
- **Deep coverage.** The corpus is broad but shallow: heavy on `_tag`-scoped and plain-parameter
  searches, with only two chained queries, one `_has`, and one `_sort`. It is a smoke net over real
  traffic, not a replacement for the per-rule unit matrix.
