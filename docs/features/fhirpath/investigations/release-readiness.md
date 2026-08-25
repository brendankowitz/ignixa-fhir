# Investigation: FHIRPath Release Readiness

**Feature**: fhirpath
**Status**: In Progress
**Created**: 2026-08-21

Ignixa FHIRPath — release-readiness plan for the fhir-server (ADR 2608) NuGet package

Date: 2026-08-21. Code references are to the PR #427 worktree at `C:/w427` (detached `92c99541`,
merge base `c66cc4a6`). Spec references are to the FHIRPath continuous build v3.0.0
(https://build.fhir.org/ig/HL7/FHIRPath/), read from its source
`https://raw.githubusercontent.com/HL7/FHIRPath/master/input/pages/index.md` (fetched 2026-08-21;
line numbers below are into that file, 4,810 lines). HAPI references are to
`hapifhir/org.hl7.fhir.core` master,
`org.hl7.fhir.r5/src/main/java/org/hl7/fhir/r5/fhirpath/FHIRPathEngine.java` (7,391 lines, last
touched 2026-07-30 by `a000cc51`) and `org.hl7.fhir.r5/.../model/Base.java`, both fetched today.

**Decision rule (binding, stated per decision below):**
Tier 1 = FHIRPath spec at build.fhir.org (current text, not the Nov-2025 ballot reading).
Tier 2 = HAPI (`FHIRPathEngine.java`).
Tier 3 = Firely 5.11.4 (the version fhir-server runs), only where 1 and 2 don't settle it.
Where Ignixa is more spec-compliant than Firely: deliberate divergence, documented and surfaced to
the seam — not "fixed" back. Where Ignixa diverges from spec AND HAPI: defect.

---

## 1. Spec-conformance re-baseline

### 1.1 The Nov-2025 gap analysis is obsolete and must be retired, not amended

`docs/features/fhirpath/investigations/gap-analysis.md` (2025-11-18) is wrong in both
directions against the code at `C:/w427`:

| gap-analysis claim | Status today | Evidence |
|---|---|---|
| Quantity literal evaluation "NOT IMPLEMENTED, critical" | Implemented; quantity equivalence and unit handling are exercised by the parity corpus | `1 'mg'` cases throughout `test/Ignixa.FhirPath.Tests/Evaluation/Parity/`; quantity comparison narrowing was the subject of PR #398 |
| Math, aggregates, date components, sort, coalesce, trim/split/join/encode/decode, defineVariable, precision, repeatAll, toLong "NOT IMPLEMENTED" | All implemented | `[FhirPathFunction]` census over `src/Core/Ignixa.FhirPath/Evaluation/Functions/`: 120+ functions including `abs ceiling floor exp ln log power round sqrt truncate`, `aggregate sum min max avg`, `year month day hour minute second millisecond timezone timeOfDay duration difference`, `sort coalesce defineVariable precision repeatAll toLong convertsToLong trim split join lastIndexOf encode decode escape unescape matchesFull combine not` |
| "`not` operator missing" (~94% operator coverage) | Wrong then and now. FHIRPath has no unary `not` operator; `not()` is a function (spec line 3884) and Ignixa implements it | spec-index.md:3884; function census above |
| "Long literal missing" | Still true as a *literal*, and `Long` is **STU**, not normative (see 1.2). The real Long defect is different (see D3, §4) | spec-index.md:226 "`Long: 0L, 45L // Long is defined as STU`" |
| "~98% normative coverage" headline | Understated now; the normative surface is effectively complete. What remains open is a handful of *semantic* decisions (§3, §4), not missing functions | function census; official suite results (§6) |

Verdict: mark gap-analysis.md superseded (header edit only, in the docs PR), pointing at this plan
and `firely-parity.md`. Do not maintain its roadmap — Phases 23–26 there describe work that already
happened.

### 1.2 What the current build actually says (deltas that matter)

Facts pulled from the current spec text, each with the line in `index.md`:

1. **Long is STU** (line 226; conversion-table rows carry `{:.stu-bg}` at 1296–1300; `toLong`/
   `convertsToLong` STU at 1484/1517). Not a normative gap. The Ignixa defect around `Long` is a
   self-consistency bug (D3), not a conformance blocker.
2. **`repeat()` dedup is defined via the `=` operator, not deep equality** (line 1016: items are
   added "only if they are not already in the output collection as determined by the equals (`=`)
   operator returning `true` *(i.e. `false` and empty both indicate that the values are not equal
   and thus added)*"). Ignixa dedups by deep equality (`src/Core/Ignixa.FhirPath/Evaluation/Functions/CollectionFunctions.cs:446-485`). For
   primitives these coincide; for temporals with mismatched precision `=` yields empty → spec says
   ADD, and deep equality may decide differently. This must be checked when the `repeat()` guard is
   built (WI-R2). Order of `repeat()` results is explicitly undefined (line 1042).
3. **The spec does not mandate termination guards.** `repeatAll`'s own examples show
   non-terminating expressions as authoring mistakes (lines 1088–1089). HAPI's `funcRepeat` is
   unbounded (FHIRPathEngine.java:5625-5657 — `while (more)` with `equalsDeep` dedup, no cap). So
   iteration caps are engine hardening, decided by neither tier — they are *our* multi-tenant
   write-path requirement, and Ignixa already set the precedent with `repeatAll`'s 100,000 cap.
4. **Collection equivalence `~`** (lines 3483–3505): same-size, "Each item must be equivalent",
   "Comparison is not order dependent", different sizes → false. The text does not say whether
   duplicates must pair off (multiset matching) or merely each find *some* equivalent partner.
   Tier 1 is ambiguous; see §3.5.
5. **Singleton evaluation of collections** (lines 546–560): single node + expected type Boolean →
   `true`; empty → empty; multi-item → error. This is the spec basis for Firely's `BooleanEval`;
   nothing in the spec defines a `Predicate`-on-empty answer — that is SDK API surface (§3.1).
6. **Type/element name case-sensitivity is delegated to the model** (line 4561: "the
   case-sensitivity of type and element names is defined by each model"; restated at 4777). Tier 1
   therefore does not settle the pre-R5 capitalised-cast question by itself — the FHIR model's own
   per-release text does, and HAPI's reading of it is Tier 2 evidence (§3.3).
7. **STU date-component functions were RENAMED in the current build**: `yearOf()`, `monthOf()`,
   `dayOf()` … `timezoneOffsetOf(): Decimal` (lines 3055, 3073, 3099, 3195). Ignixa implements the
   older ballot names (`year`, `month`, …, `timezone`). These are STU, reachable from no shipped
   search parameter, and absent from the vendored FHIR test suites (which track published FHIR
   releases, not the FHIRPath CI build). **Do not chase the rename for this release** — record it
   as a known STU drift. It is, however, a live demonstration of why implementing ballot functions
   eagerly is a liability (see §10).
8. **`conformsTo()` and `%terminologies` do not appear in the FHIRPath spec at all** (zero
   occurrences in index.md). They are FHIR-core supplement functions (fhirpath.html "additional
   functions"). This materially supports converting their suite pass-throughs to recorded skips
   rather than implementing them (§6.3).

**What "in good shape" means now:** the normative FHIRPath surface is implemented; release
readiness is decided by (a) the five production defects in §4, (b) the semantic verdicts in §3,
(c) honest measurements (§7), and (d) an honestly-passing official suite (§6). Not by function
coverage percentages.

---

## 2. HAPI cross-check — every contested behaviour

All verified by reading the fetched source, not from memory.

| # | Behaviour | HAPI (evidence) | Bearing |
|---|---|---|---|
| 2.1 | Boolean projection of a result set (`Predicate`-shaped) | `convertToBoolean(List<Base>)`: **empty → false**; singleton BooleanType → its value; any other non-empty → true (FHIRPathEngine.java:978-988) | Ignixa's `Predicate` empty→false (`src/Core/Ignixa.FhirPath/Extensions/TypedElementExtensions.cs:211,223`) **matches HAPI**, diverges from Firely (empty→true). See §3.1 |
| 2.2 | Pre-R5 capitalised casts | **Confirmed**: `initFlags()` sets `doNotEnforceAsCaseSensitive = true` and `doNotEnforceAsSingletonRule = true` when `!VersionUtilities.isR5Plus(worker.getVersion())` (FHIRPathEngine.java:237-242); `compareTypeNames` then uses `equalsIgnoreCase` (:2009-2015). The PR's `!isR5Plus()` claim is **verified**, with a nuance: HAPI's leniency is blanket case-insensitivity, *broader* than Ignixa's enumerated canonical-spelling alias set (`TypeMatcher.CanonicalSystemPrimitiveSpellings`, src/Core/Ignixa.FhirPath/Evaluation/TypeMatcher.cs:138-150). Also `Base.hasType` is `equalsIgnoreCase` (Base.java:325-338), so HAPI's System-namespace casts are case-insensitive at the base | §3.3 — Ignixa's version-gated, enumerated leniency is the *narrowest* implementation consistent with both tiers |
| 2.3 | `as` singleton rule pre-R5 | Same flag: HAPI does **not** enforce the singleton rule pre-R5 (:240, :1997, :5530) | Direct Tier-2 support for the `testFHIRPathAsFunction21` skip rationale (§6.4) |
| 2.4 | `repeat()` termination | **Unbounded** (:5625-5657). No iteration cap, `equalsDeep` dedup, O(n²) | Cap is engine hardening, not conformance (§4 D2) |
| 2.5 | Collection `~` | `opEquivalent` (:2496-2517): size equality + each left item finds *some* equivalent right item; matched right items are **not consumed** — no multiset matching | §3.5 |
| 2.6 | Complex-type equivalence recursion | `doEquivalent` recurses through `MergedList` into children with **no depth guard** (:2335-2390) | HAPI shares the exposure; irrelevant to our containment guarantee — D4 stands on the write-path argument |
| 2.7 | `Long` | HAPI's engine has no `System.Long`: `isKnownType` System list is `String, Boolean, Integer, Decimal, Quantity, DateTime, Time, SimpleTypeInfo, ClassInfo` (:2018-2035); `funcIs`'s unqualified-System list likewise (:5477). 64-bit integers appear only as FHIR `integer64` in numeric handling (:6553-6571) | Long is STU (Tier 1) and unsupported by HAPI (Tier 2) — implementing the Long *literal* is not release work. The D3 fix is about not lying (silent empty) |
| 2.8 | `ofType()` unknown-identifier failure mode | HAPI errors (`isKnownType` → `PathEngineException`, :1994-1996, :5528); Firely returns empty. Ignixa matches HAPI-style erroring by consistency choice (src/Core/Ignixa.FhirPath/Evaluation/TypeMatcher.cs doc block, "the reference engines disagree (HAPI errors, Firely returns empty)") | Keep; already documented in code. Seam note only |

Not cross-checked in HAPI because they are SDK API surface, not language semantics: `Scalar`
(throw-vs-null on 2+) and `IsBoolean` — HAPI has no analogues; these are decided in §3.1/3.2.

---

## 3. Verdicts on contested behaviours and blocking divergences

Format: verdict, then **deciding tier**.

### 3.1 `Predicate` empty→false (Ignixa) vs empty→true (Firely)
**Verdict: keep Ignixa's behaviour; no engine change.** The spec does not define a Predicate API
(Tier 1 silent — singleton boolean evaluation at spec lines 546-560 covers non-empty singletons
only); HAPI's equivalent maps empty→false (Tier 2, FHIRPathEngine.java:978-988). Ignixa agrees
with HAPI. Firely's empty→true is the odd one out. **fhir-server is unaffected**: ADR 2608
deliberately derives `Predicate` (and `Scalar`, `IsTrue`, `IsBoolean`) once, in the seam, from
`Select`, reimplementing Firely's `BooleanEval` — precisely so the engines never need to agree on
this. What the release must do: document the difference in the package docs so no one wires
Ignixa's `TypedElementExtensions.Predicate` (src/Core/Ignixa.FhirPath/Extensions/TypedElementExtensions.cs:211,223) directly into a
Firely-shaped call site, and reconcile the second `Predicate` at
`src/Core/Ignixa.DeId/Extensions/FhirPathExtensions.cs:37` with the primary one (same semantics,
one documented definition). **Deciding tier: 2 (HAPI), with the seam derivation making the
question moot for fhir-server.**

### 3.2 `Scalar` null-on-2+ (Ignixa, SDK-6 semantics) vs throw (Firely 5.11.4)
**Verdict: keep; document.** Tier 1 and 2 silent (API surface). The seam derives Scalar and pins
the 5.11.4 throw (ADR 2608, "we are on 5.11.4 and pin the throw"), so fhir-server gets the throw
regardless of what `TypedElementExtensions.Scalar` (src/Core/Ignixa.FhirPath/Extensions/TypedElementExtensions.cs:155-165) does.
Package docs must state Ignixa's native Scalar is SDK-6-shaped. `IsBoolean` similarly does not
need an engine implementation — the seam derives it — but the *characterization corpus* gap is
real: nothing in the parity work compares an IsBoolean derivation. That test belongs in
fhir-server's characterization suite per ADR 2608 step 1; note it in the ADR correction (§8), do
not build it here. **Deciding tier: 3 (Firely), implemented at the seam, not in the engine.**

### 3.3 Pre-R5 capitalised casts (STU3 11/11, R4 1, R4B 1) — crux #1
**Verdict: keep-and-document. Ignixa is more correct than Firely; this is the deliberate-divergence
case the policy exists for.** Tier 1 delegates model-name casing to the model (spec line 4561);
the FHIR model's own release texts moved from R4/R4B's `as()` allowance to R5's narrower rule
(the `TypeMatcher` doc block, src/Core/Ignixa.FhirPath/Evaluation/TypeMatcher.cs, walks this); Tier 2 confirms with `initFlags()`
gating blanket case-insensitive `as` on `!isR5Plus()` (FHIRPathEngine.java:237-242). Firely
returns empty for `value.as(DateTime)` on pre-R5 content and silently drops the value from the
index; Ignixa (and HAPI) return the value. Ignixa's mechanism — ordinal matching everywhere plus
an enumerated pre-R5 alias set (`CanonicalSystemPrimitiveSpellings`,
`PreR5ArtifactErratumCastAliases`, src/Core/Ignixa.FhirPath/Evaluation/TypeMatcher.cs:138-160) — is *narrower* than HAPI's
`equalsIgnoreCase` and is the defensible minimum.
**Deciding tier: 2 (HAPI), Tier 1 having delegated.**

Enablement consequence, stated honestly (this is what the divergence actually costs fhir-server):
enabling Ignixa makes index rows *appear* that Firely never wrote (correct, additive rows).
Existing rows are not invalidated; rollback leaves extra correct rows behind. So ADR 2608's "index
rows written under either provider stay valid" **holds for this class**, but "search results are
identical without a reindex" does not — R4 `Observation-code-value-date` (1 parameter) starts
matching where it previously silently didn't. That is a *behaviour-change release note plus
optional reindex to backfill*, not a migration blocker. STU3's 11 parameters are the same story at
larger scale; STU3 is not fhir-server's primary version. These stay pinned as
`BlocksEnablement: true` in the differential tests until the ADR text is corrected to carry this
exact framing; then they become documented divergences, not blockers.

### 3.4 R5 `instant` carrier: point vs 1-second range — crux #2
**Verdict: verify-then-decide, default keep-and-document with an explicit reindex note for R5.**
This is FHIR *search* semantics (implicit ranges of temporal values), not FHIRPath semantics —
Tier 1 here is FHIR's search page (`https://hl7.org/fhir/R5/search.html#date`: a date parameter
matches on the implicit range of the value; an `instant` carries at least second, typically
millisecond precision). The divergence: for `(start | requestedPeriod.start).first()` Ignixa hands
the indexer an `instant` and a point is indexed; Firely 5.11.4 hands `System.DateTime` and a
1-second range is indexed (docs/features/fhirpath/resource-backed-parity-corpus.md:~110, "R5 instant/dateTime carrier —
Confirmed production divergence").
Work item: pin down which implicit range FHIR R5 mandates for a millisecond-precision instant
(fetch and cite the search page + datatypes page in the PR). If the point (or ms-range) is
correct, Ignixa keeps its carrier, and ADR 2608's "index rows written under either provider stay
valid" must be **corrected**: for R5 the same resource produces *different* rows under the two
providers, so provider flips on R5 require a reindex of instant-carrying date parameters. If FHIR
mandates the second-range, the fix belongs where the range is expanded — most likely fhir-server's
date converter reacting to the carrier type, in which case the seam adapter must present the
carrier Firely-compatibly and that is an Ignixa adapter change (`src/Core/Extensions/Ignixa.Extensions.FirelySdk5`).
Only R5 is affected; fhir-server ships R4 as primary, so this is not a package release blocker —
it is an **ADR-correction blocker** (the ADR currently asserts something the measurements
contradict). **Deciding tier: 1 (FHIR search spec), pending the verification read; Firely is not
followed merely because the old rows are Firely-shaped.**

### 3.5 Collection `~`: Kuhn matching (Ignixa) vs per-item existence (HAPI)
**Verdict: keep-and-document.** Tier 1 is genuinely ambiguous (spec lines 3499-3503: same size,
"each item must be equivalent", order-independent — silent on duplicate multiplicity). Tier 2 has
a behaviour — size check plus unconsumed per-left-item existence (FHIRPathEngine.java:2496-2517) —
which answers `[a,a,b] ~ [a,b,b]` as `true` where Ignixa's verified-correct maximum bipartite
matching answers `false`. The precedence rule says HAPI settles what the spec leaves open; but
HAPI's answer is not a *reading* of the ambiguity so much as an implementation shortcut, the
divergent inputs (duplicate-heavy multisets) are reachable from no shipped search parameter, and
no official test distinguishes them (verify this claim by grepping the vendored suites when
documenting). Downgrading a verified-correct multiset semantics to an existence check to match an
implementation detail buys nothing and loses symmetry guarantees. Keep the matching; add two
differential pins (`[a,a,b] ~ [a,b,b]` false, and its official-suite absence recorded); document
as a deliberate divergence from HAPI in firely-parity.md. If HL7 clarifies the text the decision
gets revisited — cite the spec lines in the doc so the trigger is findable.
**Deciding tier: 1 (ambiguity acknowledged), consciously *not* following Tier 2, with rationale
recorded — this is the one place the plan recommends deviating from the letter of the precedence
rule; the user signs off on it or we flip to HAPI's behaviour (a ~20-line change).**

> **DECIDED 2026-08-21 (user signoff): keep the Kuhn matching.** The deviation from the stated
> precedence rule is accepted for the reasons above — HAPI's `opEquivalent` reads as an
> implementation shortcut rather than a reading of the ambiguity, the divergent inputs are
> unreachable from any shipped search parameter, and the matching's order-independence is
> guaranteed by construction where the existence check's is not. Work item stands as written:
> keep the code, add the two differential pins (`[a,a,b] ~ [a,b,b]` false, plus the recorded
> absence of any official-suite case distinguishing the two semantics), and document the
> divergence in `firely-parity.md` citing spec lines 3499-3503 and
> `FHIRPathEngine.java:2496-2517` so the decision is findable and revisitable if HL7 clarifies
> the text. This is a documentation item, not a code change.

### 3.6 `%resource` / `%rootResource` / `%context` binding
**Verdict: fix precedence in the engine (D-adjacent, hard blocker H4).** Spec: environment
variables are supplied by the evaluation environment; FHIR defines `%resource`/`%rootResource` as
the containing resource, walked up — Ignixa's `src/Core/Ignixa.FhirPath/Extensions/TypedElementExtensions.cs:106-122` binding them to
*the input element* is wrong whenever `Select` is invoked on a non-root element, and 5 shipped
composite components depend on root-bound `%resource` (R4 `R4SearchParameterDefinitions.g.cs:10206,10261`,
R4B `:10393,10448`, STU3 `:12087`). `ElementSearchIndexer` gets it right by binding `Resource`
once (`src/Core/Ignixa.Search/Indexing/ElementSearchIndexer.cs:82-85`). The engine-side requirement for the seam:
**explicitly-supplied environment bindings must always win over the input-element defaults** —
including `%context`, which `src/Core/Ignixa.FhirPath/Evaluation/EvaluationContext.cs:449-451` currently binds by name *ahead of* the
environment dictionary (also the fact that makes ADR 2608's `%context` paragraph factually wrong —
it claims the opposite fall-through; goes in the ADR correction, §8). One precedence policy, three
variables, one test class. **Deciding tier: 1 (FHIR's definitions of the variables); no HAPI/Firely
consultation needed.**

---

## 4. Production defects (all confirmed this session; verdicts and falsification)

Every item: the concrete production mutation that turns the new test red, demonstrated by
execution. This codebase's history (six tests that passed but could not fail; four consecutive
self-certified commits) makes "reviewed the diff" a non-closure.

**D1 — `'@x'` string literals misclassified as temporals (regression, dangerous direction). HARD
BLOCKER. DONE (`b7611520`, `e6023789`).**
`src/Core/Ignixa.FhirPath/Analysis/SystemTypeConstructionAnalyzer.cs:GetConstantTypeName:90-102` sniffs a leading `@`;
`src/Core/Ignixa.FhirPath/Parsing/FhirPathParseTreeGrammar.cs:57` stores `DateLiteral` with its `@` while `:30` strips only quotes
from `StringLiteral`, making them byte-identical in the AST. `'@'.length()` → hard Error;
`'@x' as String` → false AlwaysEmpty. The same rewrite dropped base's `null => "empty"` arm.
*Fix:* at the parse layer — distinct node kind or typed value for temporal literals, so the
analyzer never string-sniffs; restore the null/empty arm explicitly. Not a switch-arm patch.
*Tier:* spec (string literals are strings, unconditionally; lexical section) — defect against
Tier 1, no cross-check needed.
*Falsify:* new tests `'@2013'.length() = 5`, `'@x' as String` non-empty,
`{} …` null-arm analyzer case. Then revert only the parse-layer change (restore the `@`-sniffing
classification), run, show red, restore. Executed and recorded across `b7611520`/`e6023789`.

**D2 — `repeat()` has no iteration cap. HARD BLOCKER (tenant-suppliable unbounded loop+memory on the write path).**
`src/Core/Ignixa.FhirPath/Evaluation/Functions/CollectionFunctions.cs:446-485` vs `RepeatAll`'s 100,000 cap at `:518`. Residuals plan WI-4
already scoped filing this; this plan executes it. Constraints inherited from WI-4's pin: the
guard must throw `FhirPathEvaluationException` so it lands in the Warning/`FailedToExtractValues`
tier. While in the file: check dedup semantics against spec `=` (§1.2 item 2) and fix or
explicitly document the deep-equality choice; do not silently ship a second semantic decision
inside a guard PR — if dedup changes, it is called out in the PR description with spec line 1016
quoted. The O(n²) dedup can be improved opportunistically (hash by a stable value key) but only if
it doesn't change semantics; otherwise file it.
*Tier:* neither (spec is silent, HAPI unbounded — FHIRPathEngine.java:5625) — engine hardening,
justified by the multi-tenant write path and Ignixa's own `repeatAll` precedent.
*Falsify:* test `repeat($this & 'x')`-shaped fresh-value projection asserts
`FhirPathEvaluationException` within a bounded runtime; mutation = raise the cap to
`int.MaxValue`, run with a test timeout, show red (timeout/OOM-guarded), restore. Plus the
indexer-tier containment test per WI-4's template (Warning captured, zero Errors, write survives).

**D3 — `Long` resolves but can never match.**
`src/Core/Ignixa.FhirPath/Evaluation/TypeMatcher.cs:SystemTypeNames:223` contains `"Long"`; `CanonicalSystemPrimitiveSpellings:138` has
no `Long` entry; `toLong()` declares `ReturnType = "long"` → `X.toLong() is Long` and
`.ofType(Long)` silently return empty. **Answer to the census question asked:** Long is the *only*
name with this split. `Quantity` is in `SystemTypeNames` but absent from the spellings map —
inert, because quantity values carry the runtime spelling `Quantity` which matches ordinally
(pinned by `test/Ignixa.FhirPath.Tests/Evaluation/SystemValueTypeMatchingTests.cs:GivenQuantityLiteral_…`); `Date` is in the spellings map
but not `SystemOnlyTypes` — documented inert in the code comment ("Date's absence is inert",
src/Core/Ignixa.FhirPath/Evaluation/TypeMatcher.cs:~118). *Fix:* add `["Long"] = "long"` to the spellings map so the three facts
(resolvable, constructible via `toLong()`, matchable) agree; Long stays STU so this is
**should-fix**, not a blocker — but silent-empty on a resolvable type is the exact silent-index-
drift shape ADR 2608 warns about, so it ships in the release if at all possible.
*Tier:* self-consistency; Long itself is STU (spec line 226) and absent from HAPI
(FHIRPathEngine.java:2018-2035).
*Falsify:* census test asserting every `SystemTypeNames` entry either has a spellings entry or is
on a documented-inert allowlist (`Quantity`, with rationale); behaviour test
`(1).toLong() is Long = true`. Mutation = remove the new `Long` spelling entry → both red; also
remove `["Boolean"]` → census red (proves the census discriminates beyond the one name it was
written for). Execute both.

**D4 — Unguarded mutual recursion in collection equivalence. RESOLVED BY MEASUREMENT — not merged;
the plan's premise was wrong. No longer a hard blocker; removed from the §7 checklist.**
`src/Core/Ignixa.FhirPath/Evaluation/FhirPathEvaluator.cs:AreCollectionsEquivalent` (~925) ↔ `AreElementsEquivalent` (:~1028) still
carry no depth/work guard, and this plan originally called that a hard blocker on the theory that an
uncatchable `StackOverflowException` defeats `ElementSearchIndexer`'s containment guarantee. Measured
instead of argued (commit `6ebdb444`):
- Ingest is capped at **31 nested extensions** — every element tree reaches the evaluator through
  `JsonSourceNodeFactory` at `System.Text.Json`'s default `MaxDepth` of 64, overridden nowhere in
  `src/`. `Utf8JsonWriter`'s non-configurable 1,000-level ceiling means nothing deeper can even be
  stored or returned once built.
- The crash floor is **3,200–4,800 levels**, measured out-of-process (an SOE cannot be asserted
  in-proc). Margin from the ingest cap is roughly 125x.
- The frame that actually overflows first is `FunctionHelpers.AreElementsEqual` — **pre-existing on
  `origin/main`**, unrelated to this branch's `~` work, and reachable from `=`, `in`, `contains`,
  `distinct()`, `|`, `intersect`, `exclude` and `repeat()` — all of which shipped search parameters do
  use. `AreElementsEquivalent` calls it on its first ladder rung, so a depth counter placed on
  `AreCollectionsEquivalent` alone would sit at 1 while the process died in the shared, wider frame
  underneath it.
- A guard scoped to `~` would therefore have shipped a green test while leaving the more reachable
  path exposed — the exact "passes but cannot fail" defect class this work exists to remove. Adding it
  would have been worse than doing nothing: it looks like containment and is not.
- What shipped instead: `EquivalenceRecursionDepthTests` pins the parser ceiling that holds both
  descents off the stack. It goes red if anyone raises `MaxDepth` on the parse path — the guard is on
  the actual constraint (ingest depth), not on the symptom (recursion in one function).
*Tier:* engine hardening; no conformance dimension. HAPI shares the exposure (2.6) — irrelevant; HAPI
is not a multi-tenant indexer.
*Falsify:* executed. Out-of-process forced-overflow runs located the two floors above; reverting the
new test and raising `MaxDepth` demonstrates red. The Kuhn matching itself was not touched — `TryPair`
remains verified correct.
*Left open, not this branch's scope:* the wider, pre-existing `AreElementsEqual` exposure is real and
reachable from more operators than `~` is. It predates PR #427 and is not introduced or worsened by
it, so it is not re-litigated as a blocker here — but it is not fixed either, and a future depth/work
guard on that shared descent would need to cover all of its callers, not just `~`.

**D5 — Analyzer/evaluator casing policy split.**
`src/Core/Ignixa.FhirPath/Visitors/FhirPathTypeSet.cs:CanBeOfType:144,150` matches `OrdinalIgnoreCase` ungated by version;
`src/Core/Ignixa.FhirPath/Evaluation/TypeMatcher.cs` is ordinal-exact + gated aliases. Live only on negated always-empty/cast guards
(`src/Core/Ignixa.FhirPath/Analysis/FhirPathAnalyzer.cs:876,986,1007,1035`) → under-warns; benign direction, but it sits inside the
exact area `AnalyzerEvaluatorTypeCasingAlignmentTests` exists to guard. *Fix:* align
`CanBeOfType` to the TypeMatcher policy (ordinal + version-gated aliases), **should-fix**.
*Tier:* internal consistency with the §3.3 verdict.
*Falsify:* extend `AnalyzerEvaluatorTypeCasingAlignmentTests` with a mis-cased pre-R5-alias case
whose analyzer verdict must match the evaluator's; mutation = revert `CanBeOfType` to
`OrdinalIgnoreCase` → red. Execute.

**D6 — `canonical` search-value converter gap. NEW HARD BLOCKER, discovered by E2/E3/E4 once the
harness could see Ignixa-side failures (commit `40f425ec`). `Ignixa.Search` production work,
tracked elsewhere — out of scope for #427 (a FHIRPath PR); recorded here because it was found by this
session's evidence-base repair and must not be lost.**
- Ignixa registers `canonical` against `UriSearchValue` only
  (`src/Core/Ignixa.Search/Indexing/Converters/CanonicalToUriSearchValueConverter.cs:17`).
- Consequence: **46 shipped SearchParameters index nothing in Ignixa** — including
  `QuestionnaireResponse-questionnaire`, `MeasureReport-measure`, the `instantiates-canonical`
  family, `ConceptMap-source-scope`, `StructureDefinition-base`, and
  `CapabilityStatement-supported-profile`.
- **Corrected 2026-08-22. An earlier revision of this entry framed the fix as porting
  `CanonicalToReferenceSearchValueConverter` (and three further converters) from
  `microsoft/fhir-server`. That is wrong for this codebase, and the correction matters more than the
  original finding.** [PR #430](https://github.com/brendankowitz/ignixa-fhir/pull/430) establishes
  that canonical values *are* indexed correctly today — `CanonicalToUriSearchValueConverter` splits
  them into `UriSearchParam.Uri`/`.Version`/`.Fragment`. Ignixa stores canonical components in
  `UriSearchParam`, **not** `ReferenceSearchParam`, so a `ReferenceSearchValue` converter would
  target the wrong storage model.
- The real shape, per #430: `QuestionnaireResponse-questionnaire` is byte-identical metadata across
  STU3/R4/R5 (`Reference`, target `Questionnaire`) while the *element* changed `Reference` →
  `canonical`. Same parameter, different storage table, and nothing in `SearchParameterInfo`
  distinguishes them — so **codegen metadata is required**, and canonical resolution is a *value
  join* across two search-parameter indexes (R5 §2.1.3.0.6), not the id join every existing include
  hard-codes via `rsp.BaseUri IS NULL`. #430 owns this work; do not re-derive it here.
- `:identifier` is likewise **not** a missing-converter problem:
  [PR #421](https://github.com/brendankowitz/ignixa-fhir/pull/421) solves it with a derived token
  search parameter (`{url}#identifier`) plus `ReferenceToTokenSearchValueConverter`, compiling to a
  single `TokenSearchParam` seek with no schema, TVP, or stored-procedure change. Treat
  `IdentifierToStringSearchValueConverter` as superseded rather than missing.
- **The parity harness is structurally incapable of detecting any of this on its own**: it hands one
  converter-manager instance to both indexers (E2's original defect), so the only axis that is ever
  doubly evaluated is `Select` — a missing *converter* looks identical to "both sides agreed on
  nothing" until the logger capture E2 added made the Ignixa-side silence visible.
- Diagnostic gap worth recording alongside this: `Log.FhirElementTypeNotSupported` carries the
  element type but no parameter identity, so this class of gap cannot be diagnosed from logs on a
  running server — only from a parity run or a code read.
- **This does NOT contradict ADR 2608's claim that "index rows written under either provider stay
  valid." Corrected 2026-08-22; an earlier revision of this entry asserted that it did, and that
  assertion was a category error.** The ADR's seam is `IFhirPathProvider` — FHIRPath evaluation
  only. fhir-server's `TypedElementSearchIndexer` takes the provider by constructor injection, and
  everything downstream of `Select` — including fhir-server's *own* converter manager, which does
  have a canonical-to-reference converter — is fhir-server code that runs identically under either
  provider. `Ignixa.Search`'s converter manager never executes inside fhir-server at all, so
  flipping the provider changes nothing for these 46 parameters. ADR line 167 is contradicted
  **once**, by the R5 `instant` carrier (§3.4), not twice.
- What D6 does block is **Ignixa's own search fidelity and any index-parity claim this repo
  publishes**. The upstream takeaway for the ADR correction is therefore *methodological*, not a
  rollback-claim retraction: Ignixa's published parity numbers do not adjudicate the converter
  pipeline, so ADR line 167's justification must come from fhir-server's own two-provider corpus —
  which ADR lines 163-164 already specify.
*Fix:* out of scope here — do not write the missing converters as part of this documentation pass or
as part of #427. This is `Ignixa.Search` production work and belongs in its own PR against that
project, referenced from the ADR correction.
*Tier:* not a FHIRPath conformance question — a search-indexing completeness gap one layer above the
engine this PR ships.
*Falsify:* not executed here (no code changes made). The gap is demonstrated by the converter
registration read above and by the E2 harness fix that made the Ignixa-side silence visible in the
first place; a dedicated `Ignixa.Search` PR would falsify it the same way D2/D4 falsify — inject the
missing converter, show a previously-silent parameter starts indexing, then decide whether to ship
it.

Verified-correct list (do not spend effort): Kuhn matching/`TryPair`; `ValueOrdering` transitivity;
`SystemTypeConstruction.TypeNames` throw-on-unknown; `IsSystemValue` flag deletion.

---

## 5. Fix the evidence base before quoting it

Order matters: no number gets re-stated until the instrument that produces it discriminates.

**E1 — `OfficialTestSuiteRunner` `NotSupportedException`-as-PASS. DONE (`107480e5`, `8183b284`): RUNNER FIXED, FALSIFIED, AND RE-BASELINED.**
`test/Ignixa.FhirPath.Tests/OfficialTestSuiteRunner.cs:477-482`: `catch (NotSupportedException)` → log + `return` → xunit
Passed. The catch is scoped by exception *type*; `src/Core/Ignixa.FhirPath/Evaluation/FhirPathEvaluator.cs:322` ("Binary operator not
yet implemented") and `:1268` ("Scope not yet implemented") throw the same type, so deleting a
binary-operator arm turns every conformance case using that operator green. *Fix:* replace with a
name-allowlisted genuine skip (the runner already has a real dynamic-skip mechanism —
`test/Ignixa.FhirPath.Tests/OfficialTestSuiteRunner.cs:674` region — use it): only the five terminology/profile/CDA functions
(`conformsTo`, `memberOf`, `validateVS`, `translate`, `hasTemplateIdOf` — throw sites
`src/Core/Ignixa.FhirPath/Evaluation/Functions/FhirSpecificFunctions.cs:486,506,526,546,566`) plus `%terminologies` may skip, matched on a typed
marker (e.g. a `FhirPathFunctionNotSupportedException : NotSupportedException` carrying the
function name), never on bare `NotSupportedException`.
*Falsify (the coordinator's required mutation):* delete one binary-operator switch arm in
`FhirPathEvaluator` (the `:322` region), run the suite, demonstrate cases using that operator go
**red**; restore. Until this run exists, no conformance figure is quoted anywhere — release notes,
README, or the ADR correction.

*Falsification executed (`107480e5`).* The `"xor"` arm was deleted from the `:322` switch and the same filter run
against both the pre-fix and the post-fix runner, on the same pinned corpus:

| Runner | Same mutation (`xor` arm deleted) | Result |
|---|---|---|
| pre-fix (`ce533c1c`, isolated worktree) | `--filter "FullyQualifiedName~OfficialTestSuiteRunner"` | `Passed! - Failed: 0, Passed: 2902, Skipped: 4` |
| post-fix | same filter | `Failed! - Failed: 27, Passed: 2863, Skipped: 16` |

The pre-fix runner reported a full green suite with a binary operator removed from the engine — and
the figure it reported while doing so is one of the three already in circulation. The post-fix runner
fails the 27 affected cases (nine `testBooleanLogicXOr*` cases × three versions) with
`System.NotSupportedException : Binary operator 'xor' is not yet implemented`. The arm was restored
and the suite re-run green.

*Two of the three unintended throw sites are unreachable while the engine is complete — but not for
the reason first recorded here.* `FhirPathEvaluator.cs:1268` (`Scope '$name' is not yet implemented`)
is **not** unreachable because the tokenizer restricts `$name`. It does — `FhirPathTokenizer.cs:86`
matches only `\$(this|index|total)\b`, and `$unsupportedScope` dies at tokenization — but the tokenizer
is not the only producer of a `ScopeParseNode`. `FhirPathParseTreeGrammar.cs:200` synthesises
`new ScopeParseNode("that", default)` as the focus of *every* head-position function call, and
`AstBuilder.VisitFunctionCall` visits it. The arm is unreachable only because `case "that":` exists at
`FhirPathEvaluator.cs:1245` — four names are handled, three of which the tokenizer can spell. Measured:
deleting `case "that":` turns 56 R4 cases red with
`System.NotSupportedException : Scope '$that' is not yet implemented`, which is a second reachable
bare-`NotSupportedException` site and a second demonstration that the fixed runner fails on one.

`ParseTree/ParseNode.cs:219` (`ElementAssignmentParseNode is not directly visitable`) is genuinely
unreachable: `IParseTreeVisitor` has no `VisitElementAssignment` and `AstBuilder.VisitInstanceSelector`
reads `node.Elements` directly rather than calling `Accept`. It was still reclassified to
`InvalidOperationException`, because it guards a broken invariant and `NotSupportedException` reads as
a documented capability limit — which is exactly how it would have been reported. `:322` and `:1268`
stay bare `NotSupportedException` deliberately: the runner is now required to fail on that type, so an
unimplemented feature stays loud.

*A third bare-`NotSupportedException` site is reachable from any parseable expression, and is what the
permanent guard uses.* `FhirPathFunctionGenerator.cs:415` emits the generated dispatcher's default arm,
so any unregistered function name — `Patient.notARealFunctionAtAll()` — throws
`NotSupportedException` with no engine mutation and no test seam. It is live in production:
`htmlChecks()` reaches it on every supported version through `txt-1`/`txt-2`, which is why
`FhirPathInvariantCheck.cs:248` names that function first in its own catch. This is what makes the
discriminator falsifiable by CI rather than by hand: `GivenAnUnregisteredFunction_...ThenTheCaseFails`
and `GivenAnAllowlistedFeature_...ThenTheCaseSkips` drive the runner through its public
`OfficialTestSuite_R4` entry point and pin both edges. Verified by mutation in both directions —
re-broadening the catch to `catch (NotSupportedException)` turns both red; dropping `memberOf` from the
allowlist turns the skip guard red on its own while the failure guard stays green.

*E1a — the fix closed the hole on one of the runner's two arms and left it open on the other. DONE
(`18797b52`, `4553c38e`).* Both guards above set `IsInvalidTest: false`, so both entered
`ExecuteTestCase`; an `invalid`-marked case takes `RunInvalidExpressionTest` instead, whose filter was
still a two-type denylist (`is not XunitException and not FhirPathFunctionNotSupportedException`). A
bare `NotSupportedException` satisfied it, produced `[INVALID-OK]`, and returned — E1 verbatim, across
the 114 `invalid`-marked cases. The inversion is the tell: the marker *subclass* was correctly refused
while its base type was accepted. *Falsified the same way on a different arm:* deleting the `"&"` arm
from the `:322` switch left `testConcatenate4` (`(1 | 2 | 3) & 'b' = '1,2,3b'`, `invalid="execution"`)
passing on all three versions with an engine that has no string concatenation. *Fix:* both filters are
now allowlists, and they differ by phase — the parser signals with `FormatException`, the evaluator
with `FhirPathEvaluationException`, and `FormatException` out of `Evaluate` is an engine defect rather
than a signal, so one flat list across both phases would have laundered it. With the allowlists in
place the same `"&"` deletion fails `testConcatenate4` on all three versions. The two mirror probes
`...WhenRunAsAnInvalidMarkedCase_ThenTheCaseFails` / `...ThenTheCaseSkips` pin those edges in CI; the
first was red before the fix and green after. *Newly red from this fix: zero*, and the canonical figure
did not move — a census of what actually reaches those catches across all three versions returns 79
`FhirPathEvaluationException`, 12 parse-time `FormatException` and 18 analyzer rejections, so nothing
was passing on a type the allowlists exclude.

*The skip that `testConformsTo3` reports was held by two mechanisms and pinned by neither.* Reverting
both the marker exclusion in the filter and the marker catch in `RunInvalidExpressionTest` produced
`Failed: 0, Passed: 2887, Skipped: 13, Total: 2900` — a fully green canonical suite with E1 restored,
its only trace a skip count that existed in a documentation table and nowhere else. Prose is not a
guard. `...WhenRunAsAnInvalidMarkedCase_ThenTheCaseSkips` now fails under that combined revert.

*Newly red from the fix itself: zero.* Twelve cases moved from Passed to Skipped (`conformsTo` and
`%terminologies` cases, plus `testConformsTo3`, which is `invalid`-marked and was passing because the
engine threw for not implementing `conformsTo` at all rather than for the profile URL being bogus).
Nothing turned red, so §6.1's re-baseline was a counting exercise rather than implementation work.

*Re-baseline published (2026-08-24).* The four-way split now lives in
[Official Test Suite Integration](official-test-suite-integration.md) and is the authority the other
documents cite:

| Version | Corpus | Excluded by scope | Executed | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|---:|---:|
| R4 | 935 | 0 | 935 | 930 | 0 | 5 |
| R4B | 933 | 0 | 933 | 928 | 0 | 5 |
| R5 | 1,035 | 3 | 1,032 | 1,026 | 0 | 6 |
| **Total** | **2,903** | **3** | **2,900** | **2,884** | **0** | **16** |

Canonical filter `--filter "Category=OfficialTestSuite"`, `fhir-test-cases` 1.7.46 pinned per suite
file by SHA-256, `net10.0`, at `8183b284`, re-measured unchanged at `18797b52` after the
`invalid`-path allowlist. **The filter is part of the figure.** On this commit,
against this corpus, passed counts of **2,884 / 2,890 / 2,899** and totals of
**2,900 / 2,906 / 2,915** are all reproducible, varying only by `--filter`:
`FullyQualifiedName~OfficialTestSuiteRunner` adds 6 predicate harness tests and
`FullyQualifiedName~OfficialTestSuite` adds 9 more skip-list guard tests, neither set being an
official-suite case. That is precisely how three numbers ended up in circulation, and the third
moves again every time a guard is added — it read 2,896 / 2,912 before this round added three
guards. Cross-checked against the runner's own discovery census (`Total - CDA excluded = Running`,
`Running = Passed + Skipped`, all three versions) and against the corpus files directly.

**E8 — the discovery filter's second clause can shrink the denominator silently. OPEN; DO NOT
BUNDLE WITH A PUBLISHED FIGURE.**
`test/Ignixa.FhirPath.Tests/OfficialTestSuiteRunner.cs:384-392`. `GetTestCasesForVersion` filters twice:
`.Where(tc => tc.Mode != "cda")`, which is counted and printed as `CDA excluded:` in the census line
at `:399`, and `.Where(tc => tc.InputFile is null || File.Exists(...))`, which is **not** counted and
not printed. A case whose `inputFile` is missing or renamed vanishes from the denominator with
nothing logged and nothing failing.

*Currently excludes zero cases on all three versions*, which is only knowable indirectly:
`Total - CDA excluded == Running` holds exactly in the 2026-08-24 census, so the second clause
removed nothing. That is an inference from an identity, not a measurement the runner reports —
precisely the shape of E1 one layer out. A corpus bump that renames an input file would shrink the
published conformance denominator and every guard in this repo would stay green.

*Fix (small):* count the second clause and add it to the census line as its own field, or make a
missing `inputFile` throw rather than filter. Either makes the exclusion visible.
*Falsify:* rename one `inputFile` referenced by the R4 suite, confirm the new counter reports it (or
the throw fires); restore the name exactly. Nothing else will tell you if you don't — see below.

*Deliberately not fixed in the re-baseline commit.* Changing the runner's behaviour and publishing
its number in the same commit is how an unfalsifiable figure gets made; keeping them apart is what
let the re-baseline be independently reproduced at a later commit and yield the figure published for
the earlier one. Sequence it into whichever PR next touches the runner, **after** the current figure
is merged. A pointer comment sits at the clause itself so it cannot be found only by reading this
document.

*The corpus pin does not cover this, and the deferral should not be read as though it did.*
`VerifySuiteFileHashes` pins `tests-fhir-{r4,r4b,r5}.xml` and nothing else; the archive hash pins
`testcases.zip`, which a rename in the *extracted* tree leaves untouched. E8's trigger is a missing
or renamed file under `examples/`, which no hash covers and no marker notices. So among the gaps
this document defers, E8 is the one with no compensating control at all — deferring it stays
defensible on blast radius (it excludes zero cases today, and the `Total - CDA excluded == Running`
identity makes that checkable), but not on "the hashes would catch it", because they would not.
"Checkable" here means by a person reading the census line, not by any guard: no test asserts the
identity, and the line is `Console.WriteLine` output that the default `dotnet test` logger does not
print — seeing it requires `--logger "console;verbosity=detailed"` (see
`official-test-suite-integration.md`).

**E2 — `SearchIndexParityHarness` discards Ignixa-side failures. DONE (`af451067`, `40f425ec`).**
`test/Ignixa.FhirPath.Tests/Evaluation/Parity/SearchIndexParityHarness.cs:44-48` builds the production indexer with `NullLoggerFactory`;
Ignixa-throws + Firely-legitimately-empty = 0 vs 0 = green. *Fix:* capture logger; any
contained-failure log entry on the Ignixa side is a tallied outcome class (`IgnixaContained`),
asserted zero or pinned. *Falsify:* inject an unconditional throw for one parameter (temporary
production mutation or test seam), show the corpus goes red instead of green; restore. Executed.
This is the fix that surfaced D6 (§4): once the logger was captured, the `canonical` converter gap's
46 silently-unindexed parameters stopped being indistinguishable from "both sides legitimately empty."

**E3 — `ResourceParityReport` derives `AgreementsOnValues` by subtraction. DONE (`af451067`,
`40f425ec`).**
`test/Ignixa.FhirPath.Tests/Evaluation/Parity/ResourceParityReport.cs:32-33`: a `BothThrew` counter that stops incrementing inflates the
headline and *relaxes* the `MinimumAgreementsOnValues` floor. *Fix:* count agreements positively;
`BothThrew` becomes its own asserted floor/pin. *Falsify:* mutation = stop incrementing
`BothThrew` → the positive-count floor must go red. Executed.

**E4 — `ParitySweep` has no tally. DONE (`af451067`, `40f425ec`).**
`test/Ignixa.FhirPath.Tests/Evaluation/Parity/ParitySweep.cs:38-57` (~1,400 R4 expressions × 5 resources)
records nothing; mutual throws already occur. *Fix:* same outcome-class tally as E3.
*Falsify:* make Ignixa throw for one swept expression (temporary mutation) → sweep red. Executed.

**E5 — Compiled-vs-interpreted differentials compare `TypeMatcher` to itself. DONE (`4c6cf2cf`,
`26e8ca04`).**
`test/Ignixa.FhirPath.Tests/Evaluation/SystemValueTypeMatchingTests.cs:120`, `test/Ignixa.FhirPath.Tests/Compilation/VersionedCompiledVersusInterpretedDifferentialTests.cs:92`
— both paths route `ofType()` through `FilterByType`. *Fix:* either add an independent expected-
value oracle (literal expected results per case) or retitle/re-scope the tests honestly ("compiled
and interpreted route through one matcher — this pins route equality, not correctness"). Prefer
the oracle for the small case set. *Falsify:* mutation in `FilterByType` that changes both paths
identically → the oracle version goes red, the self-comparison version demonstrably stays green
(that pair of observations is the point). Executed both runs.

**E6 — The bipartite "33.5M-graph brute-force oracle" is not committed; every shipped case is n=2.
DONE (`4c6cf2cf`, `26e8ca04`).**
*Fix:* commit a bounded property test (n ≤ 5, exhaustive or randomized-with-seed, brute-force
matcher as oracle) or delete the claim from docs/comments. *Falsify:* mutation = skip the
visited-reset in `TryPair` → property test red. Executed. The property test found the Kuhn matching
**correct** — exhaustive at n=3 and n=4 against an independent brute-force oracle, plus a
fixed-seed n=5 sample and a hand-built greedy-first-fit counterexample
(`test/Ignixa.FhirPath.Tests/Evaluation/CollectionEquivalenceTests.cs`). The previously-unbacked
"33.5M generated graphs" claim is retired in favor of this smaller but real, committed, and
independently-checkable evidence.

**E7 — Benchmark README cites a commit (`86b5cce8`) that does not exist in the branch**, with 13
`src/` commits including `src/Core/Ignixa.FhirPath/Evaluation/TypeMatcher.cs` postdating the figures
(`bench/Ignixa.Benchmarks.Firely5/README.md:5`). *Fix:* re-run the benchmark at the release
commit; restate all three numbers together — aggregate 1.72x faster, **plain paths 1.92x slower,
and plain paths are 207 of 382 entries** — or their re-measured successors; never quote the
aggregate alone. This also feeds ADR 2608's adverse-effect #1 (the adapter-input benchmark gate is
fhir-server's, but our README must not hand it a stale number). No falsification step — it is a
measurement, gated by citing the exact commit hash in the README and CI-checking that the hash is
an ancestor of the release tag (cheap script in the release checklist).

Measurement re-run order: E1 → re-baseline suite (§6.1) → E2/E3/E4 → re-run parity corpus → E7
benchmark at the release commit. Anything published (release notes, ADR correction, docs site)
quotes only post-fix numbers, **and quotes the `--filter` alongside every count** — the suite
figure moved between 2,896 and 2,906 on a fixed corpus purely by changing the filter, so a bare
count is not a measurement. E1 and the §6.1 re-baseline are done (2026-08-24); the remaining items
in this order are unchanged.

---

## 6. Passing the official HL7 FHIRPath test suite — first-class release objective

The vendored suite is **FHIR/fhir-test-cases** (`test/Ignixa.FhirPath.Tests/TestData/
fhir-test-cases/{r4,r4b,r5}/fhirpath/tests-fhir-*.xml`), not the FHIRPath IG's own tests. That is
the right suite for a FHIR server (it tracks published FHIR releases, so the §1.2 STU renames
don't apply), but its snapshot provenance is not recorded anywhere I could find — `testcases.zip`
sits beside the tree with no version marker. **Pin the snapshot**: record the fhir-test-cases
commit/release in a README next to the data and assert it (hash of the xml files) in a test, so
"passes the official suite" names *which* suite.

### 6.1 Step zero: re-baseline on an honest runner — **DONE (2026-08-24)**
E1 landed first, then all three versions were re-run and the four-way split published:
**2,884 passed / 0 failed / 16 skipped with recorded reasons / 3 excluded by scope**, of 2,903
corpus cases, under `--filter "Category=OfficialTestSuite"`. Full table, per-version filters, skip
enumeration and HAPI citations in
[Official Test Suite Integration](official-test-suite-integration.md).

The unknown quantity this step existed to measure — **cases newly red once laundering stops** — came
back **empty**. No binary operator or scope was being propped up by the old catch. Twelve cases moved
Passed → Skipped and nothing broke, so this section was cleanup, not implementation work. The
superseded "2,900 runnable / 2,887 asserted / 9 pass-throughs / 4 skipped" was never a baseline: it
was a hand subtraction on top of an instrument that could not produce the number.

### 6.2 Two bars, kept separate
- **Bar A (release blocker):** every case whose expression surface is reachable from a shipped
  search-parameter expression, in any supported version, passes. Anything normative that goes red
  after E1 is a blocker regardless of reachability.
- **Bar B (conformance claim):** the full suite passes minus a short, recorded, justified
  exclusion list. Bar B gates the *claim* "passes the official FHIR test cases", the user's stated
  ideal — it does not gate shipping the package if the only misses are the recorded exclusions
  below.

### 6.3 The 9 pass-throughs: convert to genuine skips, do not implement
`conformsTo()` ×6 and `%terminologies` ×3. Neither appears in the FHIRPath spec at all (§1.2
item 8 — zero occurrences in index.md); both are FHIR-supplement functions requiring profile
validation / terminology-service infrastructure (`src/Core/Ignixa.FhirPath/Evaluation/Functions/FhirSpecificFunctions.cs:486` and the
`%terminologies` path); neither is reachable from any shipped search parameter in any version
(verified by grep across the five generated definition files, this session). Implementing them for
a FHIRPath package release is scope creep into validation/terminology subsystems. **Verdict:
genuine `Skip` with recorded rationale and a self-retiring guard** — the skip is keyed to the
typed not-supported marker (E1), so the day `conformsTo` gets implemented the skip mechanism finds
nothing to catch and the guard test (asserting the skip list matches the actually-throwing
function set) goes red, forcing the entry's removal. A pass-through that reads as a pass is the
worst option and dies with E1. The conformance claim then reads: "passes N of N runnable cases;
9 cases skipped pending terminology/profile services, listed with rationale" — honest and stable.

### 6.4 The 4 version-policy skips: keep, with HAPI citations attached — **DONE (2026-08-24)**
Already genuine skips with recorded reasons (`test/Ignixa.FhirPath.Tests/OfficialTestSuiteRunner.cs:442-477`,
guarded by `SkipUnlessTheCaseWouldNowPass` at `:504`; the mechanism that reports a real xunit skip is
`SkipTest` at `:840`). Line references re-verified 2026-08-24 — the previous `:342-363` / `:674`
citations had rotted and were carried under this DONE stamp until the review caught them.
- **`testFHIRPathAsFunction21` (R4/R4B)** — Ignixa enforces the `as` singleton rule only from R5,
  "because HL7's own R4/R4B SearchParameters violate it" (`:363`). Now Tier-2-confirmed: HAPI sets
  `doNotEnforceAsSingletonRule = true` for pre-R5 (FHIRPathEngine.java:237-242) — HAPI would not
  enforce it on R4 content either. The published R4/R4B expectation contradicts both engines'
  reading of R4. Keep the skip; add the HAPI citation to the reason string; file upstream at
  FHIR/fhir-test-cases (issue, low effort, optional).
- **`testPlusDate19` (R4/R4B)** — **checked, and it lands on the second branch.** HAPI ships
  three version-specific engine copies rather than a runtime flag, and `dateAdd` differs between
  them exactly here: the R4 and R4B engines' seconds arm is `result.add(Calendar.SECOND, value)`
  with `value = q.getValue().intValue()` and nothing else
  (`org.hl7.fhir.r4/src/main/java/org/hl7/fhir/r4/fhirpath/FHIRPathEngine.java:2752-2756`, R4B
  identical), so `+ 0.1 's'` truncates to `.000`; the R5 engine adds the integer seconds and then
  re-adds the fractional remainder as `(int)(decValue * 1000)` milliseconds
  (`org.hl7.fhir.r5/.../fhirpath/FHIRPathEngine.java:2906-2916`), giving `.100`. **HAPI truncates on
  R4/R4B and satisfies the published expectation. The corpus is not wrong here.** This is therefore
  *not* the contradicts-both-engines category that `testFHIRPathAsFunction21` is in: Ignixa ships one
  engine following R5 semantics, and the skip is recorded as "Ignixa deliberately more
  R5-spec-compliant than the R4 expectation", not as an upstream corpus bug. Do not file this one
  upstream.

### 6.5 CDA exclusion: legitimate, make it a recorded scope decision — **DONE (2026-08-24)**
The runner "Filter[s] like the Firely validator: exclude only CDA mode"
(`test/Ignixa.FhirPath.Tests/OfficialTestSuiteRunner.cs:375-383`) and prints the excluded count
(`:399`, re-verified 2026-08-24; the previous `:279` / `:297` citations had rotted); `hasTemplateIdOf`
throws not-supported (`src/Core/Ignixa.FhirPath/Evaluation/Functions/FhirSpecificFunctions.cs:566`, "CDA support is out of scope"). Correct for
a FHIR server package. Recorded as a deliberate scope decision with per-version counts (R4 0,
R4B 0, R5 3) in [Official Test Suite Integration](official-test-suite-integration.md), reconciled
against the runner's own `CDA excluded:` census line.

**One residual, registered as E8 below rather than left as prose here.** The discovery filter's
second clause drops a case whose `inputFile` resolves to no file on disk, with no counter. See E8.

### 6.6 Falsification for the whole section
The E1 operator-arm deletion run (suite goes red) is the load-bearing proof. Additional: remove
one entry from the skip allowlist → the corresponding case must go red (skip only fires from the
list); add a bogus entry naming an implemented function → guard test red. Execute all three.

---

## 7. Release-gating checklist for `publish-release.yml`

Mechanics first, because they constrain sequencing: `publish-release.yml` is manual
`workflow_dispatch` and **downloads the NuGet artifacts of the latest successful `ci.yml` run on
`main`** (`.github/workflows/publish-release.yml`, `workflow: ci.yml … branch: main`). Therefore
the release content is "whatever main last built" — #427 (and every PR below marked pre-release)
must be **merged to main with CI green** before dispatch; there is no release-from-branch path.
Latest published is 0.6.41 (`release/0.6.41` = `c22ca789`, 2026-07-27); #398 (`4e760f23`, ADR
2608's named prerequisite) has never shipped; #427 adds public API
(`src/Core/Ignixa.Abstractions/Structure/ISystemValueElement.cs`, plus `IgnixaElementAdapter` /
`FirelyPrimitiveValues` / `FhirTemporal` changes), so the package fhir-server needs is
#427-inclusive. Version: next minor (0.7.0) given the new public interface; 0.x semver makes this
a judgment call — say so in the release notes.

**Hard blockers (all true before dispatch):** was 9 at the plan's writing; D4 leaving the list
(measurement resolved it — see §4) makes 8; the D6 canonical indexing gap discovered while fixing the evidence
base makes it 9 again. D4 is **not** on this list — see its entry in §4 for why.

1. D1 (string-literal regression) merged. — dangerous-direction write-path regression.
   **DONE** (`b7611520`, `e6023789`).
2. D2 (`repeat()` cap) merged. — tenant-suppliable unbounded loop on the write path. Not yet done.
3. §3.6 (`%resource`/`%rootResource`/`%context` explicit-binding precedence) merged — 5 shipped
   composite components and the seam's context bridge depend on it. Not yet done.
4. E1 merged and the suite re-baselined (§6.1); Bar A green; any post-E1 normative reds fixed.
   **Re-baseline DONE** (2026-08-24, 2,884/0/16/3 under `Category=OfficialTestSuite`); newly-red
   set from the fix was empty, so there were no post-E1 normative reds to fix. Merge to `main` not
   yet done.
5. E2–E4 merged and the parity corpus re-run green with positive counting. **DONE** (`af451067`,
   `40f425ec`) — and this is the fix that surfaced D6 below: once the harness could see Ignixa-side
   failures instead of laundering them into agreement, the missing `canonical` converters stopped
   being invisible.
6. **D6 (new) — the `canonical` indexing gap, resolved or explicitly accepted as a named gap in
   Ignixa's own search fidelity.** 46 shipped SearchParameters index nothing in Ignixa (§4, D6).
   The work is `Ignixa.Search` production work owned by
   [PR #430](https://github.com/brendankowitz/ignixa-fhir/pull/430), out of scope for #427 itself.
   It gates this repo's **index-parity language**, not the seam: per §4 D6 as corrected, flipping
   fhir-server's provider does not change these rows, because fhir-server's own converters run
   either way. So the requirement here is that no release note or parity claim published from this
   repo asserts index fidelity while the gap is open. Silence is not an option — that is exactly
   the failure mode this evidence-base repair exists to close.
7. **Firely floor resolved (explicit answer):** the shipped `src/Core/Extensions/Ignixa.Extensions.FirelySdk5` targets
   `Hl7.Fhir.Base` **5.13.1**
   (`C:/w427/src/Core/Extensions/Ignixa.Extensions.FirelySdk5/Directory.Packages.props:14`)
   while every parity measurement link-compiles the same sources at **5.11.4**
   (`test/Ignixa.FhirPath.Tests/Ignixa.FhirPath.Tests.csproj:94-95`), and fhir-server runs 5.11.4.
   A 5.13.1 dependency floor would force fhir-server's Firely up past the version its own
   characterization tests pin (`Scalar` throw semantics are version-sensitive — SDK 6 changed
   them). **Decision: lower the package floor to 5.11.4** (standard lowest-supported-version
   practice), verify it compiles at 5.11.4 (the comment says 5.13.1 was a "compatibility target" —
   if an API used exists only in 5.13.x, that changes the answer and must be found by the
   downgrade build, not assumed), and add a CI matrix leg that builds+tests the adapter at both
   5.11.4 (floor) and 5.13.1 (current 5.x) so neither combination is ever untested again.
   Multi-targeting is not the tool here — this is a dependency-version floor, not a TFM problem.
   *Falsify:* packaging test asserting the nuspec dependency range floor is 5.11.4; mutation =
   bump the props version → red. Plus the two CI legs actually executing (check the run, not the
   yaml). Not yet done.
8. #427 + all of the above merged to main; `ci.yml` green on main; artifact version.txt matches
   the intended release version. Not yet done.
9. Release notes drafted quoting **only** post-fix measurements (E-series) and carrying the
   §3.3/§3.4 divergence framing verbatim, plus the D6 converter-gap framing where it does not. Not
   yet done.

**Should-fix (ship without only with a recorded decision):**
- D3 (`Long` spelling + census), D5 (casing-policy alignment). Not yet done.
- E5 (differential oracle), E6 (bipartite property test or claim deletion). **DONE** (`4c6cf2cf`,
  `26e8ca04`) — E6 in particular found the Kuhn matching **correct**: exhaustive at n=3 and n=4
  against an independent brute-force oracle, plus a seeded n=5 sample and a hand-built
  greedy-first-fit counterexample, all in
  `test/Ignixa.FhirPath.Tests/Evaluation/CollectionEquivalenceTests.cs`. The previously-unbacked
  "33.5M generated graphs" claim is retired; this is different and smaller evidence (n≤5, not
  millions), but it is real, committed, and independently checkable — where the prior claim was
  neither.
- E7 (benchmark re-run, README commit fix). Not yet done.
- Residuals WI-1 (#423 analyzer fix + sweep) and WI-2/3/4 per `pr427-residuals.md` — folded into
  sequencing below by reference, not restated.
- §6.4 `testPlusDate19` HAPI verification; fhir-test-cases snapshot pinning (§6 preamble).
- DeId `Predicate` reconciliation (§3.1).

**Document-and-ship (no code):**
- Pre-R5 capitalised casts: deliberate, Ignixa+HAPI-correct, additive-rows enablement note (§3.3).
- Collection `~` matching semantics vs HAPI (§3.5) — **done**, including the decided-divergence pin
  and its firely-parity.md entry (§4 D4 context; see `firely-parity.md` entry 13).
- `Scalar`/`Predicate`/`IsBoolean` API-surface differences and the seam derivation story (§3.1/3.2).
- STU date-component rename drift vs the FHIRPath CI build (§1.2 item 7).
- Skip/exclusion register for the official suite (§6.3–6.5).
- gap-analysis.md superseded header (§1.1).
- **ADR 2608 correction PR to microsoft/fhir-server** (the branch is ADR-only, so this is a text
  PR): (a) "2906 of 2906" is false — replace with the re-baselined figure and the skip register,
  never a laundered number. **The replacement text now exists** (2026-08-24): 2,884 passed / 0
  failed / 16 skipped with named reasons / 3 excluded by scope, of 2,903 corpus cases, under
  `--filter "Category=OfficialTestSuite"` against `fhir-test-cases` 1.7.46 pinned per suite file by
  SHA-256. Note what the corrected figure implies for the ADR beyond the arithmetic: 2906 was never
  a pass count at all — it is the *total* under a filter that also sweeps in 6 predicate harness
  tests, so "2906 of 2906" restated the denominator twice and counted this repository's own tests
  about the runner as conformance. The corrected claim must carry its filter and its corpus hash or
  it will drift again. **Nothing in microsoft/fhir-server is changed by this document** — ADR 2608
  lives in a separate repo and its correction is a separate decision; (b) the `%context` paragraph is factually inverted
  (`src/Core/Ignixa.FhirPath/Evaluation/EvaluationContext.cs:449-451` binds it by name ahead of the dictionary) — and after §3.6 the
  corrected statement is "explicit bindings win"; (c) `resolve()` appears in 75 distinct shipped
  expressions, not 76; (d) the composition snippet passes a schema into `ElementSearchIndexer`,
  which arms the two schema-gated TypeMatcher errors the indexer deliberately avoids
  (`src/Core/Ignixa.Search/Indexing/ElementSearchIndexer.cs:61-81` — the unset-Schema comment is load-bearing) — the snippet must
  match the shipped composition; (e) "index rows written under either provider stay valid" gets
  the §3.3 additive-rows framing and the §3.4 R5-instant qualification; (f) the package
  prerequisite is a release containing **#427**, not merely #398; (g) **new, and methodological rather than a
  retraction** — Ignixa's published parity numbers do **not** adjudicate the converter pipeline,
  because the corpus hands one converter-manager instance to both indexers and therefore doubly
  evaluates only `Select` (§4 D6). So ADR line 167's justification cannot rest on figures published
  from this repo; it must come from fhir-server's own two-provider corpus, which ADR lines 163-164
  already specify. **Do not** write into the ADR that line 167 is false for the 46 D6 parameters —
  an earlier revision of this document said so and it was a category error: `Ignixa.Search`'s
  converter manager never executes inside fhir-server, so the provider flag does not move those
  rows. (e) remains the only correction line 167 needs.

---

## 8. Sequencing into PRs

Residuals plan (`pr427-residuals.md`) PRs 1–3 are incorporated by reference; its WI numbering is
reused unchanged. New PRs:

| PR | Content | Stands alone because | Effort |
|---|---|---|---|
| **N1** | E1 runner honesty + §6 re-baseline + skip conversion/guards + snapshot pin | Changes the headline number everyone downstream quotes; its PR description carries the operator-arm-deletion red run | 2–3 d |
| **N1b** | E8 — give the discovery filter's input-file clause a counter (or make it throw) | Must land **after** N1, never with it: the re-baseline's independent reproducibility depends on N1 moving no runner behaviour | <0.5 d |
| **N2** | E2+E3+E4 parity-evidence repairs + corpus re-run | Test-only, one theme; its description carries the injected-throw red runs | 2–3 d |
| **N3** | D1 parse-layer literal fix | Production parser/analyzer semantics; nothing rides with it | 1–2 d |
| **N4** | D2 `repeat()` cap (+dedup check). **D4 cut** — resolved by measurement, no guard needed | Evaluator resource guard on the write path; throws `FhirPathEvaluationException` per WI-4's pin, so it lands **after or with** residuals PR 3 (#428 affirmation) — the tier decision is the same conversation | 1 d |
| **N5** | §3.6 environment-variable binding precedence | Production evaluation-context semantics; seam-facing contract | 1–2 d |
| **N6** | D3 + D5 + type-name census | Consistency fixes, one theme | 1 d |
| **N7** | Firely floor: props change to 5.11.4, CI matrix leg, nuspec test (H7) | Packaging; must not ride with semantics changes | 1 d |
| **N8** | E5+E6+E7: differential oracle, bipartite property test, benchmark re-run + README | Measurement hygiene batch; last, at (near-)release commit | 1–2 d |
| **N9** | Docs: firely-parity/corpus updates, divergence register, gap-analysis supersession; and the fhir-server ADR-correction PR | Text only; after all numbers are re-run | 1–2 d |

Residuals PR 1 (WI-1, #423 sweep) can proceed in parallel with N1–N2; residuals PR 2 (WI-2/3) after
PR 1; residuals PR 3 (WI-4) before or with N4 as noted. Order-critical chain:
**N1 → (re-baseline) → N1b → any newly-red normative fixes → N3/N4/N5 → N6/N7 → N8 → merge train to
main → CI → N9/ADR correction → dispatch `publish-release.yml`.**
Total new work ≈ 11–16 working days plus residuals' 4–6; call it 3–4 engineer-weeks, long pole
being N1's unknown (newly-red set) and the two corpus re-runs.

---

## 9. What could not be verified (stated plainly)

- Firely 5.11.4's collection-`~` algorithm was not read; unnecessary — §3.5 is decided at tiers
  1–2, and the divergence tests pin observed behaviour regardless.
- HAPI's `testPlusDate19` behaviour (fractional-second date arithmetic on R4) — deliberately left
  as the §6.4 verification step, not asserted.
- Whether any 5.13.1-only API blocks the H7 floor downgrade — decided by the downgrade build.
- The FHIR R5 implicit-range mandate for millisecond instants (§3.4) — the verification read is
  the work item; the verdict text above brackets both outcomes.
- fhir-test-cases snapshot provenance — unknown; that is why pinning it is a work item.
- Whether official-suite cases exist that would distinguish §3.5's duplicate-multiset semantics —
  asserted unlikely, verified during N9's doc write-up by grep, as noted.

## 10. Do not do this

1. **Do not chase STU/ballot surface to raise a percentage.** Normative correctness and the
   shipped search-parameter surface gate the release; the §1.2 `yearOf()` rename is the standing
   proof that ballot functions churn under you. Specifically: do not implement `conformsTo`,
   `%terminologies`, `memberOf`, `validateVS`, `translate`, the Long *literal*, or the renamed
   date components for this release.
2. **Do not "fix" Ignixa back to a Firely behaviour where spec and HAPI agree Ignixa is right.**
   Pre-R5 capitalised casts (§3.3) are the canonical case: Firely silently drops indexable values;
   Ignixa and HAPI keep them. Matching the bug would be re-introducing silent data loss to make a
   diff smaller.
3. **Do not quote any of the three broken measurements before re-running them** — the conformance
   count (laundered by E1's bug), the parity agreement count (inflated by E3's subtraction and
   blinded by E2's null logger), and the benchmark figures (unattributable commit, E7). This
   includes the ADR correction: a corrected-but-still-laundered number must not ship as the fix.
4. **Do not implement the seam's `Predicate`/`Scalar`/`IsBoolean` semantics inside the engine.**
   ADR 2608 derives them once from `Select` in fhir-server precisely to remove that drift class;
   duplicating Firely semantics in Ignixa reintroduces two sources of truth.
5. **Do not pass a schema into `ElementSearchIndexer`** to match ADR 2608's snippet — the unset
   schema is load-bearing (`src/Core/Ignixa.Search/Indexing/ElementSearchIndexer.cs:61-81`); the ADR snippet is what gets fixed.
6. **Do not patch D1 in the analyzer switch.** The defect is that two byte-identical AST shapes
   need distinguishing; the only sound fix is at the parse layer. A smarter sniffer is the same
   bug with better aim.
7. **Do not close any item on "reviewed the diff/test".** Every falsification step above is an
   executed mutation with an observed red, recorded in the PR. Base rate here: six tests that
   passed but could not fail, four consecutive self-certified commits that were not clean.
8. Residuals plan §"Do not do this" items 1–7 carry over unchanged (axis conflation, the
   System.Quantity member map, `AnalyzeChild` tightening, inventory-from-TheoryData, tier-by-
   message pinning, and the rest).
