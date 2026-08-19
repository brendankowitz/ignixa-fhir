# ADR-2610: Typed Temporal Values on IElement.Value

**Status**: Accepted (implemented)
**Date**: 2026-08-17
**Feature**: typed-models

## Context

`IElement.Value` — the input to FHIRPath evaluation, validation, and search extraction — returned
the raw wire `string` for `date`, `dateTime`, `instant`, and `time`, on the stated rationale that
typing a temporal necessarily loses fidelity (`DateTimeOffset` cannot represent `"1974"`).
[typed-primitive-values](investigations/typed-primitive-values.md) investigated whether that
representation still held up and found:

- The fidelity-vs-typing tradeoff was never real: Firely is typed *and* lossless, because it keeps
  the wire literal alongside the parsed value (Finding (e)). Ignixa evaluated one lossy candidate
  container (`DateTimeOffset`) and generalised from it.
- Ignixa already ships the pattern this ADR adopts, for `Quantity`: `QuantityElement.Value` returns
  a non-BCL typed object (Finding (g)).
- Collapsing fidelity and evaluation into one `string`-shaped slot forced ~500 lines of
  string-parsing compensation in `FhirPathEvaluator.cs` (precision re-derived at 7 call sites in
  that file — 9 across both files together with `BoundaryFunctions.cs` — two already-divergent
  copies of `GetDateTimePrecision`), and produced a recurring silent-empty bug
  class: four found instances of a helper narrowing `object?` to `string` and returning nothing on
  a type it didn't expect (Findings (a)-(d)).
- **The strongest argument surfaced late (Finding (j)) and is a conformance gap, not a style
  preference.** FHIR's FHIRPath profile (§2.1.9) maps `date`/`dateTime`/`instant` to `System.DateTime`
  and `time` to `System.Time`; only `string`/`uri`/`code`/`oid`/`id`/`uuid`/`markdown`/`base64Binary`
  map to `System.String`. `date` is not on that list. Presenting a temporal as `System.String` let
  `Patient.birthDate + 1` silently produce `1975` where Firely (all three verified majors), HAPI, and
  the spec itself all require the operation to error or return empty. Firely reached the same
  conclusion independently: `Patient.birthDate.Value` is `Hl7.Fhir.ElementModel.Types.Date`, not a
  string.

`IElement` ships in `Ignixa.Abstractions` (`IsPackable=true`, `PackageStability=stable`), and the
repo is pre-1.0. A contract change here is a changelog entry today and a major-version migration
after 1.0 — the investigation's [Timing](investigations/typed-primitive-values.md#timing-the-pre-10-window)
section treats that as a step function, not a constant, and argues for resolving the ambiguity
before the window closes rather than waiting for more "trigger conditions."

## Decision

`IElement.Value` returns a typed `FhirTemporal` for `date`, `dateTime`, `instant`, and `time`,
carrying the wire literal and the parsed precision together — the investigation's Option 3, scoped
to temporals only. `Quantity`'s precedent is narrower than that: `QuantityElement.Value` already
returns a non-BCL typed object, but that type has since moved assemblies (to `FhirQuantity` in
`Ignixa.Abstractions`) and shed `IComparable`, `Precision`, and the `Add`/`Subtract`/`ConvertTo`/
`CanCombineWith`/`DivideBy` members it used to carry.

`FhirTemporal` (`src/Core/Ignixa.Abstractions/Structure/FhirTemporal.cs`) is a sealed class,
constructed only via `TryParse` (malformed wire data is expected input, not a programmer error, so
there is no public constructor that could yield a half-populated instance). It exposes:

- `Literal` — the wire text verbatim (the `@` sigil stripped), the fidelity half of the type.
- `Precision` — computed once at construction, not re-derived per operation.
- `Kind` — the FHIR primitive the literal was read as.
- `HasTimezone` — whether the literal's time-of-day carried an offset, scanned from the literal
  itself rather than inferred, because a fixed instant and a floating local time compare
  indeterminately in FHIRPath and normalising to UTC erases the distinction.

There is deliberately no resolved-instant member; see the design note below.

Comparison (`FhirTemporal.Compare`) implements FHIRPath's tri-state partial-precision ordering
directly on the type, replacing the hand-rolled string-splitting helpers.

## Consequences

**Positive**

- Closes the conformance gap: a temporal now presents as a temporal to FHIRPath, not as
  `System.String`, matching the spec's type profile and Firely's independent implementation.
- Fidelity is not traded away — `Literal` round-trips the source exactly, including partial
  precision (`"1974"` stays `"1974"`), which is what made this an extension of the existing
  `Quantity` pattern rather than a new tradeoff.
- Precision computed once at parse time collapses the two divergent `GetDateTimePrecision`
  implementations and removes the re-derive-per-operation cost the investigation measured.

**Negative — the contract is a union, not a single type**

`IElement.Value` was already documented as `DateTimeOffset or string` for temporals before this
change; that ambiguity is not eliminated, it is extended. A consumer must now tolerate:

- `FhirTemporal`, from `SchemaAwareElement` and everything built on it — the primary case.
- `string`, when the wire literal fails to parse (`TryParse` never throws; unparseable input falls
  back to the raw string rather than dropping the element).
- `DateTimeOffset` or `DateTime`, tolerated from third-party `IElement` implementations that predate
  or don't adopt `FhirTemporal`.

FHIRPath `@`-literals inside expressions are still parsed as raw strings by the tokenizer, not as
`FhirTemporal` — they only become comparable to a resource-backed temporal at evaluation time.
`WireValue.AsWireString` (`src/Core/Ignixa.FhirPath/Evaluation/WireValue.cs`, internal) is the single
normalization chokepoint the engine's string-oriented paths route through to reconcile the two; it
exists precisely because the union has more than one shape carrying a lexical form.

**One design point was flagged for revisit before 1.0 and has since been resolved by deletion.**
`FhirTemporal.Value` was a `DateTimeOffset?` that returned `null` at `Year`/`Month` precision and for
every `time` — structurally the same silent-empty shape this type was built to eliminate, and one
dereference away from colliding with `IElement.Value`. Rather than rename it to `Instant`, which
would have relabelled the ambiguous null without removing it, the member is gone.

The deciding evidence was that it had no consumer. Renaming it and building `All.sln` produced
compile errors in exactly two files, both of them tests of `Value` itself — nothing in `src/`,
`tools/`, the benchmarks, or the Firely adapter shims. Since `FhirTemporal` had not yet shipped in
any package, removing it broke nothing that existed.

Nothing is lost with it: `Literal` is the wire truth, `Precision` states what is known, and the
ordering bounds already back comparison. A caller needing a `DateTimeOffset` must say *which* one —
lower bound, upper bound, or a UTC normalisation — so if the need arises it returns as a member named
for the answer it gives, not as a bare `Value` whose meaning changes with precision. (No such member
is exposed today: the ordering bounds backing comparison, `_lowerBound` and `_upperBound`, are
private fields, not a public API.)

**One design point remains flagged for revisit before 1.0, not deferred indefinitely:**

1. **`FhirTemporal` is a sealed class, not the `readonly struct` the investigation sketched.** That
   costs an allocation per temporal per navigation on hot paths (every `IElement.Value` read for a
   date/dateTime/instant/time element). The class shape was taken because `FhirTemporal?` needs
   reference-type null semantics against the existing `object? Value` contract without a second
   nullable-wrapping layer; revisiting it means resolving that against `IElement.Value`'s type,
   which is a larger change than the removal above.

**`InternalsVisibleTo` grants a pre-1.0 API-surface decision, made deliberately:**

- `Ignixa.Abstractions` → `Ignixa.FhirPath`, so the evaluator can reach `FhirTemporal.GetLiteralPrecision`
  and `FhirTemporal.IsTemporalLiteral` without making them public.
- `Ignixa.FhirPath` → `Ignixa.Search`, so search indexing can reach `WireValue.AsWireString` instead
  of duplicating its `string`/`FhirTemporal`/`DateTimeOffset` table.

Both are internals a third-party `IElement` implementation would also need to interoperate correctly
with the same normalization rules. Granting them via `InternalsVisibleTo` to in-repo consumers only,
rather than making them public now, is a scope decision worth revisiting deliberately before 1.0 —
either by publicizing the surface or by accepting that out-of-repo `IElement` implementers reimplement
it — rather than letting it happen by accretion as more internal consumers are added.

## Implementation status

Implemented. `FhirTemporal` and `FhirTemporalPrecision` ship in `Ignixa.Abstractions.Structure`;
`SchemaAwareElement.Value` and the FHIRPath evaluator (`FhirPathEvaluator`, `BoundaryFunctions`,
`DateTimeFunctions`, `AggregateFunctions`) consume `FhirTemporal` directly or via `WireValue`;
`Ignixa.Search`'s indexing converters and the Firely SDK adapters translate at the boundary. Tests
live in `Ignixa.Abstractions.Tests` (moved there from `Ignixa.Serialization.Tests` to keep the
`InternalsVisibleTo` pointed at the assembly that actually owns the type). Shipped as part of the
0.7.0 breaking-change release.

## References

- [typed-primitive-values investigation](investigations/typed-primitive-values.md) — findings,
  options, and the pre-1.0 timing argument this ADR decides.
- [ADR-2510: CapabilityStatement Without Firely SDK](../../adr/adr-2510-capability-sourcenode-model.md)
  — the Ignixa-native-over-Firely-dependency precedent this decision follows.
- `src/Core/Ignixa.Abstractions/Structure/FhirTemporal.cs`
- `src/Core/Ignixa.FhirPath/Evaluation/WireValue.cs`
