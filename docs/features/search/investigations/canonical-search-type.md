# Investigation: Canonical Search Type in Ignixa.Search.Sql

**Feature**: search
**Status**: In Progress
**Created**: 2026-08-20

## Problem Statement

A FHIR `canonical` reference is `url[|version][#fragment]` — `http://example.org/Questionnaire/intake|1.0#section`.
Ignixa splits those three components apart **on the index side** and stores them in
`UriSearchParam(Uri, Version, Fragment)`, but the query side never splits them, so the version and
fragment columns are written and never read. Every canonical search silently degrades to a whole-string
URI comparison that cannot match:

```
GET /Observation?_profile=http://ignixa.io/tests/StructureDefinition/lab|1
  index row:  Uri = 'http://…/lab'   Version = '1'
  query:      Uri = 'http://…/lab|1' Version = (never compared)
  result:     no match, HTTP 200, empty bundle — a silently wrong answer
```

The repo already records this as accepted-unknown behaviour: every match assertion in
`src/Core/Ignixa.TestScript.Suites/testscripts/Search/canonical.json` is `warningOnly`, described as
"canonical version/fragment stripping … is implementation-defined".

That is only half the problem, and the easier half. The harder half is **canonical resolution** —
`QuestionnaireResponse?_include=QuestionnaireResponse:questionnaire`, `_revinclude` in the other
direction, and `questionnaire.title=…` chaining. A `canonical` carries no resource id, so there is nothing
for the existing include machinery to join on. The spec defines resolution as a *search on the target's
own identity elements* (R5 §2.1.3.0.6):

> Resolving this pipe ('\|') syntax is equivalent to using a GET with the version parameter:
> `GET fhir/ValueSet?url=http://hl7.org/fhir/ValueSet/my-valueset&version=0.8`

So a canonical include is a **value join across two different search-parameter indexes**: the source
resource's canonical (`UriSearchParam.Uri`/`.Version`) against the target resource's `url` parameter
(`UriSearchParam.Uri`) *and* its `version` parameter (`TokenSearchParam.Code`). Every other include in the
compiler is an id join through one table. This is the design's real centre of gravity, and it is developed
in [The canonical resolution join](#the-canonical-resolution-join) below.

`Ignixa.Search.Sql` is alpha and not yet wired into a production data layer, so it is the right place to
implement canonical search **as a first-class leaf type** rather than inheriting the defect.

## Current State — traced end-to-end

| Stage | File | Behaviour |
|---|---|---|
| Index conversion | `Ignixa.Search/Indexing/Converters/CanonicalToUriSearchValueConverter.cs:39` | `new UriSearchValue(v, separateCanonicalComponents: true)` — **splits correctly** |
| Value model | `Ignixa.Search/Indexing/SearchValues/UriSearchValue.cs:26,110` | regex `(?<url>…)(?<version>\|…)?(?<fragment>#…)?`; STU3 short-circuits to no-split |
| Index write (TVP) | `SqlEntityFramework/RowGenerators/UriSearchParameterRowGenerator.cs:30-36` | TVP carries **4 columns only** — `Version`/`Fragment` excluded |
| Index write (post-merge) | `SqlEntityFramework/PostMergeExtensionUpdater.cs:120-127` | `UPDATE … WHERE … AND Uri = @Uri` fills `Version`/`Fragment` |
| **Query parse** | `Ignixa.Search/Expressions/Parsers/SearchAtomicValueParser.cs:38` | **`separateCanonicalComponents: false`** ← the defect |
| Query expression | `Ignixa.Search/Expressions/Parsers/SearchValueExpressionBuilderHelper.cs:230-251` | `BuildCanonicalExpression` emits `Uri ∧ UriVersion ∧ UriFragment` — **dead code**, the components are always null |
| Legacy SQL | `SqlEntityFramework/Search/SearchParameterQueryGenerator.cs:2369-2399` | `GenerateUriVersionQuery` / `GenerateUriFragmentQuery` exist and are reachable only from the dead branch |
| **New compiler** | `Ignixa.Search.Sql/Lowering/Leaf/UriLoweringRule.cs:9-13` | doc comment states "Version/Fragment are not on this table" — **stale**; the DDL has both |
| **Includes / chains** | `Ignixa.Search.Sql/Builders/IncludeEmitter.cs:46,65-69`, `Lowering/ChainLoweringRule.cs` | join `ReferenceSearchParam` by id with `rsp.BaseUri IS NULL` — a table canonicals never populate, so a canonical include emits valid SQL that returns nothing |

Four defects fall out of that trace, in dependency order:

1. **Query-side split never happens** (`SearchAtomicValueParser.cs:38`). Root cause of the empty bundle.
2. **Cross-row component smearing.** The legacy expression is an `Expression.And` of three independent
   `StringExpression`s, each of which becomes its own surrogate-id set that is then intersected
   (`SearchParameterQueryGenerator`). A resource with `meta.profile = ['A|1', 'B|2']` therefore matches
   `?_profile=A|2` — `Uri='A'` comes from one row, `Version='2'` from another. Component predicates for
   one canonical **must be conjoined within a single row**.
3. **Storage identity excludes the version.** `dbo.UriSearchParamList` is keyed
   `PRIMARY KEY (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri)`
   (`SqlEntityFramework/Resources/97.sql:245-249`), while `UriSearchParameterRowGenerator` emits one row
   per search-index entry with no de-duplication. A resource carrying `X|1` **and** `X|2` produces two
   rows with an identical key.
4. **Post-merge update aliases versions.** `PostMergeExtensionUpdater` matches rows on `Uri` alone, so if
   two same-URL rows ever did survive, both would be stamped with whichever version was written last.

Defects 3 and 4 are storage-layer, not compiler; the compiler cannot return correct results over an index
that cannot represent two versions of one canonical on one resource.

## Specification Requirements (verified)

All quotes fetched from the published spec, not recalled.

**Search type.** R5 §3.2.1.6.1.5 *Canonical Identifiers*: "When searching canonical references, the search
type is **reference**, though with an additional syntax for version information."
R5 §3.2.1.5.11 *reference* lists `[parameter]=[url]|[version]`<sup>TU</sup> — "where the search element is
a canonical reference, the \[url\] is an absolute URL, and a specific version **or partial version** is
desired."

**Version matching.** R5 §3.2.1.6.2 *References and Versions*:

> For canonical references, servers **SHOULD** support searching by Canonical URLs, and **SHOULD** support
> automatically detecting a `|[version]` portion as part of the search parameter and interpreting that
> portion as a search on the business version of the target resource. The modifier `:below` is used with
> canonical references, to control whether the version is considered in the search.

with worked examples: `questionnaire:below=<url>` matches `|1.0`, `|1.1`, `|2.0`; `questionnaire:below=<url>|1`
matches "Questionnaires with a **major version of '1'**" — `|1.0`, `|1.1` but not `|2.0`.
R4 states the same SHOULD under `search.html#uri`; it is already quoted verbatim in
`CanonicalToUriSearchValueConverter.cs:25-31`.

**Escaping.** R5 §3.2.1.5.5.2.2: "any vertical pipe (`|`) characters that are part of the URL must be
escaped (`%7C`) — the character is used as the separator between the URL and version components."
An unescaped `|` in a canonical search value is therefore **unambiguously** the version separator.

**`:above` / `:below`.** R5 §3.2.1.5.5.1.2 and §3.2.1.5.5.2.2: against a canonical reference these are "a
version search … performed as a 'greater than' / 'less than' against the version-scheme defined by the
resource", and "only allowed if the version scheme for the resource is known … Version-related search
criteria against resources with unknown versioning schemes **SHALL** be either ignored or rejected"<sup>TU</sup>.
⚠️ This conflicts with the §3.2.1.6.2 example, which describes `:below` as a **major-version prefix**, not
an ordering comparison. The prefix reading is the one the spec actually demonstrates.

**Not supported on canonicals.** R5 §3.2.1.5.11: "The `:identifier` modifier is not supported on canonical
elements since they do not have an identifier separate from the reference itself."

⚠️ **Correction to an earlier reading of this investigation.** The following sentence of §3.2.1.5.11 —
"nor are chaining, includes or reverse includes supported for reference elements that do not have a
`reference` element" — is about `Reference` datatype instances carrying only `identifier`/`display`, in a
paragraph entirely about `Reference.identifier`. It does **not** exclude `canonical`. R5 §2.1.3.0.6 and
§2.1.3.0.7 describe canonical resolution at length and expect servers to perform it. Chaining, `_include`
and `_revinclude` on canonical parameters are **in scope**.

**Resolution.** R5 §2.1.3.0.6 *Canonical URLs*:

> Resolving this pipe ('\|') syntax is equivalent to using a GET with the version parameter:
> `GET fhir/ValueSet?url=http://hl7.org/fhir/ValueSet/my-valueset&version=0.8`
> … (The canonical URL full format is `{{CanonicalResource.url}}|{{CanonicalResource.version}}`)
>
> Note that if a canonical URL reference does not have a version, and the server finds multiple versions
> for the value set, the system using the reference **should pick the latest version** of the target
> resource and use that. … Additional notes about searching on versioned references to canonical URLs:
> - Search only regards the latest version for each different logical resource id
> - This search only works for specific data elements of type of uri that act as canonical URL's
> - If there is no match (either because .version is empty or is different), the instance will not be
>   matched, and will not appear in the result bundle
> - If there are multiple matches (because the version is missing, or incomplete) then it is a matter of
>   policy to decide how to resolve this

R5 §2.1.3.0.7 *Choosing the right Canonical Reference* enumerates the multiple-match cases — no version;
**a partial version, where `…|1.2` matches both `1.2.1` and `1.2.3-draft`**; and a duplicated full version
(an editorial error). "In general, the correct version to use is the latest version approved for
production use. **This specification does not define the algorithm** for servers to use to determine the
latest version." Resources SHOULD declare `versionAlgorithm[x]`; absent it, servers MAY use their own
default logic.

⚠️ Note the tension this creates with the leaf-matching rules above: **partial-version prefix matching
applies to *resolution* without any modifier**, whereas leaf *matching* of a stored canonical string is
exact unless `:below` is used. The two are different operations on different data and must not be
conflated — see [The canonical resolution join](#the-canonical-resolution-join).

**Fragments.** R5 §2.1.3.0.8: a canonical fragment (`…/Questionnaire/example|1.0#vs1`) references a
**contained resource inside the target**. For resolution and `_include`, the fragment therefore selects a
part *within* the resolved container; the container is what gets resolved and included.

**Version landscape.** `canonical` does not exist in STU3 — `|` and `#` carry no meaning and the value is
an opaque URI (already handled at `UriSearchValue.cs:110`). `_profile` is declared `uri` in R4/R4B and
`reference` in R5/R6. In Ignixa this distinction is *inert*: `ElementSearchIndexer.InferSearchParamTypeFromFhirType`
maps `"canonical" => SearchParamType.Uri` (`ElementSearchIndexer.cs:538`) and the converter registry has no
`(canonical, ReferenceSearchValue)` entry, so canonical elements land in `UriSearchParam` under **either**
declared type. One lowering rule covers all versions.

**The declared metadata does not distinguish canonical from Reference.** `QuestionnaireResponse.questionnaire`
is `searchParamType: SearchParamType.Reference, targetResourceTypes: ["Questionnaire"]` — *byte-identical*
in STU3, R4 and R5 (`STU3SearchParameterDefinitions.g.cs:11115`, `R4…:12655`, `R5…:11170`). What changed is
the **element** type: `Reference(Questionnaire)` in STU3, `canonical(Questionnaire)` in R4+. So the same
search parameter is stored in `ReferenceSearchParam` on STU3 and in `UriSearchParam` on R4+, with nothing
in `SearchParameterInfo` to say which. Target-side identity also moves: R4 has per-resource
`Questionnaire-url` (uri) / `Questionnaire-version` (token); R5 consolidates them into
`CanonicalResource-url` / `CanonicalResource-version` across 34 base types.

## Constraints

1. **Byte-identical SQL / golden tests.** `Ast/EmitTests.cs`, `EndToEndCompilationTests.cs`,
   `Ast/EmitSqlGrammarTests.cs` and `Corpus/legacy-sql-corpus.json` pin emitted text. Existing plain-`uri`
   output must not move; canonical must be additive.
2. **`SqlCatalog` is generated from the real DDL.** `UriSearchParam.Version`/`.Fragment` are already
   available to the compiler with no catalog change (`SqlCatalogGenerator` reads
   `Ignixa.DataLayer.SqlServer.Database/Tables/*.sql`). Only the stale doc comment in `UriLoweringRule`
   needs correcting.
3. **`MergeResources` procs and TVP schemas are off-limits** (repo guidance). Any change to the write path
   must be additive — a new versioned type/proc — or the design must accept the current row identity.
4. **Fail loud, never silently wrong** (`Ignixa.Search.Sql/README.md`). An unsupported canonical shape
   throws with an actionable message; it never degrades to a query that returns the wrong rows.
5. **Leaf rules are isolated.** A `LeafContext` rule sees only symbol lookups and parameterization; it
   cannot reach the CTE graph. Canonical must fit that shape (it does — it is a single-table predicate).

## Approach

Introduce canonical as a **distinct search-value type with its own leaf lowering rule**, mirroring the
pattern the project already uses for every other value type.

### P0 — `CanonicalSearchValue` and the query-side split

Add `CanonicalSearchValue(Url, Version, Fragment) : ISearchValue` to
`Ignixa.Search/Indexing/SearchValues/`. `UriSearchValue` keeps its current shape and stays the plain-URI
type; the canonical concept stops being a nullable-field mode on it.

`SearchAtomicValueParser` produces a `CanonicalSearchValue` when, for a `Uri`- or `Reference`-typed
parameter on a non-STU3 model, the raw value contains an unescaped `|` or a `#`. Per R5 §3.2.1.5.5.2.2 a
literal `|` inside the URL must arrive as `%7C`, so shape dispatch is spec-sanctioned and needs no new
search-parameter metadata. A bare URL keeps producing a `UriSearchValue`, preserving today's behaviour and
the golden tests.

### P1 — `CanonicalLoweringRule` in `Lowering/Leaf/`

One `CteDefinition.ParamSource` over `UriSearchParam`, one conjunctive `Predicate` — so all components are
correlated **within a row**, fixing defect 2 by construction:

| Query | Predicate |
|---|---|
| `?p=U` (bare URL) | `Uri = @u` — matches any version/fragment (spec: bare URL is the superset) |
| `?p=U\|V` | `Uri = @u AND Version = @v` |
| `?p=U\|V#F` | `Uri = @u AND Version = @v AND Fragment = @f` |
| `?p=U#F` | `Uri = @u AND Fragment = @f` |
| `?p:below=U` | `Uri = @u` — version explicitly not considered |
| `?p:below=U\|V` | `Uri = @u AND (Version = @v OR Version LIKE @v + '.%')` |
| `?p:above=…` | reject — 400, unknown version scheme (see Open Questions) |

The `:below` version match is **separator-aware on `.`**, so `|1` matches `1.0` and `1.10` but not `10.0` —
the same segment-boundary reasoning `UriLoweringRule` already applies to URL paths. Both branches stay
sargable against a `(SearchParamId, Uri, Version)` key; no `LEFT()`/function wrapping on `Version`.

Register `CanonicalSearchValue c => CanonicalLoweringRule.Lower(…)` in `LeafLoweringDispatcher.LowerCore`.
`UriLoweringRule` is untouched apart from its stale comment, so plain-`uri` `:above`/`:below` path-prefix
semantics and their golden SQL are unchanged — the two `:below` meanings never collide because they are
selected by value type, not by sniffing the string inside one rule.

### P2 — Storage: what changes, and what does not

**No new information has to be extracted or stored.** Every value the design needs is already indexed
today:

| Needed for | Value | Already stored? |
|---|---|---|
| Leaf matching | source canonical's url / version / fragment | ✅ `UriSearchParam.Uri` / `.Version` / `.Fragment` |
| Resolution join | target's canonical URL | ✅ `UriSearchParam.Uri` under the `url` parameter |
| Resolution join | target's business version | ✅ `TokenSearchParam.Code` under the `version` parameter |

So there is **no new table, no new column, and no content reindex** in the base design. What does change is
row *identity*, column *types*, and index *coverage* — three defects in how the existing data is stored.

#### 1. Row identity excludes the version (correctness, blocking)

`dbo.UriSearchParam` itself is fine: `IXC_UriSearchParam` is a **non-unique** clustered index and the table
has no primary key, so it can physically hold `X|1` and `X|2` as two rows. The blocker is the write path:

```sql
CREATE TYPE dbo.UriSearchParamList AS TABLE (
    …, Uri VARCHAR (256) … NOT NULL
    PRIMARY KEY (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri));   -- 97.sql:245-249
```

`UriSearchParameterRowGenerator` emits one record per search-index entry with no de-duplication, so a
resource whose `meta.profile` (or `PlanDefinition.library`, etc.) carries the same URL at two versions
produces two records with an identical TVP key. ⚠️ Which failure follows — a PK violation that fails the
whole merge, or a silent collapse — needs an integration test to establish; either way the second version
is unrepresentable. `PostMergeExtensionUpdater` then compounds it by matching rows on `Uri` alone
(`PostMergeExtensionUpdater.cs:127`), stamping every same-URL row with the last version written.

Three ways out, in preference order:

1. **Carry `Version`/`Fragment` in a new TVP** (`UriSearchParamListV2`, keyed on `…, Uri, Version, Fragment`)
   with a new merge entry point. Additive, so constraint 3 holds; and because the columns arrive with the
   core insert, the post-merge update — and defect 4 with it — disappears entirely, along with the
   visibility window in which a row exists with a null version.
2. **Keep the extension-update shape** but correlate the `UPDATE` on a deterministic row ordinal instead of
   `Uri`. Smaller change, keeps the two-phase write and its failure mode.
3. **Accept the limitation**: one version per (resource, parameter, url), documented. Adequate for
   `meta.profile` in practice; wrong for artefacts that legitimately reference several versions of one URL.

Note that option 1 or 2 means previously-lost rows only reappear after a **reindex** — the migration fixes
the schema, not the history.

#### 2. Column types and collation (correctness *and* a hard join error)

| Column | Today | Target's counterpart | Problem |
|---|---|---|---|
| `UriSearchParam.Uri` | `VARCHAR(256) COLLATE Latin1_General_100_CS_AS` | same column, `url` param | ✅ aligned — the URL half of the join is sound |
| `UriSearchParam.Version` | `NVARCHAR(64)`, **no collation declared** | `TokenSearchParam.Code` `VARCHAR(256) COLLATE Latin1_General_100_CS_AS` | type *and* collation mismatch |
| `UriSearchParam.Fragment` | `NVARCHAR(128)`, no collation declared | n/a (source-side only) | case-insensitivity only |

`UriSearchParamEntity` documents both columns as "Case-sensitive" (`UriSearchParamEntity.cs:52,61`), but
the DDL never says so — they inherit the database default collation, which on a stock install is
case-**in**sensitive. The intent is recorded and not enforced, so `…|1.0-Alpha` and `…|1.0-alpha` compare
equal today.

Comparing them in the resolution join is worse than slow: a database-default collation on one side and an
explicit `Latin1_General_100_CS_AS` on the other raises `Cannot resolve the collation conflict` — a hard
error, not a silent plan regression. Aligning `Version` to `VARCHAR(64) COLLATE Latin1_General_100_CS_AS`
fixes correctness and the join together. The `NVARCHAR → VARCHAR` narrowing costs nothing in practice
**because the target side is already `VARCHAR`**: `TokenSearchParam.Code` has always stored the business
version narrowed, so making the source side match introduces no loss that the target does not already
have — it makes two halves of one comparison agree.

One length asymmetry to settle: `Version` is 64 characters against `Code`'s 256. A version between the two
lengths is truncated on the source side and intact on the target, so the two would silently fail to match.
Either widen `Version` to 256 or assert the shorter bound at index time.

#### 3. Index coverage (performance only)

- `IX_SearchParamId_Uri (SearchParamId, Uri)` already serves both halves of the URL value join. Adding
  `INCLUDE (Version, Fragment)` keeps the version narrowing a seek residual instead of a key lookup.
- The target-version probe needs **no new index**: it is correlated by
  `(ResourceTypeId, ResourceSurrogateId, SearchParamId)`, which is exactly `IXC_TokenSearchParam`'s key.
- `_revinclude` probes `UriSearchParam` by the same triple, served by `IXC_UriSearchParam`.

When emitting predicates, declare the collation in the DDL rather than as a column-side `COLLATE` override:
`UriLoweringRuleTests.cs:46-47` records that forcing BIN2 on the column made the predicate incompatible
with the index key ordering.

#### Where a storage change has to land

The schema has **two** sources that must move together, plus a third that follows automatically:

| Artefact | Change |
|---|---|
| `Ignixa.DataLayer.SqlServer.Database/Tables/UriSearchParam.sql` | column types/collation, index `INCLUDE` |
| `Ignixa.DataLayer.SqlEntityFramework/Migrations/…` + `Entities/UriSearchParamEntity.cs` | matching EF migration and column mapping |
| `Ignixa.Search.Sql`'s `SqlCatalog` | **none** — regenerated from the DDL by `Ignixa.Search.Sql.Generators`, so drift becomes a build error rather than a runtime surprise |
| `Resources/97.sql` (TVP + `MergeResources`) | only under option 1 — and additively, as a new type and entry point |

### P3 — Canonical-ness metadata from codegen

`_include=QuestionnaireResponse:questionnaire` carries **no search value**, so the value-shape dispatch of
P0 is structurally unable to classify it — and as shown above, `SearchParameterInfo` is byte-identical
between the STU3 `Reference` form and the R4+ `canonical` form. The include/chain planner therefore
*cannot* know which table to join without new metadata.

Add a `TargetElementType` (or a narrower `IsCanonical`) fact to `SearchParameterInfo`, populated by the
existing search-parameter code generator from the element type each SP expression resolves to. This makes
the two dispatch decisions explicit and version-correct:

| Decision | Driven by |
|---|---|
| Which index table a parameter lives in (`ReferenceSearchParam` vs `UriSearchParam`) | `TargetElementType == canonical` |
| Whether a leaf value splits on `\|` / `#` | same flag, plus the model version (STU3 never splits) |

With the flag available, P0's value-shape heuristic becomes a fallback for custom/IG parameters whose
element type could not be resolved, not the primary mechanism.

Note the ordering dependency: P3 must precede the resolution join (it is what makes the join selectable at
all) but is independent of P1/P2.

### The canonical resolution join

This is the part with no precedent in the compiler. Today every include and chain is an **id join**:
`IncludeEmitter` emits `ReferenceSearchParam rsp INNER JOIN Resource r ON r.ResourceTypeId =
rsp.ReferenceResourceTypeId AND r.ResourceId = rsp.ReferenceResourceId`, hard-coded with
`rsp.BaseUri IS NULL` (`IncludeEmitter.cs:46,65-69`); `ChainLoweringRule` builds a `CteDefinition.ChainJoin`
over the same table. A canonical has no `ReferenceResourceId`, so none of that applies.

Per R5 §2.1.3.0.6, resolution is `?url=<url>&version=<version>` against the target. In Ignixa's index that
is three tables, joined on **values**:

| Side | Element | Search parameter | Storage |
|---|---|---|---|
| Source | `QuestionnaireResponse.questionnaire` | `questionnaire` (declared `reference`) | `UriSearchParam.Uri` + `.Version` + `.Fragment` |
| Target | `Questionnaire.url` | `url` (uri) — R5: `CanonicalResource-url` | `UriSearchParam.Uri` (`Version` always NULL) |
| Target | `Questionnaire.version` | `version` (**token**) — R5: `CanonicalResource-version` | `TokenSearchParam.Code` (`SystemId` NULL) |

```sql
FROM      dbo.UriSearchParam   src           -- QuestionnaireResponse.questionnaire
INNER JOIN dbo.UriSearchParam  tgt           -- Questionnaire.url
        ON tgt.Uri = src.Uri
       AND tgt.SearchParamId = <url>
       AND tgt.ResourceTypeId = <Questionnaire>
INNER JOIN dbo.Resource        r
        ON r.ResourceTypeId = tgt.ResourceTypeId
       AND r.ResourceSurrogateId = tgt.ResourceSurrogateId
WHERE     src.SearchParamId = <questionnaire>
      AND (src.Version IS NULL OR EXISTS (    -- version narrowing, only when the canonical carried one
            SELECT 1 FROM dbo.TokenSearchParam v
            WHERE v.ResourceTypeId = tgt.ResourceTypeId
              AND v.ResourceSurrogateId = tgt.ResourceSurrogateId
              AND v.SearchParamId = <version>
              AND (v.Code = src.Version OR v.Code LIKE src.Version + '.%')))
```

Six consequences, each of which is a real design decision:

1. **The version predicate crosses a type and collation boundary.** `UriSearchParam.Version` is
   `NVARCHAR(64)` with no declared collation (database default, most likely case-**in**sensitive), while
   `TokenSearchParam.Code` is `VARCHAR(256) COLLATE Latin1_General_100_CS_AS`. Joining them is an implicit
   `NVARCHAR → VARCHAR` conversion *plus* a collation conflict — which SQL Server will either reject or
   silently resolve, in both cases destroying the seek on `IX_SearchParamId_Code_INCLUDE_SystemId`. Making
   `UriSearchParam.Version` `VARCHAR(64) COLLATE Latin1_General_100_CS_AS` in the DDL is close to a
   prerequisite. (The `Uri`-to-`Uri` half of the join is already collation-aligned — both are
   `VARCHAR(256) CS_AS`.)
2. **Partial-version matching belongs here, not in the leaf rule.** §2.1.3.0.7 says an unmodified `|1.2`
   legitimately resolves to `1.2.1` and `1.2.3-draft`. That prefix behaviour applies to *resolving the
   target*, while matching a stored canonical string stays exact. The same `|1.2` therefore means two
   different things on the two sides of one query — the single subtlest point in this whole design, and
   the one most likely to be implemented as an accidental inconsistency.
3. **The "latest version" policy is unavoidable and unspecified.** A bare canonical with several matching
   target versions "should pick the latest", by an algorithm the spec explicitly declines to define, using
   a `versionAlgorithm[x]` element Ignixa does not index. For `_include` the safe reading is to include
   *all* matching versions (an include is best-effort and a superset harms nothing); for *chaining*, where
   the resolution changes which source resources match, a superset is a semantic choice that must be
   documented rather than defaulted into.
4. **`_revinclude` needs the seed's own identity, which the seed CTE does not carry.** For
   `Questionnaire?_revinclude=QuestionnaireResponse:questionnaire`, each seed row is only `(T1, Sid1)`
   (`IncludeEmitter.EmitSeedExists`), so the emitter must join *back* through
   `UriSearchParam(url)` — and `TokenSearchParam(version)` — to recover the URL and version each seed
   publishes, before matching them against `UriSearchParam(questionnaire).Uri`. The correlation is
   value-based in both directions.
5. **The target `url`/`version` parameter ids must be resolved per target type and per FHIR version.**
   `ISymbolResolver` is asked for a `SearchParameterInfo`, so `Resolve` must pick `Questionnaire-url` on
   R4 and `CanonicalResource-url` on R5, once per candidate target type. A `canonical(Any)` element
   multiplies this across every canonical resource type.
6. **STU3 must take the ordinary reference path** for the very same parameter, because the element is a
   real `Reference(Questionnaire)` there. One `_include` expression, two join shapes, selected by the P2
   metadata.

**Recommended staging.** Land the leaf semantics (P0/P1) first — they are self-contained, deliver the
visible fix, and need none of this. Then P2 metadata. Treat the resolution join as its own increment with
its own ADR, because it introduces a second join kind into `CteDefinition`/`IncludeStage` and touches
`IncludeEmitter`, `IncludeStagePlanner` and `ChainLoweringRule` — all of which currently assume
`ReferenceSearchParam`. **Until it lands, `_include`/`_revinclude`/chaining on a canonical parameter must
throw with an actionable message rather than emit the existing `ReferenceSearchParam` join**, which would
be valid SQL against a table canonicals never populate and would silently return nothing.

### P4 — Reject what the spec says is unsupported

`:identifier` on a canonical parameter must throw a `NotSupportedException` carrying the parameter (via
`LeafLoweringDispatcher.Enrich`) so it surfaces as an actionable 400 (R5 §3.2.1.5.11). The same mechanism
carries the interim canonical-include refusal described above.

## Alternatives Considered

| Option | Why not |
|---|---|
| **Extend `UriLoweringRule`** with optional `Version`/`Fragment` conjuncts, no new value type | Cheapest, but forces one rule to branch on the string's shape to decide whether `:below` means URL-path-prefix or version-prefix. Two incompatible semantics behind one type is precisely the silently-wrong-answer failure mode the README forbids. |
| **Model canonical as a composite** (uri + token) | Reuses the composite dispatcher, but composites model `SearchParameter.component`, not datatype decomposition, and there is no composite table for the pair. It would need a synthetic parameter and would not correlate to a single row any better than P1 does. |
| **Index canonicals into `ReferenceSearchParam`**, resolving `url\|version` → local resource id at index time | Would make includes/chaining fall out of the existing id join for free. But a canonical target frequently does not exist yet when the referrer is written (a QuestionnaireResponse can legitimately precede its Questionnaire), so the resolution would be null and permanently wrong until a reindex; and every write of a canonical resource would invalidate rows pointing at it. Resolving at query time has none of these failure modes. |
| **A materialised canonical-resolution table** `(url, version) → (ResourceTypeId, ResourceSurrogateId)`, maintained on canonical-resource write | The performance answer if the query-time three-table value join proves too slow: one id join again, refreshed independently of the referrer. Costs a new table, a new write path, and a rebuild story. Worth measuring against the P-join before adopting — not a P0. |
| **Normalize at index time** (strip version, or store the whole string unsplit) | Stripping loses information irrecoverably; storing unsplit makes the spec-mandated "bare URL matches all versions" behaviour a `LIKE 'u%'` scan and re-breaks `:below`. |

## Tradeoffs

| Pros | Cons |
|---|---|
| Turns a silent wrong answer (empty bundle) into a correct one | Requires a storage-identity change to represent two versions of one canonical on one resource (P2), which touches the write path |
| Reuses the existing leaf dispatcher/rule idiom — small, isolated, unit-testable rule | Adds a fifth value type to `Ignixa.Search` that the legacy `SearchValueExpressionBuilderHelper` must also learn, or explicitly reject |
| Plain-`uri` behaviour and its golden SQL are untouched — canonical is purely additive | Value-shape dispatch is a heuristic on `\|`; correct per spec escaping rules, but a client that fails to escape a literal pipe gets a confusing result |
| Leaf semantics (P0/P1) ship independently of the resolution join and deliver the visible fix on their own | The resolution join introduces a **second join kind** into `CteDefinition`/`IncludeStage`; `IncludeEmitter`, `IncludeStagePlanner` and `ChainLoweringRule` all currently assume `ReferenceSearchParam` |
| Query-time resolution is immune to ordering — a QuestionnaireResponse written before its Questionnaire resolves correctly with no reindex | Three-table value join on `VARCHAR(256)` is inherently more expensive than the existing id join; needs measurement, and possibly the materialised-resolution alternative later |
| Spec-verified semantics with citations; `:identifier` rejection is explicit | The R5 `:below` definition is self-inconsistent (ordering vs. prefix), and unmodified *resolution* does partial-version prefix matching while leaf *matching* is exact — two behaviours that read alike and must be documented apart |
| Existing `Search/canonical.json` TestScript can have its `warningOnly` assertions tightened, converting documentation of a gap into a conformance guarantee | Version comparison remains lexical; no `versionAlgorithm`/semver ordering (deliberately out of scope) |

## Alignment

- [x] Follows architectural layering rules — value type in `Ignixa.Search`, lowering rule in
      `Ignixa.Search.Sql/Lowering/Leaf/`, storage facts stay generated from DDL; no `Hl7.Fhir.*` reference
- [x] Developer Experience — no configuration; canonical search works from a stock deployment
- [x] Specification compliance — R5 §2.1.3.0.6, §2.1.3.0.7, §3.2.1.5.11, §3.2.1.6.1.5, §3.2.1.6.2
      verified; R4/R4B via the same SHOULD; STU3 explicitly excluded
- [x] Consistent with existing patterns — one value type, one leaf rule, one dispatcher arm, exactly like
      token/reference/quantity
- [ ] Storage change (P2) needs an ADR decision because it brushes the "do not modify `MergeResources`
      procs/TVPs" constraint
- [ ] The resolution join is **not** consistent with the existing single-table include/chain shape; it
      warrants its own ADR rather than being folded in

## Open Questions

1. **`:above` on canonicals.** R5 defines it as a version 'greater than' requiring a known version scheme,
   and says unknown schemes SHALL be ignored **or** rejected. Proposal: reject with 400 (fail-loud), since
   Ignixa does not track `versionAlgorithm`. Needs an explicit ADR decision.
2. **`:below` — ordering or prefix?** §3.2.1.5.5.2.2 says 'less than'; §3.2.1.6.2's example shows a
   major-version prefix. Proposal: implement the demonstrated prefix behaviour and document it in
   `CapabilityStatement.rest.resource.searchParam.documentation`.
3. **Partial-version resolution vs. exact leaf matching.** §2.1.3.0.7 makes unmodified `|1.2` resolve to
   `1.2.1`/`1.2.3-draft` on the *target* side, while the leaf predicate on the *source* string stays exact.
   Confirm this asymmetry is intended before implementing, because it is indistinguishable from a bug on
   inspection.
4. **"Latest version" policy** when a bare canonical resolves to several target versions. Proposal:
   `_include` returns all (best-effort, superset); chaining also matches through all, documented. The
   alternative — a server-chosen "latest" — needs `versionAlgorithm`, `status` and `date` to be indexed,
   which they are not.
5. **Fragment matching** is not specified for search. Proposal: narrowing conjunct on the source side only;
   for resolution the fragment is ignored, since it names a contained resource *inside* the resolved
   container (R5 §2.1.3.0.8).
6. **Do we retire the legacy path's dead canonical branch**, or fix `separateCanonicalComponents` there
   too? Fixing it makes the legacy path exhibit the cross-row smearing of defect 2, which is arguably worse
   than today's empty result. Prefer: leave legacy as-is, land canonical only in `Ignixa.Search.Sql`.
7. **`VARCHAR(256)` on `Uri`** — real IG canonicals plus versions approach this. Confirm truncation is
   impossible or detected; a truncated canonical is a silent wrong answer.
8. **Cost of the resolution join.** Measure before committing; if it is unacceptable, the materialised
   resolution table becomes the design rather than a fallback.

## Acceptance Test Matrix

### Leaf matching — fixtures (already in `Search/canonical.json`): `P|1#fragment`, `P|2`, `[PAlt, P|1]`

| Query | Expected | Source |
|---|---|---|
| `_profile=P` | all three (bare URL is the superset) | R5 §3.2.1.6.2 |
| `_profile=P\|1` | v1 and alt only | §3.2.1.6.2 |
| `_profile=P\|1%23fragment` | v1 only | derived (fragment as narrowing conjunct) |
| `_profile=P\|2` | v2 only | §3.2.1.6.2 |
| `_profile=PAlt` | alt only | §3.2.1.6.2 |
| `_profile:below=P` | all three | §3.2.1.6.2 example |
| `_profile:below=P\|1` given `1.0`/`1.1`/`2.0` | `1.0`, `1.1` | §3.2.1.6.2 example |
| `_profile:below=P\|1` given `10.0` | no match | derived (separator-aware) |
| Single resource with `[A\|1, B\|2]`, query `A\|2` | no match | defect 2 regression |
| `_profile:identifier=…` | 400 | R5 §3.2.1.5.11 |
| STU3 `_profile=P\|1` | literal whole-string match | `canonical` absent in STU3 |

### Resolution — fixtures: `Questionnaire` Q1 (`url=U, version=1.0`), Q2 (`url=U, version=2.0`), Q3 (`url=U`, no version); `QuestionnaireResponse` QR-a (`questionnaire=U|1.0`), QR-b (`questionnaire=U`), QR-c (`questionnaire=U|1`)

| Query | Expected | Source |
|---|---|---|
| `QuestionnaireResponse?_include=QuestionnaireResponse:questionnaire` (QR-a) | includes Q1 only | §2.1.3.0.6 (`?url=U&version=1.0`) |
| same for QR-b (bare URL) | includes Q1, Q2, Q3 — or the policy-chosen latest | §2.1.3.0.6 / open question 4 |
| same for QR-c (`\|1` partial) | includes Q1 (`1.0`), not Q2 | §2.1.3.0.7 partial-version |
| `Questionnaire?_revinclude=QuestionnaireResponse:questionnaire` seeded by Q1 | includes QR-a, QR-b, QR-c | §2.1.3.0.6, reverse direction |
| `QuestionnaireResponse?questionnaire.title=X` | resolves then filters on the target | §2.1.3.0.6 + chaining |
| Q created *after* QR, no reindex | include still resolves | query-time resolution invariant |
| `canonical` target whose `version` is absent, query carries `\|1.0` | no match ("If there is no match … the instance will not be matched") | §2.1.3.0.6 |
| STU3 `_include=QuestionnaireResponse:questionnaire` | ordinary `ReferenceSearchParam` id join | element is `Reference` in STU3 |
| Canonical include *before* the resolution join lands | 400 with an actionable message, never an empty bundle | fail-loud principle |

Validation surfaces: unit tests on `CanonicalLoweringRule` (`test/Ignixa.Search.Sql.Tests/Lowering/`),
`Explain()` golden tests, the differential corpus under `test/Ignixa.Search.Sql.Tests/Corpus/`, and
tightening the `warningOnly` flags in `Search/canonical.json` once implemented.

## Verdict

*Pending evaluation.*
