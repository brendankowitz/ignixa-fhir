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

Concretely, a generated Patient with an empty-but-present `family`, or (by the same path) an
impossible calendar date in `birthDate` (e.g. `2000-02-31`, `2000-13`), passes validation with zero
issues.

> **Correction (control characters).** An earlier draft listed control characters in string fields as
> a third hard gap. That was overstated. The published FHIR R4 `string` value regex is `[ \r\n\t\S]+`;
> `\S` matches C0 control characters, so control chars are **not** a hard type violation — the
> spec's "avoid characters below U+0020" guidance is prose-level SHOULD, not enforced by the type.
> The strict `FhirPrimitiveValidator` also accepts them. So `string.control-chars` passing is
> acceptable; rejecting control chars would be an optional Warning-level enhancement, out of scope
> for this fix. The two **unambiguous** Error-level gaps are empty-string and invalid-calendar-date.

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

1. **(Caveat, not a hard gap) String character handling.** `GetValidationByType` returns
   `"string" => true` (`TypeCheck.cs:179`). Per the literal FHIR `string` regex `[ \r\n\t\S]+`, this is
   acceptable for control characters (see Correction above). The only string-content rule actually
   missing here is the empty-string one — covered next.
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

`ignixa-fakes r4 resource Observation --density maximum --edge-cases string,unicode --seed 5
--include-invalid --validate` →
```
mutations=15  (string.control-chars: 6, string.empty-present: 1, string.whitespace-only: 2, …)
✓ Validation passed
```
MayViolate string strategies fired on free-text (`string`-typed) fields and the validator reported
no issues. `string.whitespace-only` and `string.control-chars` passing are actually **correct** for
`string`-typed fields (FHIR base `string` permits whitespace and, per `[ \r\n\t\S]+`, control chars;
only `code`/`id` forbid whitespace). The genuine spec violation the validator missed is
`empty-present` — and, by the same loose path, invalid calendar dates on `date`/`dateTime` elements.

## Impact

- **Severity: moderate.** Structural/cardinality/type-kind validation is intact (a number-where-a-
  string-belongs is still caught by `TypeCheck`'s JSON-kind check). The gap is in *value-content*
  conformance for non-choice primitives.
- A FHIR server can ingest and persist resources with empty primitives and impossible dates — which
  then flow to clients, search indexing, and round-trip serialization. This is the #281 class
  (bad temporal values surviving the pipeline).
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

Option 1, **additively**. `TypeCheck` keeps its existing checks (JSON-kind, and the `id`/`uri`/`url`/
`oid`/`uuid`/`canonical` format checks that `FhirPrimitiveValidator` does **not** cover) and *also*
delegates value-content validation to `FhirPrimitiveValidator`. The net effect is strictly more
rejections: empty primitives, invalid calendar dates, out-of-range date components, and stricter
integer/date-family rules now apply to non-choice elements too — matching what choice elements
already get. Add regression tests to `TypeCheckTests` for: empty-but-present primitive and an invalid
calendar date (`2000-02-31`, `2000-13`) → expected to fail; valid values (incl. partial dates like
`2000`, leap `2000-02-29`) → still pass. `whitespace-only`/`control-chars` on `string` stay passing
(valid). Control-char rejection, if ever wanted, is a separate Warning-level enhancement.

This is a validation-engine change, independent of the faker. The faker's edge-case mode did its job:
it surfaced a real, previously-untested conformance gap on its first adversarial run, and the
`string.empty-present` strategy should become a regression fixture for the fix.

## Verdict

Confirmed gap, not a design choice. Worth an ADR-light fix in the validation feature (Option 1) plus
the three regression tests. Tracks back to the edge-case investigation
([../../fhir-faker/investigations/adversarial-data-generation.md]) as the motivating find — concrete
proof of the "validity measured, not assumed" premise.
