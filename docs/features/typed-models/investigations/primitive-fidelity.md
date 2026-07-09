# Investigation: Primitive Round-Trip Fidelity (decimal, date/time)

**Feature**: typed-models
**Status**: Complete — recommendation implemented
**Created**: 2026-06-14

> **Implemented (2026-06-14).** The raw-`JsonNode` escape-hatch recommended below now ships: the generator emits a `…Raw` accessor (e.g. `Quantity.ValueRaw`) alongside `decimal?` for every `decimal` element. The characterization tests were ported out of the (now-retired) spike to `test/Ignixa.Models.R4.Tests/PrimitiveFidelityTests.cs`.

## Approach

Empirically characterize whether the JSON-primary typed-model runtime preserves FHIR primitive
precision across `parse -> typed get/set -> serialize`, with focus on `decimal` (FHIR mandates
preserving significant digits, trailing zeros, and exponent form) and the date/time family.

Method: a characterization test class,
`spike/typed-models/Ignixa.Models.Spike.Tests/PrimitiveFidelitySpikeTests.cs` (24 tests, all
passing), exercising the actual runtime — `ResourceJsonNode.Parse` + raw `MutableNode` +
`GetProperty<decimal?>`/`SetProperty` + the hand-written `R4.Quantity` (which mirrors what the
generator's `EmitPrimitive` emits for `decimal`). Tests assert the *observed* behavior, so the file
is a living record of reality, not aspiration.

### Runtime under test

| Stage | Mechanism |
|---|---|
| parse | `ResourceJsonNode.Parse` -> `ResourceConverter.Read` -> `JsonNode.Parse(ref reader)`. Number leaves become `JsonValue` instances backed by a `JsonElement` — the **raw token text is retained**. |
| typed get | `GetProperty<decimal?>("value")` -> `JsonValue.GetValue<decimal?>()` (materializes a CLR `decimal`). |
| typed set | `SetProperty("value", value)` -> `JsonValue.Create(decimal)` (leaf is now backed by a CLR `decimal`, the raw token is gone). |
| serialize | `MutableNode.ToJsonString()`. |

## Findings

### (a) Untouched round-trip preserves raw decimals — byte-for-byte. ✅

`JsonNode.Parse` keeps the original number token verbatim on every untouched leaf. `ToJsonString()`
writes it back unchanged. Verified for `185.0`, `1.230`, `1.00000000000000000000000001`, the exponent
form `1.00e2` (emitted literally as `1.00e2`, not normalized), `100`, and the same through the real
resource `Parse` path. **If you never call the typed `decimal` accessor, fidelity is perfect.**

### (b) CLR `decimal` exposure preserves trailing zeros — within decimal's capacity. ✅ (bounded)

`System.Decimal` carries its own scale, so trailing zeros survive materialization:

- `GetValue<decimal>()` on `185.0` yields a decimal whose `ToString()` is `"185.0"`; `1.230` -> `"1.230"`.
- Reading is **non-destructive**: `GetValue<decimal>()` does not swap the underlying leaf, so "read
  the decimal, edit a sibling, serialize" leaves the original token intact.
- On the **set** path, `JsonValue.Create(185.0m)` preserves the literal's scale -> serializes as
  `185.0`; `1.230m` -> `1.230`. Trailing zeros survive a set **provided the value is expressible as a
  decimal literal**.

Surprise worth recording: `1.00000000000000000000000001` (27 significant digits) is **not** lossy —
it fits inside decimal's 28–29 sig-digit capacity and round-trips exactly. "High precision" alone is
not the loss boundary; decimal's capacity is.

### (c) Where loss occurs — beyond `System.Decimal`'s capacity. ⚠️

Two distinct failure modes once a FHIR decimal exceeds what `System.Decimal` can hold:

| Input | `GetValue<decimal>()` behavior | Consequence |
|---|---|---|
| `0.12345678901234567890123456789012345` (35 sig digits) | **Silently rounds** to ~28–29 digits, no exception | Precision dropped; raw token and materialized value disagree. |
| `1e40` (magnitude > ~7.9e28) | **Throws** | The typed read fails outright. |

The loss is purely on the **materialize-as-decimal** boundary — it bites on the typed get (rounds or
throws) and is baked in before any serialization. The set path is bounded identically:
`decimal.Parse("0.1234…012345")` rounds *before* `JsonValue.Create` ever runs, so there is no way to
push a >29-digit FHIR decimal through the `decimal?` accessor without losing it first.

FHIR `decimal` is unbounded in precision and magnitude; `System.Decimal` is not. So `decimal?` is
lossy at the spec's extremes — silently for high precision, fatally for large magnitude.

### Escape hatch (proves the fix is cheap). ✅

Bypassing the CLR `decimal` entirely preserves arbitrary precision:
`quantity.SetProperty("value", JsonNode.Parse(rawText))` writes the raw node; the 35-digit value
survives byte-for-byte. Confirms a raw-`JsonNode`-backed accessor would be fully faithful.

### Sibling-field mutation is lossless for untouched fields. ✅

The realistic edit case: mutating a *different* field (`Quantity.unit`, or `Observation.status`)
never reformats the untouched decimal — its `JsonElement`-backed leaf is left alone and serializes
verbatim (`185.0`, `1.230`, and even the 27-digit value all stay intact). Editing one field does not
disturb others.

### (e) Dates / dateTimes / instants as `string` — confirmed lossless. ✅

Surfaced as `string`, stored and emitted verbatim by STJ with no `DateTimeOffset` parsing or
normalization. Verified for full dates (`1974-12-25`), partial dates (`1974`, `1974-12`), and an
instant with fractional seconds + timezone offset (`1974-12-25T14:35:45.123-05:00`) across
read/set/serialize/reparse. String treatment is correct and trivially faithful — partial-date and
offset semantics are preserved precisely *because* we never parse them.

## Tie-back to the generator (`EmitPrimitive`)

`codegen/Ignixa.Specification.Generators/CSharpTypedModelLanguage.cs`:

- `decimal` is a primitive but **not** in `StringLikePrimitives`, so it takes the simple branch:
  `get => GetProperty<decimal?>("value"); set => SetProperty("value", value);` — exactly the lossy
  path characterized above. `MapPrimitiveToClr` maps `"decimal" => "decimal"`.
- `date`, `dateTime`, `time`, `instant` **are** in `StringLikePrimitives`, so they are emitted as
  `PrimitiveElement<string>` — the lossless path confirmed in (e).

So the generator's current `decimal` choice inherits the (c) loss at the spec's extremes; its
date/time choice is already correct.

## Recommendation

**`decimal?` is acceptable as the *ergonomic* typed accessor, but it must not be the only door to the
value, and it must never be the path an untouched value travels.**

Concretely, for the generator's `EmitPrimitive` decimal branch:

1. **Keep raw fidelity the default.** Untouched decimals already round-trip perfectly (finding a) —
   the generator must preserve this by *never* materializing a value it isn't asked to. The current
   `get`/`set`-only shape already honors this: nothing touches the leaf unless the caller uses the
   accessor. Keep it that way; do not add eager normalization.
2. **Expose a raw-preserving setter alongside `decimal?`.** Mirror the string-like primitives:
   generate a `PrimitiveElement`-style or `…Element`/raw-node accessor for decimal so callers can
   read/write the underlying `JsonNode` (or a `string`) and preserve >29-digit / large-magnitude
   values. The escape-hatch test shows this is a one-line `SetProperty(name, JsonNode)` away.
3. **Make the `decimal?` accessor honest about its bounds.** Document that `decimal?` rounds beyond
   ~28–29 significant digits and throws beyond ~7.9e28. Either let the `OverflowException` surface
   (current behavior — fail loud, acceptable) or, better, route the typed getter through the raw node
   so the common case never risks the throw and only callers who *opt into* `decimal` accept its
   limits.

A dedicated `FhirDecimal` string-backed wrapper (Firely-style) is **not** warranted yet: it adds a
type for a problem that (1) only manifests beyond `System.Decimal`'s already-generous range and (2) is
fully solved by keeping untouched values raw + offering a raw-node accessor. Revisit only if a real
dataset surfaces >29-digit decimals flowing through typed *setters*.

**Dates as string: confirmed fine — no change needed.**

## Evidence

- Tests: `spike/typed-models/Ignixa.Models.Spike.Tests/PrimitiveFidelitySpikeTests.cs` — 24 tests,
  all passing; spike suite total 45 passing.
- Results by area:
  - (1) untouched round-trip preserves `185.0`, `1.230`, `1.00000000000000000000000001`, `1.00e2`,
    `100` byte-for-byte — **PASS**.
  - (2) typed read preserves scale (`185.0`/`1.230`); 27-digit value round-trips exactly (no
    rounding) — **PASS** (corrected a wrong initial prediction of rounding).
  - (3) 35-digit value silently rounds on `GetValue<decimal>`; `1e40` throws — **PASS** (loss
    characterized).
  - (4) set path: `185.0m`/`1.230m` serialize with trailing zeros; 35-digit value cannot be pushed
    through `decimal?`; raw `JsonNode` set preserves it — **PASS**.
  - (5) sibling mutation leaves untouched decimals byte-identical — **PASS**.
  - (6) dates/partial-dates/instant lossless as string — **PASS**.
- Generator: `codegen/Ignixa.Specification.Generators/CSharpTypedModelLanguage.cs` `EmitPrimitive`
  (decimal -> `GetProperty<decimal?>`/`SetProperty`; date family -> `PrimitiveElement<string>`).

## Verdict

The JSON-primary runtime is **faithful by default** (untouched values round-trip exactly) and the CLR
`decimal` accessor is **faithful within `System.Decimal`'s range**, which covers essentially all real
FHIR data. Loss is confined to the spec's extremes (>29 sig digits: silent rounding; >~7.9e28: throw)
and only when the value is *materialized as a CLR decimal*. Recommendation: ship `decimal?` as the
ergonomic accessor, keep untouched values raw, and add a raw-node escape hatch in the generator's
decimal branch. Dates/dateTimes/instants as `string` are confirmed lossless and need no change. This
resolves the "Primitive typing fidelity" open question from the source-generated-poco-facades
investigation for the decimal and date/time axes.
