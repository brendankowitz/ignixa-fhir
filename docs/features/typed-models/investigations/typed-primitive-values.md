# Investigation: Typed Primitive Values

**Feature**: typed-models
**Status**: Implemented — see [ADR-2610](../adr-2610-typed-temporal-values.md)
**Created**: 2026-08-13

> **Implemented (2026-08-13).** `IElement.Value` now returns `FhirTemporal` for `date`/`dateTime`/
> `instant`/`time`, scoped to temporals as recommended (`Quantity` untouched). See
> [ADR-2610](../adr-2610-typed-temporal-values.md) for the decision record — including the
> conformance argument (Finding (j)) that made this a pre-1.0 requirement rather than a deferred
> refactor, and the two open design points (the `Value` property's silent-null shape at partial
> precision, and the sealed-class-vs-struct allocation cost) flagged for revisit before 1.0.

## Approach

Should `IElement.Value` carry **typed value objects** for temporal primitives (as Firely's
`ITypedElement.Value` carries `P.Date` / `P.DateTime` / `P.Time`), instead of the raw wire `string`
it carries today?

Concretely: introduce an Ignixa-native readonly value type for temporals that holds the original
wire literal **and** a parsed precision, and return it from `IElement.Value` for `date`, `dateTime`,
`instant`, and `time`. Comparison, boundary, and arithmetic helpers would pattern-match on that type
rather than re-deriving precision from the string on every operation.

### Scope: this is the *evaluation* axis, not the *serialization* axis

This investigation deliberately does **not** revisit [primitive-fidelity](primitive-fidelity.md).
That investigation asked whether the typed-model facade layer (`ResourceJsonNode` / generated POCOs)
round-trips primitives byte-exactly, and concluded dates-as-`string` is lossless there. That verdict
stands and is not in dispute.

The question here is different: whether `IElement.Value` — the input to **FHIRPath evaluation** — is
well-served by the same representation. Fidelity and evaluation are different jobs, and Finding (a)
below shows the codebase already pays for conflating them.

## Findings

### (a) The two layers Firely separates, Ignixa collapses into one

Firely runs two interfaces with two jobs:

| Interface | Property | Job |
|-----------|----------|-----|
| `ISourceNode` | `Text` (string) | fidelity — the literal off the wire |
| `ITypedElement` | `Value` (typed) | evaluation — `P.Date`, `P.Decimal`, `bool`, ... |

Ignixa has the analogous pair (`ISourceNavigator` + `IElement`) but **collapses both roles into
`IElement.Value`**. Having one slot for two jobs forces a single representation, and Ignixa picked
the fidelity-shaped one. Everything downstream compensates.

The compensation is not hypothetical. The evaluator says so itself, in a comment
(`FhirPathEvaluator.cs:1301-1305`):

```csharp
if (leftValue is string leftStr && rightValue is string rightStr)
{
    // Try to treat as typed dates first if they look like dates
    // This handles cases where type info is lost or implicit conversion is expected
    if (IsDateTimeString(leftStr) && IsDateTimeString(rightStr))
```

`IsDateTimeString` (`FhirPathEvaluator.cs:851-873`) is a heuristic that sniffs whether a string
"looks like" a date by checking for a leading `@`, a leading `T` + digit, or four leading digits.
It exists only because the type was dropped at the boundary and has to be guessed back.

### (b) Ignixa *does* parse temporals — just repeatedly, and then throws the result away

The stated rationale for keeping temporals as `string` is that parsing is lossy: `DateTimeOffset`
cannot represent `"1974"`. True. But Ignixa doesn't avoid parsing — it defers it, then re-does it at
every operation. `EvaluateDateTimeArithmetic` (`FhirPathEvaluator.cs:1735-1780`) is the full cycle:

```csharp
dateTimeStr = dateTimeStr.StartsWith("@", ...) ? dateTimeStr.Substring(1) : dateTimeStr;  // 1. strip
var precision = GetDateTimePrecision(parseStr);                                            // 2. re-derive precision
if (!TryParseFhirDateTime(parseStr, out var dt)) return [];                                // 3. parse
result = quantity.Unit switch { "a" ... => dt.AddYears(...), ... };                         // 4. compute
var resultPrecision = MaxPrecision(precision, unitPrecision.Value);                         // 5. re-derive
var resultStr = FormatDateTimeWithPrecision(result, resultPrecision, dateTimeStr, ...);     // 6. re-format
```

Step 6 then recovers the timezone by scanning the *original string again*, with a magic-number
heuristic (`FhirPathEvaluator.cs:1785-1787`):

```csharp
var hasTimeZone = originalStr.Contains('+', ...) ||
                  (originalStr.Contains('-', ...) && originalStr.LastIndexOf('-') > 10) ||
                  originalStr.EndsWith("Z", ...);
```

The parsed `DateTimeOffset` and the computed precision are both discarded at the end. The next
operation on the result starts from the string again.

`GetDateTimePrecision` is called from **9 sites** across two files
(`FhirPathEvaluator.cs:751,752,1128,1129,1380,1381,1744` and `BoundaryFunctions.cs:586,657`), each
performing `Split('-')`, `Count(c => c == ':')`, `Substring`, and `Contains('.')` on a string whose
precision was fully determined the moment it was read.

### (c) The compensation logic is large, and has already been duplicated

Roughly **500 of the 1,935 lines** in `FhirPathEvaluator.cs` are temporal string manipulation —
19 private helpers including `IsDateTimeString`, `GetDateTimePrecision`, `MaxPrecision`,
`TruncateToDateTimePrecision`, `TruncateTimePortion`, `HasNonZeroAdditionalPrecision`, `HasTimezone`,
`RemoveTimezoneForComparison`, `NormalizeMillisecondPrecision`, `GetDateTimeLowerBound`,
`GetDateTimeUpperBound`, and `FormatDateTimeWithPrecision`.

Because that logic is keyed to the string rather than to a type, it has already forked. Two helpers
now exist in **two incompatible copies**:

| Helper | Copy 1 | Copy 2 | Divergence |
|--------|--------|--------|------------|
| `GetDateTimePrecision` | `FhirPathEvaluator.cs:1455` → `DateTimePrecision` enum | `BoundaryFunctions.cs:741` → `int` (4/6/8/10/12/14/17) | different return type, different semantics; copy 2 deliberately folds hour-precision into minute (`:763-765`) |
| `IsDateTimeString` | `FhirPathEvaluator.cs:851` | `BoundaryFunctions.cs:794` | separate implementations of the same heuristic |

Two implementations of "what precision is this?" that disagree is a defect surface, not a style nit.

### (d) The recurring bug class: type and value travel separately, so helpers silently lose one

Because the discriminator (`InstanceType`) and the payload (`Value`) are separate fields, any helper
accepting a bare `object` needs a parallel type parameter. Omitting it doesn't fail to compile — it
returns an **empty collection**, which FHIRPath treats as a legitimate result. Silent wrong answers.

Four instances found:

1. **`CompareDateTimeEquality`** (`FhirPathEvaluator.cs:1099-1118`) — narrows `object?` to `string`
   via a switch whose default arm is `_ => null` (`:1106`, `:1114`), then bails on null (`:1117`).
2. **`CompareDateTimesWithPrecision`** (`FhirPathEvaluator.cs:1346-1362`) — a byte-identical switch
   with the same `_ => null` arms (`:1353`, `:1361`).
3. **The Firely adapter boundary** — passing `P.Date` straight through to Ignixa hit exactly those
   `_ => null` arms. This is what [PR #398](https://github.com/brendankowitz/ignixa-fhir/pull/398)
   fixes, by translating at the boundary.
4. **`Quantity` — still open.** `TryExtractQuantity` (`FhirPathEvaluator.cs:646-650`) tests
   `element.Value is Types.Quantity`. With no `using` alias in that file, `Types.Quantity` resolves
   to Ignixa's own `Ignixa.FhirPath.Types.Quantity` (`src/Core/Ignixa.FhirPath/Types/Quantity.cs:23`),
   so a Firely `P.Quantity` arriving through the adapter can never match. Reproduced:
   `obs.ToTypedElement().Select("value.toQuantity()").ToIgnixaElements()` where `$this = 5 'mg'`
   yields `count=0`, empty.

Instance 4 is the tell. Three fixes in, the class is still producing new instances.

### (e) The fidelity-vs-typing dichotomy is false — Firely is typed *and* lossless

The premise "typing temporals costs fidelity" only holds if you type into a container that can't
carry the source literal. Firely doesn't. `P.DateTime.ToString()` returns
`OriginalParsedString ?? ToStringWithPrecision(...)` — verified against decompiled `Hl7.Fhir.Base`
6.0.1 and 5.13.1. The wire literal is retained *alongside* the parsed value and precision.

So `"1974"` round-trips through Firely as `"1974"`, and simultaneously supports
`is System.Date`, `+ 1 year`, and partial-precision comparison. Ignixa concluded typing was lossy
after evaluating only BCL types (`DateTimeOffset`), which is where the loss actually lives.

### (f) Ignixa already accepts lossy typed parsing — for decimal

`SchemaAwareElement.Value` (`SchemaAwareElement.cs:173-190`) is the authoritative contract:

```csharp
return InstanceType switch
{
    "boolean" => bool.TryParse(text, out var b) ? b : text,
    "integer" or "unsignedInt" or "positiveInt" => int.TryParse(text, out var i) ? i : text,
    "decimal" => decimal.TryParse(text, out var d) ? d : text,
    _ => text     // ← temporals fall through here
};
```

`decimal` is typed despite [primitive-fidelity](primitive-fidelity.md) finding (c) documenting that
`decimal?` **silently rounds** past ~28–29 significant digits and **throws** past ~7.9e28 — a real,
if rare, fidelity loss. Temporals are left as `string` to avoid a fidelity loss that
[Finding (e)](#e-the-fidelity-vs-typing-dichotomy-is-false--firely-is-typed-and-lossless) shows is
avoidable. The current split is a case-by-case judgement, not a principle.

### (g) Ignixa already ships this exact pattern — for Quantity

This is the decisive precedent. `QuantityElement` (`FunctionHelpers.cs:378-390`) returns a
non-BCL typed value object straight out of `IElement.Value`:

```csharp
public sealed class QuantityElement : IElement
{
    private readonly Types.Quantity _quantity;
    public string InstanceType => "Quantity";
    public object Value => _quantity;      // ← custom typed value object
}
```

And `Types.Quantity` (`src/Core/Ignixa.FhirPath/Types/Quantity.cs:23-50`) already carries value, unit,
**and an optional precision field**:

```csharp
public sealed class Quantity : IEquatable<Quantity>, IComparable<Quantity>
{
    public decimal Value { get; }
    public string Unit { get; }
    public int? Precision { get; init; }
}
```

So the proposal is not "adopt Firely's model". It is "apply the pattern Ignixa already chose for
`Quantity` to the temporal types" — the same shape, in the same slot, in the same assembly.

### (h) Capability comparison

| | Firely | Ignixa today |
|---|---|---|
| Precision | parsed once into a field | re-derived per operation by string splitting (9 call sites, 2 divergent impls) |
| Partial-precision comparison | falls out of `TryCompareDateTimeParts` | hand-rolled in `CompareDateTimesWithPrecision` |
| `is System.Date` / `type().name` | conformant | conformant via `InstanceType`; `GetFhirPathTypeName` fallback is narrow |
| Calendar arithmetic (`+ 1 year`) | native on the value | full parse/compute/re-format cycle (Finding (b)) |
| Quantity / UCUM | `P.Quantity` | `Types.Quantity` — already typed (Finding (g)) |
| Source fidelity | preserved (`OriginalParsedString`) | preserved |
| Adapter boundary | — | needs explicit translation both ways (PR #398) |

Firely's typed values are not buying fidelity — both sides have that. They're buying **precision
carried with the value**, which is what removes the compensation layer.

### (i) There are *two* evaluation paths, not one — and the cost analysis above only counted one

Findings (b) and (c) size the compensation layer as ~500 of the 1,935 lines in `FhirPathEvaluator.cs`.
That undercounts, because the interpreter is not the only consumer of `IElement.Value`.

`TypedElementExtensions.Select()` (`:64`) — the entry point `CLAUDE.md` directs all resource data
access to, and the one search-parameter extraction runs through — tries a **compiled delegate first**
and only falls back to the interpreter:

```csharp
private static readonly FhirPathDelegateCompiler _delegateCompiler = new(new FhirPathEvaluator());   // :33
return _delegateCache.GetOrAdd(expressionString, _ => _delegateCompiler.TryCompile(ast));            // :53
```

`FhirPathDelegateCompiler` is 683 lines with its **own** independently-implemented comparison
semantics — a bare `Equals(pair.First.Value, pair.Second.Value)` at three sites, and a `CompareValues`
that does an ordinal string compare. So the "two divergent copies of `GetDateTimePrecision`" in
Finding (c) understates the divergence: comparison itself is forked across an interpreter and a
compiler, and the two do not agree.

Two consequences, both confirmed empirically when Option 3 was implemented:

1. **The migration surface is larger than Finding (c) implies.** Changing `IElement.Value` broke the
   compiled path too — `Patient.where(birthDate = '1974-12-25').exists()` silently stopped matching,
   because the compiler's `Equals` compared a `FhirTemporal` against a `string`. Any audit scoped to
   `FhirPathEvaluator.cs` would have missed it, and did.
2. **The paths already disagree on `main`, independent of this work.** Because the compiler keeps the
   `@` sigil in a temporal constant's value and compares ordinally, `Patient.birthDate = @1974-12-25`
   evaluates `false` compiled and `true` interpreted. Same for `Observation.issued > @2024-01-01T00:00:00Z`.
   This predates PR #399 and is not caused by it.

Item 2 is a live correctness bug on `main` and deserves its own investigation — fixing it properly
requires the compiled path to be able to return *empty* for indeterminate partial precision, but
`CompileComparison` is typed `Func<object?, object?, bool>` and structurally cannot. That is a design
change, not a parity fix, and it is out of scope here.

The relevance to this investigation: a typed temporal value makes the two paths *able* to share one
comparison implementation (`FhirTemporal.Compare`), which neither Option 1 nor Option 4 offers. That
is a point in Option 3's favour the original analysis did not credit it with.

### (j) The strongest argument was missed entirely: the spec mandates it

Everything above argues the change on maintainability — compensation cost, bug class, divergent helpers.
Those are real but they are *engineering* arguments, and they are why the original verdict could
plausibly land on "not worth the refactor today". The conformance argument is stronger and was not
made.

FHIR's FHIRPath profile (`https://hl7.org/fhir/fhirpath.html`, §2.1.9) defines the mapping from FHIR
primitives to FHIRPath System types:

| FHIR primitive | System type |
|---|---|
| `date`, `dateTime`, `instant` | `System.DateTime` |
| `time` | `System.Time` |
| `string`, `uri`, `code`, `oid`, `id`, `uuid`, `markdown`, `base64Binary` | `System.String` |

`date` is **not** on the `System.String` list. Handing a temporal to the FHIRPath engine as a string
was a conformance gap, not merely an inelegant representation — and it is the direct cause of the
downstream symptoms catalogued in Findings (a) through (d), which are consequences rather than
independent problems.

Firely reached the same conclusion in practice: `Patient.birthDate.Value` is
`Hl7.Fhir.ElementModel.Types.Date`, not a string. Verified empirically against `Hl7.Fhir` 4.3.0,
5.11.4 and 6.0.1 — byte-identical behaviour across three majors.

The observable consequence of the gap, also verified across those three Firely versions and against
HAPI's `FHIRPathEngine`:

| | `Patient.birthDate + 1` | `Patient.birthDate.toInteger()` |
|---|---|---|
| Firely (all three majors) | throws | empty |
| HAPI (`operateTypes` **and** `opPlus`) | throws | n/a |
| FHIRPath spec | error | empty |
| Ignixa, dates-as-`string` | **`1975`** | **`1974`** |

Ignixa silently produced a plausible wrong number where the entire ecosystem refuses the operation.
That is the worst available failure mode in a clinical query language, and it was a direct consequence
of `date` presenting as `System.String`, exactly as the profile forbids.

This reframes the verdict below. The original weighed a maintainability benefit against a refactor
cost and reasonably concluded "wait for more pain". Conformance is not a maintainability preference to
be traded off, and the trigger conditions never contemplated it.

## Options

### Option 1 — Do nothing

Keep `IElement.Value` returning `string` for temporals; keep fixing `_ => null` sites as found.

Zero cost now. Accepts that the bug class in Finding (d) stays open and that Findings (b), (c)
compound: every new temporal function adds string-parsing, and the duplicated helpers keep drifting.

### Option 2 — Translate at boundaries only (status quo + PR #398)

Normalize typed values to Ignixa's string contract at every adapter edge, as PR #398 does for the
Firely SDK5/SDK6 adapters.

Correct and cheap, and the right immediate move. But it's per-boundary: each new interop surface
needs its own translation layer, and the Quantity instance (Finding (d4)) shows a boundary can be
"done" while still leaking.

### Option 3 — Ignixa-native temporal value type

Add a readonly struct in `Ignixa.FhirPath.Types` (sibling to the existing `Quantity`) holding the
wire literal plus a parsed precision enum; return it from `IElement.Value` for `date`, `dateTime`,
`instant`, `time`.

- Lossless by construction — the literal is retained, per Finding (e).
- Precision computed once at construction, killing the 8 re-derivation sites and letting the two
  divergent `GetDateTimePrecision` copies collapse to one.
- Comparison helpers pattern-match on the type instead of needing a parallel `instanceType`
  parameter — retires the bug class in Finding (d), including the open Quantity instance.
- Ignixa-native, no `Hl7.Fhir.*` dependency — consistent with `CLAUDE.md`'s layer rule, and with the
  precedent [ADR-2510](../../adr/adr-2510-capability-sourcenode-model.md) set when it chose
  SourceNode-based POCOs over the Firely SDK for CapabilityStatement.

Cost is real: `IElement.Value`'s contract changes, so every consumer that assumes `string` for a
temporal must be found and updated. Note the documented contract (`IElement.cs:29-41`) already reads
`dateTime/date/instant → DateTimeOffset or string` — an "or" that obliges every consumer to handle
both shapes, which is precisely the ambiguity Finding (d) turns into silent empty results. It also
omits `time` and `integer64` entirely. Serialization must be verified untouched; per Finding (a) it
reads `ISourceNavigator`, not `IElement.Value`, so it should be, but "should be" is not evidence.
Needs a migration shim and a full `dotnet test All.sln` gate.

### Option 4 — Keep `string`, but delete the ambiguity

Leave temporals as `string`, and make that a *commitment* rather than a default: strike the
`DateTimeOffset` arm from the `IElement.Value` contract, remove the code paths that produce it, and
require every adapter boundary to normalize inbound typed values (the PR #398 pattern, applied as a
rule rather than case-by-case).

This is Option 1's cost with Option 3's main *contract* benefit. It does not remove the ~500 lines of
compensation, does not merge the divergent `GetDateTimePrecision` copies, and does not retire the
Finding (d) bug class — a helper can still receive a bare `object` without its `instanceType`. What it
does remove is the `or`: consumers stop having to handle two shapes, so the silent-empty failure mode
loses its ambiguity at the public boundary.

Relevant because Option 1 ("do nothing") and Option 4 are *not* the same choice once the contract is
frozen — see [Timing](#timing-the-pre-10-window).

## Timing: the pre-1.0 window

The options above weigh benefit against cost as if cost were constant. It is not.

`IElement` ships in `Ignixa.Abstractions`, which sets `IsPackable=true`
(`src/Core/Ignixa.Abstractions/Ignixa.Abstractions.csproj:12`). `Directory.Build.props:57-63` sets
`IsPackable=false` only for projects whose name contains `Test`, so packability here is an explicit
opt-in rather than an override of a repo-wide default. Either way `IElement.Value` is shipped public
API, not an internal detail. The latest release tag is `release/0.6.41` — pre-1.0.

That makes the cost of an `IElement.Value` change a **step function**, not a constant:

- **Before 1.0** — a changelog entry and an internal audit of temporal consumers.
- **After 1.0** — a major version bump plus downstream migration for every package consumer and
  extension author.

Three consequences:

1. **The trigger conditions below are mis-specified in isolation.** Each is a "wait for more pain"
   condition, which is only the right call if the pain arrives *before* the version boundary. Waiting
   past 1.0 means paying materially more for an identical fix.
2. **Reversibility is asymmetric.** Shipping the typed shape and getting the details wrong is a patch
   fix — the shape is right. Shipping `string` and later wanting typed is a breaking change. Per
   `CLAUDE.md`'s reversibility principle ("can we undo this decision in 2 weeks?"), the asymmetry
   favours acting inside the window.
3. **"Do nothing" is not available for 1.0.** The contract today reads `dateTime/date/instant →
   DateTimeOffset or string`. Shipping 1.0 with that `or` freezes an *ambiguous* value contract at a
   public boundary, obliging every consumer to handle both shapes forever or carry a latent
   silent-empty bug (Finding (d)). Option 1 stops being a null action the moment the contract is
   frozen. Options 3 and 4 are the two ways to resolve the ambiguity; they differ in how much of the
   compensation cost they also retire.

## Tradeoffs

| Pros | Cons |
|------|------|
| Retires the `_ => null` bug class (4 known instances, Finding (d)) — type and value stop travelling separately | Breaking change to the `IElement.Value` contract; every temporal consumer must be audited |
| Removes ~500 lines of string-parsing compensation and lets two divergent helper copies collapse to one (Finding (c)) | Migration must prove serialization fidelity is untouched — the whole point of the current design |
| Precision computed once, not per operation across 9 call sites (Finding (b)) | Real refactor cost against a system whose tests currently pass |
| Extends a pattern Ignixa already ships and validated for `Quantity` (Finding (g)) | Temporal semantics are subtler than `Quantity`'s; new type is easy to get wrong |
| Lossless — the wire literal is retained, as Firely proves is achievable (Finding (e)) | Consumers pattern-matching `is string` on temporals break **silently**, not loudly — see the note below |
| Ignixa-native, consistent with `CLAUDE.md`'s layer rule and the ADR-2510 precedent | Reimplements what Firely already solved, for dependency reasons rather than technical ones |

**Correction, recorded from the implementation attempt**: the "break loudly" assumption above was
wrong, and wrong in the direction that matters. When Option 3 was implemented, every call site still
gating on `is string` — arithmetic (`EvaluateAddition`/`EvaluateSubtraction`), `toDate`/`toDateTime`,
`join()`, and the `>`/`<` type gates missing `instant`/`time` — failed by returning an **empty
collection**, not by throwing or failing to compile. `TryGetSingleString` was the sole exception, and
it threw. The full 4,024-test suite stayed green throughout, because the FHIRPath conformance suite
exercises `@`-literals (still `string`) and never routes a *parsed resource's* `IElement.Value`
through those functions.

That is the same silent-empty failure mode as Finding (d), reproduced by the fix for Finding (d).
The lesson is not that Option 3 is wrong — it is that `object? Value` makes the migration itself
un-typecheckable, so the migration needs resource-backed test coverage **before** the contract
changes, not after.

## Alignment

- [x] Follows architectural layering rules — no `Hl7.Fhir.*` dependency, consistent with
      `CLAUDE.md`'s layer rule and the ADR-2510 precedent. (As implemented the type landed in
      `Ignixa.Abstractions.Structure`, not `Ignixa.FhirPath.Types` as sketched here, because
      `IElement.Value`'s declared contract lives in `Ignixa.Abstractions`.)
- [~] Developer Experience — removes the "did I remember to pass `instanceType`?" trap, but *not* by
      making wrong usage a compile error. `IElement.Value` is still `object?`, so a missed call site
      degrades to an empty collection exactly as before; only `TryGetSingleString` failed loudly.
      See the correction under Tradeoffs.
- [x] Specification compliance — improves it. Partial-precision comparison and calendar arithmetic
      are FHIRPath-spec behaviours currently hand-rolled per call site.
- [x] Consistent with existing patterns — `QuantityElement` / `Types.Quantity` is the same shape
      already in production (Finding (g)).

## Evidence

Read on `main` at `ca5c65b7` unless noted.

**The collapsed layer and its compensation**
- `src/Core/Ignixa.FhirPath/Evaluation/FhirPathEvaluator.cs:1301-1305` — comment: *"This handles
  cases where type info is lost"*
- `src/Core/Ignixa.FhirPath/Evaluation/FhirPathEvaluator.cs:851-873` — `IsDateTimeString` heuristic
- `src/Core/Ignixa.FhirPath/Evaluation/FhirPathEvaluator.cs:1735-1780` — full arithmetic cycle
- `src/Core/Ignixa.FhirPath/Evaluation/FhirPathEvaluator.cs:1785-1787` — `LastIndexOf('-') > 10`
  timezone heuristic
- `src/Core/Ignixa.FhirPath/Evaluation/FhirPathEvaluator.cs:1443-1500` — `DateTimePrecision` enum +
  `GetDateTimePrecision`; called from `:751,752,1128,1129,1380,1381,1744`
- `src/Core/Ignixa.FhirPath/Evaluation/Functions/BoundaryFunctions.cs:741-772` — second, incompatible
  `GetDateTimePrecision` returning `int`; `:794` — second `IsDateTimeString`

**The bug class**
- `src/Core/Ignixa.FhirPath/Evaluation/FhirPathEvaluator.cs:1099-1118` — `CompareDateTimeEquality`,
  `_ => null` at `:1106`, `:1114`
- `src/Core/Ignixa.FhirPath/Evaluation/FhirPathEvaluator.cs:1346-1362` —
  `CompareDateTimesWithPrecision`, `_ => null` at `:1353`, `:1361`
- `src/Core/Ignixa.FhirPath/Evaluation/FhirPathEvaluator.cs:646-650` — `TryExtractQuantity` matching
  Ignixa's own `Types.Quantity`; open defect
- [PR #398](https://github.com/brendankowitz/ignixa-fhir/pull/398) — the boundary fix

**The second evaluation path (Finding (i))**
- `src/Core/Ignixa.FhirPath/Evaluation/TypedElementExtensions.cs:33,53,64` — `Select()` prefers a
  compiled delegate over the interpreter
- `src/Core/Ignixa.FhirPath/Evaluation/FhirPathDelegateCompiler.cs` — 683 lines, its own `Equals`
  and ordinal `CompareValues`; `CompileComparison` is typed `Func<object?, object?, bool>` and so
  cannot express FHIRPath's indeterminate-precision empty result

**The contract and the precedent**
- `src/Core/Ignixa.Serialization/SourceNodes/SchemaAwareElement.cs:173-190` — `Value` getter;
  temporals fall through `_ => text`
- `src/Core/Ignixa.Abstractions/Structure/IElement.cs:29-41` — documented contract:
  `dateTime/date/instant → DateTimeOffset or string` (omits `time`, `integer64`)
- `src/Core/Ignixa.FhirPath/Evaluation/Functions/FunctionHelpers.cs:378-390` — `QuantityElement`
  returning a typed object from `Value`
- `src/Core/Ignixa.FhirPath/Types/Quantity.cs:23-50` — `Types.Quantity` with a `Precision` field

**Versioning surface**
- `src/Core/Ignixa.Abstractions/Ignixa.Abstractions.csproj:12` — `IsPackable=true` (explicit opt-in;
  `Directory.Build.props:57-63` sets `IsPackable=false` only for `*Test*` project names, so this is
  not an override of a repo-wide default); `IElement` is shipped public API
- `git tag --list "release/*"` → latest `release/0.6.41`; pre-1.0 as of this investigation

**Firely, verified against decompiled `Hl7.Fhir.Base` 6.0.1 / 5.13.1**
- `P.DateTime.ToString()` → `OriginalParsedString ?? ToStringWithPrecision(...)` — typed *and*
  lossless
- `P.DateTime.Parse` ≈ 3446 ns; native `ITypedElement.Value` ≈ 172 ns; `ToString()` ≈ 9 ns —
  parse cost is paid once, not per comparison
- `Parse` fails only with `FormatException` across 28 malformed inputs × 3 parsers

**Prior art in this repo**
- [primitive-fidelity](primitive-fidelity.md) — serialization axis; finding (c) documents `decimal`
  loss, finding (e) the dates-as-string verdict this investigation scopes against
- [ADR-2510](../../adr/adr-2510-capability-sourcenode-model.md) — scoped to CapabilityStatement, but
  the precedent for preferring Ignixa-native SourceNode models over the Firely SDK. The repo-wide
  prohibition on `Hl7.Fhir.*` in Application/DataLayer is in `CLAUDE.md`, not this ADR; together they
  rule out simply adopting `P.Date`

## Verdict

**The design is defensible and the diagnosis of *why* is wrong.**

Keeping temporals as strings does preserve fidelity. But Finding (e) shows fidelity was never the
tradeoff — Firely is typed and lossless simultaneously. The loss lives in `DateTimeOffset`, not in
typing. Ignixa evaluated one candidate container, found it lossy, and generalised. Finding (f)
confirms it wasn't applied as a principle anyway: `decimal` is typed despite documented loss.

The cost is measurable and compounding: ~500 lines of compensation, two already-divergent copies of
the same helper, precision re-derived at 9 sites, and a bug class (Finding (d)) that has produced
four instances with one still open.

**Recommendation — not a decision; per the Transformer Mandate this is a human call:**

- **Now**: land [PR #398](https://github.com/brendankowitz/ignixa-fhir/pull/398) (Option 2). It is
  the correct tactical fix and independent of this question.
- **Next**: fix the Quantity instance (Finding (d4)) as a bug, not as part of a refactor.
- **Before 1.0**: resolve the `IElement.Value` contract ambiguity — Option 3 or Option 4, but not
  Option 1. See [Timing](#timing-the-pre-10-window) for why deferral is not neutral here.
- **Option 3 is the right long-term shape**, and Finding (g) means it is an extension of an existing
  Ignixa pattern rather than a new architecture. Scope it to temporals (`date`, `dateTime`,
  `instant`, `time`); leave `Quantity` alone, since it already works.

**On sequencing**: if the 1.0 schedule does not allow doing Option 3 carefully, take Option 4. A
hastily-designed typed model, frozen at 1.0, is worse than a frozen `string` — temporal semantics are
subtler than `Quantity`'s, and the Tradeoffs table lists "easy to get wrong" for good reason. The
thing that must not ship is the `or`.

**Trigger conditions for escalating to Option 3** — any one is sufficient, and all are now bounded by
the 1.0 boundary rather than open-ended:

1. A **fifth** instance of the Finding (d) bug class appears.
2. The two `GetDateTimePrecision` implementations produce a **user-visible disagreement**.
3. A new FHIRPath temporal function is required that would add materially to the ~500 lines
   (`lowBoundary`/`highBoundary` on new types, timezone-aware comparison, calendar-duration `between`).
4. A second interop adapter is added, requiring a third copy of the PR #398 translation layer.
5. **1.0 is scheduled.** This is a deadline, not a symptom: it closes the window in which conditions
   1–4 can be answered cheaply.

Note that an earlier revision of this investigation concluded "I would not spend the refactor on
today's evidence," with conditions 1–4 as open-ended waits. The benefit evidence has not changed;
the *cost* side has. Conditions 1–4 assumed a constant refactor cost, which the packability and
version facts above disprove.
