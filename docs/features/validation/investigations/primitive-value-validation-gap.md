# Investigation: Primitive Value Validation Gap (TypeCheck vs FhirPrimitiveValidator)

**Feature**: validation
**Status**: Viable
**Created**: 2026-06-16
**Found by**: fhir-faker edge-case generation (string family, `--include-invalid`) — the first
real adversarial run produced spec-invalid string values that the validator accepted.

## Problem

The validator enforces FHIR primitive **value rules** (empty-string rejection, character grammar,
calendar-date validity) for **choice elements only** (`value[x]`). For ordinary non-choice primitive
elements — the overwhelming majority, e.g. `Patient.name.family` (string), `Patient.birthDate`
(date) — it runs a *different, weaker* checker that accepts values the FHIR spec forbids.

Concretely, a generated Patient with control characters in `name.family`, an empty-but-present
`family`, or (by the same path) an impossible calendar date in `birthDate` passes validation with
zero issues.

## Root cause: two divergent primitive validators, only one wired broadly

There are two primitive validators in `Ignixa.Validation`:

| Validator | Strictness | Where it runs |
|-----------|-----------|---------------|
| `Checks/FhirPrimitiveValidator.cs` | **Strict** — rejects empty strings (`:144`), validates calendar dates via `DateOnly.TryParseExact` (`:180`), range-checks ints, FHIR-grammar date/dateTime/time/instant regexes | **Only** `ChoiceElementCheck.cs:118-119` (i.e. `value[x]` elements) |
| `Checks/TypeCheck.cs` | **Loose** — `"string" => true` (`:179`), `code/markdown/uri => true`, no empty-string check, looser date regex (`^\d{4}(-\d{2}(-\d{2})?)?$`, `:27`) with **no** calendar validity check | **All non-choice primitive elements** |

The split is explicit at schema-build time — `Schema/StructureDefinitionSchemaBuilder.cs:130-132`:

```csharp
var typeChecks = elements
    .Where(e => e.Info.IsPrimitive && !e.Info.IsChoiceElement)   // non-choice primitives
    .Select(e => new TypeCheck(e.Info.Name, GetTypeName(e)));     // → loose TypeCheck
```

…while choice elements get `ChoiceElementCheck` (`:200`), which is the *only* caller of the strict
`FhirPrimitiveValidator`.

`FhirPrimitiveValidator`'s own summary says "primitive value validation **shared across checks**" —
the intent was broad use, but the wiring reaches one check. This reads as an incomplete rollout: the
strict validator was added (with a full conformance test suite,
`FhirPrimitiveValidatorConformanceTests.cs`) but never replaced `TypeCheck`'s primitive logic for
ordinary elements.

## Specific gaps in `TypeCheck` (non-choice primitive path)

1. **String character grammar not enforced.** `GetValidationByType` returns `"string" => true`
   (`TypeCheck.cs:179`). FHIR `string` is `[\r\n\t -￿]+` — C0 control characters
   (U+0000, U+0007, U+001B, …) are illegal but accepted. Same for `code`/`markdown`.
2. **Empty string accepted.** An empty-but-present primitive passes. `TypeCheck.cs:106-108` even
   carries a comment that "An empty string … should be validated against the type's rules," but the
   code does not do it — `FhirPrimitiveValidator.cs:144` is the one that actually rejects it.
3. **Invalid calendar dates accepted.** `TypeCheck`'s `DatePattern` has no month/day range or
   calendar check, so `2000-13`, `2000-00`, `2000-02-31` pass on `birthDate`.
   `FhirPrimitiveValidator` rejects all of these (range-constrained regex + `IsCalendarDateValid`).
   The two date regexes diverge — choice-typed dates are strict, ordinary dates are not.

These behaviors are **untested** in `TypeCheckTests.cs` (no empty-string, control-char, or
invalid-calendar-date cases), confirming they are incidental, not a specified design choice.

## Evidence (reproduced)

`ignixa-fakes r4 resource Observation --density maximal --edge-cases string,unicode --seed 5
--include-invalid --validate` →
```
mutations=15  (string.control-chars: 6, string.empty-present: 1, string.whitespace-only: 2, …)
✓ Validation passed
```
MayViolate string strategies fired on free-text (`string`-typed) fields and the validator reported
no issues. `string.whitespace-only` passing is actually **correct** (FHIR base `string` permits
whitespace; only `code`/`id` forbid it), but `control-chars` and `empty-present` passing are genuine
spec violations the validator missed.

## Impact

- **Severity: moderate.** Structural/cardinality/type-kind validation is intact (a number-where-a-
  string-belongs is still caught by `TypeCheck`'s JSON-kind check). The gap is in *value-content*
  conformance for non-choice primitives.
- A FHIR server can ingest and persist resources with control characters in text fields, empty
  primitives, and impossible dates — which then flow to clients, search indexing, and round-trip
  serialization. This is exactly the #280/#281 class (bad bytes surviving the pipeline).
- The inconsistency (choice elements strict, everything else loose) is a correctness/parity bug
  against the FHIR spec and against the validator's own stated intent.

## Options

1. **Delegate `TypeCheck`'s primitive value validation to `FhirPrimitiveValidator` (recommended).**
   Have `TypeCheck` call `FhirPrimitiveValidator.TryValidate(element, fhirType, out reason)` for the
   value-content portion after its existing JSON-kind check, mapping the failure to a `type-1` issue.
   One strict implementation, one set of conformance tests, choice and non-choice paths converge.
   Small, surgical, reversible.
2. **Inline the missing rules into `TypeCheck`.** Add empty-string rejection, the string character
   regex, and a calendar-date check directly. Rejected: duplicates `FhirPrimitiveValidator` and
   re-introduces drift — two implementations is what caused this.
3. **Do nothing / document as intentional.** Rejected: it contradicts the FHIR spec, the
   `FhirPrimitiveValidator` "shared across checks" intent, and the latent TODO comment in `TypeCheck`.

## Recommendation

Option 1. Unify on `FhirPrimitiveValidator` for all primitive elements by having `TypeCheck` delegate
value-content validation to it. Add regression tests to `TypeCheckTests` for: control chars in a
`string` field, empty-but-present primitive, and an invalid calendar date in `birthDate` — all
expected to fail. Keep `whitespace-only` on `string` passing (it is valid) but ensure `code`/`id`
whitespace is rejected via the shared validator.

This is a validation-engine change, independent of the faker. The faker's edge-case mode did its job:
it surfaced a real, previously-untested conformance gap on its first adversarial run, and the
`string.control-chars` / `string.empty-present` strategies should become regression fixtures for the
fix.

## Verdict

Confirmed gap, not a design choice. Worth an ADR-light fix in the validation feature (Option 1) plus
the three regression tests. Tracks back to the edge-case investigation
([../../fhir-faker/investigations/adversarial-data-generation.md]) as the motivating find — concrete
proof of the "validity measured, not assumed" premise.
